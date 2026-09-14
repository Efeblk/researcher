# Analiz hattı

`ResearcherAnalysisService`, aynı solution içindeki bağımsız .NET 10 HTTP uygulamasıdır. Collector SQL Server'daki veriden snapshot üretir, servisi çağırır ve başarılı raporu `analysis` şemasında snapshot'ıyla saklar. AI servisi araştırmacı verisini SQL'den okumaz; kendisine gönderilen snapshot'ı işler. Gemini kullanım defteri için aynı SQL Server'a erişir.

## Araştırmacı analizi

Collector uçları:

| POST işlemi | Davranış |
| --- | --- |
| `AnalyzeResearcher` | `PersonelID` için kayıtlı veriden snapshot üretir, AI servisini çağırır ve raporu saklar. |
| `GetResearcherAnalysis` | Son başarılı raporu sağlayıcı çağrısı yapmadan getirir. |
| `GetResearcherSourceCoverage` | Mevcut kayıtlı kanıt kapsamını dış çağrı yapmadan getirir. |

İstek örnekleri [`Requests/ResearcherAnalysis.http`](../Requests/ResearcherAnalysis.http) dosyasındadır. İsteğe bağlı `snapshotAt` yalnız mevcut snapshot'ın etiketidir; geçmiş tarihli veriyi yeniden kurmaz. Başarısız üretim önceki başarılı raporu değiştirmez.

Yerel kurulumda `ResearcherAnalysisService` varsayılan olarak Ollama kullanır; aktif sağlayıcı/model ve adres için [`ResearcherAnalysisService/appsettings.json`](../ResearcherAnalysisService/appsettings.json) esas alınır. Hosted anahtarlar user-secrets ile verilir. Collector'ın analiz servisi adresi `AnalysisService:BaseUrl` ayarıdır. Production'da her iki servis de erişim kontrolü arkasında olmalıdır.

```powershell
# Development örnekleri: OpenAI araştırmacı raporu seçildiğinde
dotnet user-secrets set "Ai:ApiKey" "<OPENAI_KEY>" --project ResearcherAnalysisService

# Development: Gemini makale özeti ve collector migration'larının bulunduğu aynı DB
dotnet user-secrets set "Gemini:ApiKey" "<GEMINI_KEY>" --project ResearcherAnalysisService
dotnet user-secrets set "ConnectionStrings:UsageDatabase" "<ACADEMIC_DATABASE>" --project ResearcherAnalysisService

# Development'ta uzak/non-development davranışını denemek için iki tarafta aynı anahtar
dotnet user-secrets set "Service:ApiKey" "<SERVICE_KEY>" --project ResearcherAnalysisService
dotnet user-secrets set "AnalysisService:ApiKey" "<SERVICE_KEY>"

dotnet run --project ResearcherAnalysisService --launch-profile http
```

`ConnectionStrings:UsageDatabase` için varsayılan/fallback yoktur. Collector önce başlatılıp migration'lar uygulanmalıdır. User-secrets örnekleri yerel geliştirme içindir; production değerlerini environment/deployment secret ile verin. AI sağlayıcısını değiştirmek için ilgili `Ai:Provider`, model ve makale sağlayıcı ayarlarını da güvenli deployment yapılandırmasında belirtin.

## Kalıcı ürün iş akışları ve varsayılanlar

Collector, `/Services/AcademicPerformance/V1/` altında şu POST işlemlerini sunar: `GetArticleSummaryAutomationStatus`, `GetCanonicalArticleEvidence`, `ReviewCanonicalArticle`, `GetCanonicalArticleReview`, `StartArticleEvaluation`, `GetArticleEvaluation`, `SaveFacultyAssistantContext`, `GetFacultyAssistantContext`, `StartFacultyAssistant`, `GetFacultyAssistantRun`, `CreateHrEvidenceDossier`, `GetHrEvidenceDossier`, `AppendHrDossierReviewAction` ve `ListHrDossierReviewActions`. Kanonik yayın, metrik, arama, referans popülasyonu ve grafik işlemleri [kod rehberinde](CODEBASE_GUIDE.md) özetlenir; kesin payload örnekleri [`Requests/`](../Requests/) altındadır.

`ArticleSummaryAutomation`, `ArticleEvaluation`, `PublicationMetrics`, `FacultyAssistant` ve `BulkCollection` worker'ları kayıtlı varsayılanlarda etkindir ve beş saniyede bir yoklama yapar. Otomatik özetler en çok üç kez denenir; değerlendirme isteği 330, fakülte yanıtı 1.800 saniyeyle sınırlıdır. Kanonik makale incelemesi en çok 24 sağlayıcı çağrısı, tahmini 1,00 USD harcama, 100.000 kaynak baytı ve 600 saniye kabul eder. Bunlar sağlayıcı kotası veya fatura garantisi değil operasyon sınırlarıdır. Deployment öncesinde esas [`academicsettings.json`](../academicsettings.json) dosyasını inceleyin.

