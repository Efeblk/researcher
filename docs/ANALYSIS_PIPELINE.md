# Analiz hattı

`ResearcherAnalysisService`, aynı solution içindeki bağımsız .NET 10 HTTP uygulamasıdır. Collector ürün akışında SQL Server'daki veriden snapshot üretir, servisi çağırır ve başarılı raporu kendi `analysis` tablolarında snapshot'ıyla saklar. Analysis Service araştırmacı verisini SQL'den okumaz; kendisine gönderilen tam snapshot veya kaynak bağlamını işler. Yalnız Gemini kullanım defterini aynı SQL Server'daki `analysis.GeminiUsageAttempts` tablosuna yazar ve bu tablonun migration'larını kendisi yönetir.

## Araştırmacı analizi

Collector uçları:

| POST işlemi | Davranış |
| --- | --- |
| `AnalyzeResearcher` | `PersonelID` için kayıtlı veriden snapshot üretir, AI servisini çağırır ve raporu saklar. |
| `GetResearcherAnalysis` | Son başarılı raporu sağlayıcı çağrısı yapmadan getirir. |
| `GetResearcherSourceCoverage` | Mevcut kayıtlı kanıt kapsamını dış çağrı yapmadan getirir. |

Collector orkestrasyon örnekleri [ResearcherAnalysis.http](../Requests/AcademicCollector/ResearcherAnalysis.http) dosyasındadır. İsteğe bağlı `snapshotAt` yalnız mevcut snapshot'ın etiketidir; geçmiş tarihli veriyi yeniden kurmaz. Başarısız üretim önceki başarılı raporu değiştirmez. Analysis Service'in `POST /api/v1/analyze` ucu ise `PersonelID` yanında araştırmacı adı, zaman, dil, yayın örnekleri ve metrikleri içeren tam snapshot ister; [doğrudan örnek](../ResearcherAnalysisService/Requests/ResearcherAnalysis.http) collector verisi olmadan çalışacak biçimdedir.

Yerel kurulumda `ResearcherAnalysisService` araştırmacı raporları için varsayılan olarak Ollama, makale akışları için `gemini-3.8-flash` kullanır. Ayrı çalıştırma, build/publish, user-secrets ve doğrudan API talimatları [ResearcherAnalysisService README](../ResearcherAnalysisService/README.md) dosyasındadır. Collector'ın analiz servisi adresi `AnalysisService:BaseUrl`, eşleşen servis anahtarı `AnalysisService:ApiKey` ayarıdır; Analysis Service aynı sırrı `Service:ApiKey` altında alır ve `X-Analysis-Key` header'ını doğrular.

Yerel varsayılanlarda collector'ın `ConnectionStrings:AcademicDatabase` ve Analysis Service'in `ConnectionStrings:UsageDatabase` değerleri aynı LocalDB veritabanını gösterir. Deployment'ta iki proje için aynı SQL bağlantısını ayrı ayrı güvenli yapılandırmaya verin. Veritabanı oluşturulduktan sonra servisler herhangi bir sırada veya eşzamanlı başlatılabilir: collector kendi migration'larını `dbo.VersionInfo`, Analysis Service kullanım defteri migration'larını `dbo.ResearcherAnalysisVersionInfo` geçmişiyle başlangıçta uygular. Production'da her iki servis de kendi erişim kontrolüyle korunmalıdır.

## Kalıcı ürün iş akışları ve varsayılanlar

Collector, `/Services/AcademicPerformance/V1/` altında şu POST işlemlerini sunar: `GetArticleSummaryAutomationStatus`, `GetCanonicalArticleEvidence`, `ReviewCanonicalArticle`, `GetCanonicalArticleReview`, `StartArticleEvaluation`, `GetArticleEvaluation`, `SaveFacultyAssistantContext`, `GetFacultyAssistantContext`, `StartFacultyAssistant`, `GetFacultyAssistantRun`, `CreateHrEvidenceDossier`, `GetHrEvidenceDossier`, `AppendHrDossierReviewAction` ve `ListHrDossierReviewActions`. Kanonik yayın, metrik, arama, referans popülasyonu ve grafik işlemleri [kod rehberinde](CODEBASE_GUIDE.md) özetlenir; kesin collector payload'ları [Requests/AcademicCollector](../Requests/AcademicCollector/README.md) altındadır. Bunlar özne erişimi, kuyruklar ve kalıcı ürün kayıtları için collector yüzeyidir; tam bağlamlı, doğrudan Analysis Service sözleşmeleri [kendi Requests rehberinde](../ResearcherAnalysisService/Requests/README.md) ayrıdır.

`ArticleSummaryAutomation`, `ArticleEvaluation`, `PublicationMetrics`, `FacultyAssistant` ve `BulkCollection` worker'ları kayıtlı varsayılanlarda etkindir ve beş saniyede bir yoklama yapar. Otomatik özetler en çok üç kez denenir; değerlendirme isteği 330, fakülte yanıtı 1.800 saniyeyle sınırlıdır. Kanonik makale incelemesi en çok 24 sağlayıcı çağrısı, tahmini 1,00 USD harcama, 100.000 kaynak baytı ve 600 saniye kabul eder. Bunlar sağlayıcı kotası veya fatura garantisi değil operasyon sınırlarıdır. Deployment öncesinde esas [`academicsettings.json`](../academicsettings.json) dosyasını inceleyin.

Collector'ın yönettiği kalıcı makale akışı Analysis Service üzerinden Gemini kullanır. Analysis Service ilişkilendirilebilir denemeleri kendi `ConnectionStrings:UsageDatabase` bağlantısındaki kullanım defterine yazar; ücretli çağrıdan önce kayıt oluşturamazsa çağrıyı göndermez. Hosted kimlik bilgilerini secret olarak yapılandırın. Development dışındaki analiz servisi `Service:ApiKey` olmadan kapalı kalır; collector aynı değeri `AnalysisService:ApiKey` ile göndermelidir. Collector ürün uçları veri okumadan önce istenen özne için yetki ister. Varsayılan `IAcademicProductAccessService` uygulaması kapalı kalır; consuming host güvenilir kimlik ve kapsam uygulaması sağlamalıdır. Bu standalone hosttaki `DevelopmentPermissionService` izin veren bir adapter'dır ve production öncesinde BYS yetkilendirmesiyle değiştirilmelidir.

## Makale özeti

Collector `SummarizeArticle` isteği `PersonelID`, o araştırmacıya ait `AcademicWorkId` ve isteğe bağlı dili alır. `GetArticleSummary` son başarılı sonucu AI çağrısı olmadan döndürür. Ayrıntılı collector örnekleri [ArticleSummary.http](../Requests/AcademicCollector/ArticleSummary.http) dosyasındadır. Analysis Service'in `POST /api/v1/articles/summarize` ucu bunun altındaki doğrudan sözleşmedir ve çıkarılmış sayfalar, kaynak hash'i, extraction metadata'sı ve deterministik `SourceSpans` ister; [ArticleAnalysis.http](../ResearcherAnalysisService/Requests/ArticleAnalysis.http) tam örnek içerir.

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

Gemini kullanıldığında her `generateContent` denemesi gönderilmeden SQL kullanım defterine `Pending` yazılır ve sonuçta token, model, durum ve maliyet tahminiyle tamamlanır. Prompt, kaynak içerik, kişi/DOI, anahtar ve URL saklanmaz. Pending satır oluşturulamıyorsa ücretli istek gönderilmez; tamamlama yazımı başarısızsa maliyet bilinmediği için satır Pending kalır. Analysis Service kullanım tablosunu kendi başlangıç migration'ıyla oluşturur veya mevcut tabloyu satırları değiştirmeden ayrı geçmişine benimser.

Limitler ve model seçimi için [`academicsettings.json`](../academicsettings.json) ile [`ResearcherAnalysisService/appsettings.json`](../ResearcherAnalysisService/appsettings.json) kaynak kabul edilir. OCR hostunda Tesseract ve gerekli `eng`/`tur` dil verileri ayrıca kurulmalıdır.