Collector'ın yönettiği kalıcı makale akışı Gemini kullanır ve ilişkilendirilebilir denemeleri `ConnectionStrings:UsageDatabase` içine yazar; bu bağlantının fallback'i yoktur ve migration uygulanmış akademik veritabanını göstermelidir. Analiz servisi araştırmacı analizi için yerel Ollama'yı, makale üretimi için `gemini-3.8-flash` modelini varsayar. Hosted kimlik bilgilerini secret olarak yapılandırın. Development dışındaki analiz servisi `Service:ApiKey` olmadan kapalı kalır; collector aynı değeri `AnalysisService:ApiKey` ile göndermelidir. Collector ürün uçları veri okumadan önce istenen özne için yetki ister. Varsayılan `IAcademicProductAccessService` uygulaması kapalı kalır; consuming host güvenilir kimlik ve kapsam uygulaması sağlamalıdır. Bu standalone hosttaki `DevelopmentPermissionService` izin veren bir adapter'dır ve production öncesinde BYS yetkilendirmesiyle değiştirilmelidir.

## Makale özeti

`SummarizeArticle` isteği `PersonelID`, o araştırmacıya ait `AcademicWorkId` ve isteğe bağlı dili alır. `GetArticleSummary` son başarılı sonucu AI çağrısı olmadan döndürür. Ayrıntılı örnekler [`Requests/ArticleSummary.http`](../Requests/ArticleSummary.http) dosyasındadır.

Kaynak edinme sırası şöyledir:

1. Aynı araştırmacının tam normalize DOI eşleşmeli kayıtlarındaki mevcut özet ve kaynak adayları toplanır.
2. PDF adayları, ardından landing page'ler sınırlı istek/redirect/boyut/süre bütçesiyle denenir.
3. Semantik makale gövdesi olan HTML kabul edilir; menü, login, paywall, yalnız abstract veya genel `body` tam metin sayılmaz.
4. Metin katmanlı PDF doğrudan çıkarılır. Eksik sayfalar ayar açıksa sınırlı Tesseract OCR kullanabilir; sayfa, DPI, piksel ve süre sınırları uygulanır.
5. Kayıtlı kaynaklar yetmezse OpenAlex, Crossref ve yapılandırılmışsa Unpaywall DOI önbelleği üzerinden bir kez zenginleştirme yapar.
6. Tam metin yoksa bulunan abstract kısmi kapsam gerekçesiyle özetlenir; yalnız metaveri bulgu üretmez.

İstekten fetch URL kabul edilmez. Her redirect ve keşfedilen adres yeniden doğrulanır; yalnız genel HTTP(S) adreslerine bağlanılır ve DNS bağlantısı doğrulanan adrese sabitlenir. Script çalıştırılmaz. API anahtarı, kayıtlı URL, sorgu dizesi ve ham transport hatası tanılara konmaz. Başarılı kaynak snapshot'ı SHA-256 özetiyle rapor yanında saklanır.

Model kaynak metni için deterministik span kimlikleri alır ve yalnız iddia ile span kimliği döndürür. Servis bilinmeyen kimlikleri reddeder; alıntı, sayfa ve offset'i kayıtlı kaynaktan kendi çözer. Ayrı model geçişi her iddiayı bağlamıyla doğrular; desteklenmeyen, belirsiz, eksik veya bütçede doğrulanamayan iddialar elenir. Destekli iddia kalmazsa durum `insufficient_evidence` olur ve başarılı rapor kaydedilmez. Bu kontrol doğruluk garantisi değildir; aynı model ailesi ilişkili hata üretebilir.

Bağlam sınırında kaynak deterministik span parçalarına bölünür; karakterler sessizce atılmaz. Güvenlik engeli, boş/malformed JSON, bilinmeyen bitiş nedeni ve beklenmeyen span kimliği fail-closed davranır.

Gemini kullanıldığında her `generateContent` denemesi gönderilmeden SQL kullanım defterine `Pending` yazılır ve sonuçta token, model, durum ve maliyet tahminiyle tamamlanır. Prompt, kaynak içerik, kişi/DOI, anahtar ve URL saklanmaz. Pending satır oluşturulamıyorsa ücretli istek gönderilmez; tamamlama yazımı başarısızsa maliyet bilinmediği için satır Pending kalır. Kullanım veritabanı collector'ın migration uyguladığı SQL Server olmalıdır.

Limitler ve model seçimi için [`academicsettings.json`](../academicsettings.json) ile [`ResearcherAnalysisService/appsettings.json`](../ResearcherAnalysisService/appsettings.json) kaynak kabul edilir. OCR hostunda Tesseract ve gerekli `eng`/`tur` dil verileri ayrıca kurulmalıdır.
