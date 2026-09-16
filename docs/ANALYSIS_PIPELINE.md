# Analiz hattı

`ResearcherAnalysisService`, araştırmacı ve makale analizlerinin, özet/inceleme/değerlendirme akışlarının, yayın metriklerinin, bilgi/grafik ürünlerinin ve İK/fakülte işlerinin bağımsız .NET 10 sahibidir. Ürünler aynı SQL veritabanındaki collector kaynaklarını salt okunur modellerle çözer; analiz motorları ürün iş akışlarınca süreç içinde çağrılır. Çıktılar yalnız Analysis Service'in `analysis`, `hr` ve `faculty` tablolarına yazılır.

## Çalışma ve veritabanı sınırı

Yerel varsayılanlarda collector'ın `ConnectionStrings:AcademicDatabase` ve Analysis Service'in `ConnectionStrings:UsageDatabase` değerleri aynı LocalDB veritabanını gösterir. Deployment'ta aynı SQL veritabanı hedefini iki projeye ayrı güvenli yapılandırmayla verin. Analysis hesabı collector tablolarında yalnız okur; startup migration açıksa kendi şema/history nesneleri için DDL ve kendi tabloları için okuma/yazma izni de gerekir.

Collector `dbo.VersionInfo`, Analysis Service `dbo.ResearcherAnalysisVersionInfo` geçmişini kullanır ve her servis yalnız kendi DDL'ini başlangıçta uygular. Analysis-first collector şeması olmadan başlar; kaynak bağımlı ürünler `503` verebilir ve worker'lar kaynak hazır olana kadar bekler. Collector-first Analysis tabloları oluşturmaz. Eşzamanlı migration ortak SQL uygulama kilidiyle koordine edilir.

Collector her başarılı normalize/kanonik transaction'ında `core.CollectionChanges` sinyali yazar. Analysis worker'ları bunu `analysis.CollectionChangeReceipts` ile idempotent tüketir ve kendi özet/metrik kuyruklarını planlar. Collector Analysis HTTP çağrısı veya AI sağlık proxy'si yapmaz. Analysis Service kapalıysa sinyaller SQL'de kalır; collector kapalıysa Analysis Service mevcut normalize kaynaklardan ürün okumaya ve bekleyen işleri yürütmeye devam eder.

Analysis'in tek `AnalysisDbContext` modeli kendi yazılabilir entity'leriyle özel salt okunur collector kaynak modellerini birlikte eşler. `SaveChanges` kaynak entity'lerinde ekleme/değiştirme/silmeyi reddeder. Servisler arasında foreign key yoktur; mantıksal kaynak kimlikleri güncel owner/üyelik sorgularıyla doğrulanır. Kaynak kanıtı gerekiyorsa değişmez snapshot temsili Analysis tablolarına alınır, collector tabloları kopyalanmaz.

## Araştırmacı analizi

Araştırmacı ürün yüzeyi `POST http://localhost:5011/api/v1/...` altında iki işlem sunar:

| İşlem | Davranış |
| --- | --- |
| `/api/v1/researchers/analysis/generate` | `PersonelID` için güncel salt okunur kaynaklardan snapshot üretir, raporu oluşturur ve kaydeder. |
| `/api/v1/researchers/analysis` | Güncel kaynak kapsamını ve nullable son başarılı raporu sağlayıcı çağrısı yapmadan birlikte getirir. |

Örnekler [Researchers.http](../ResearcherAnalysisService/Requests/Researchers.http) dosyasındadır. `snapshotAt` yalnız oluşturulan snapshot'ın metadata etiketidir; geçmiş veriyi yeniden kurmaz. Başarısız üretim önceki başarılı raporu değiştirmez.

Yinelenen stateless araştırmacı üretim yolu kaldırılmıştır. Yerel varsayılan araştırmacı sağlayıcısı Ollama/Qwen'dir.

## Kalıcı ürünler ve worker'lar

Analysis Service'in `/api/v1/...` yüzeyi ayrıca şunları sunar:

- makale özeti/kanıtı/incelemesi: `/api/v1/articles/summary/generate`, `/api/v1/articles/summary`, `/api/v1/articles/analysis`, `/api/v1/articles/review/generate`;
- metrik, bilgi ve grafik: `/api/v1/researchers/metrics`, `/api/v1/researchers/metrics/refresh`, `/api/v1/knowledge/search`, `/api/v1/knowledge/reference-population`, `/api/v1/knowledge/reference-population/import`, `/api/v1/knowledge/graph/export`;
- değerlendirme: `/api/v1/evaluations/start`, `/api/v1/evaluations/status`;
- İK: `/api/v1/hr/dossiers/create`, `/api/v1/hr/dossiers`, `/api/v1/hr/dossiers/actions/append`, `/api/v1/hr/dossiers/actions`;
- fakülte: `/api/v1/faculty/context/save`, `/api/v1/faculty/context`, `/api/v1/faculty/assistant/start`, `/api/v1/faculty/assistant/run`.

Kesin payload'lar [Analysis Service HTTP rehberindedir](../ResearcherAnalysisService/Requests/README.md). `/health` dışındaki Analysis API uçları `X-Analysis-Key` servis erişim denetimini kullanır. Bilgi/grafik, değerlendirme, İK ve fakülte kalıcı ürünleri buna ek olarak veri okumadan önce `IAcademicProductAccessService` ile özne erişimini denetler; servis anahtarı özne yetkisi yerine geçmez ve korunan ürünlerde `PersonelID` tek başına yetki değildir. Varsayılan adaptör kapalıdır; deployment güvenilir kimlik/kapsam eşlemesi sağlamalıdır. Araştırmacı, makale ve metrik uyumluluk işlemleri mevcut kaynak ilişkisi kontrollerini korur.

`ArticleSummaryAutomationWorker`, `PublicationMetricsWorker`, `ArticleEvaluationWorker` ve `FacultyAssistantWorker` Analysis Service sürecinde çalışır. `AnalysisProducts`, `ArticleSummary`, `ArticleSummaryAutomation`, `FacultyAssistant`, `ArticleReview`, `ArticleEvaluation`, `PublicationMetrics` ve `CollectionChanges` bölümleri [ResearcherAnalysisService/appsettings.json](../ResearcherAnalysisService/appsettings.json) içindedir; `Ai`, `Gemini` ve `Evaluation` stateless model ayarları da aynı projede kalır. Deployment öncesinde Analysis değerlerini ve secret'ları inceleyin. Okuma işlemleri model çağrısı veya kuyruk yazması yapmaz. Yenileme, force regeneration, import ve start işlemleri yeni kalıcı sürüm/run oluşturabilir; HTTP örneklerindeki ön koşul ve maliyet etiketlerini izleyin.

## Makale özeti ve kaynak edinme

Kalıcı `/api/v1/articles/summary/generate`, yetkili `PersonelID`, ilişkili `AcademicWorkId` ve dili alır. `/api/v1/articles/summary` son başarılı sonucu AI çağrısı olmadan döndürür. Analysis Service salt okunur kaynaklardan uygun kanonik gözlem ve kaydedilmiş adayları çözer; ayrıntılı istekler [Articles.http](../ResearcherAnalysisService/Requests/Articles.http) dosyasındadır.

`POST /api/v1/articles/analysis`, kanonik durum, sayfalı kanıt ve nullable uzman incelemesini tek salt okunur yanıtta birleştirir.

Kalıcı kaynak edinme sırası şöyledir:

1. Aynı araştırmacının tam normalize DOI eşleşmeli kayıtlarındaki mevcut abstract ve kaynak adayları toplanır.
2. PDF adayları, ardından landing page'ler sınırlı istek, redirect, boyut ve süre bütçesiyle denenir.
3. Semantik makale gövdesi olan HTML kabul edilir; menü, login, paywall, yalnız abstract veya genel `body` tam metin sayılmaz.
4. Metin katmanlı PDF doğrudan çıkarılır. Eksik sayfalar ayar açıksa sınırlı Tesseract OCR kullanabilir; sayfa, DPI, piksel ve süre sınırları uygulanır.
5. Analysis Service kayıtlı adayları akademik metadata sağlayıcılarına geri dönüp zenginleştirmez. Kaynak adayları collector'ın normal sağlayıcı toplama akışlarında kaydedilir.
6. Kayıtlı adaylarda tam metin yoksa bulunan abstract kısmi kapsam gerekçesiyle özetlenir; yalnız metadata bulgu üretmez.

İstekten fetch URL kabul edilmez. Her redirect ve keşfedilen adres yeniden doğrulanır; yalnız genel HTTP(S) adreslerine bağlanılır ve DNS bağlantısı doğrulanan adrese sabitlenir. Script çalıştırılmaz. API anahtarı, kayıtlı URL, sorgu dizesi ve ham transport hatası tanılara konmaz. Başarılı kaynak snapshot'ı SHA-256 özetiyle rapor yanında saklanır.

Model yalnız verilen deterministik span kimliklerine atıf yapabilir. Servis bilinmeyen kimlikleri reddeder; alıntı, sayfa ve offset'i kayıtlı kaynaktan çözer. Ayrı verifier geçişi her iddiayı bağlamıyla denetler; desteklenmeyen, belirsiz veya bütçede doğrulanamayan iddialar elenir. Bu mekanizma bilimsel doğruluk garantisi değildir.

## İnceleme, kullanım ve hata sınırları

Staged makale incelemesi her uzman rolü için generation ve verification checkpoint'lerini Analysis SQL'inde saklar. Girdiler sessizce kırpılmaz; boyut/context sınırını aşan istek `413` alır. Exact-model, fiyatlandırılmış ve kalıcı ledger kaydıyla doğrulanmış output-limit durumları dışında belirsiz maliyetli dispatch otomatik tekrarlanmaz. Faculty ve evaluation kuyrukları da server-issued grant'i yürütme öncesi yeniden yetkilendirir ve yalnız güvenli hata kodu saklar.

Gemini kullanıldığında her `generateContent` denemesi gönderilmeden `analysis.GeminiUsageAttempts` tablosuna `Pending` yazılır; sonuç token, model, durum ve maliyet tahminiyle tamamlanır. Prompt, kaynak içerik, kişi/DOI, anahtar ve URL saklanmaz. Pending satırı oluşturulamıyorsa ücretli istek gönderilmez; tamamlama yazımı başarısızsa maliyet bilinmediği için satır Pending kalır.

Model, thinking, token, timeout ve fiyat varsayılanları servis ayrımında değişmez. Otomatik testler sentetik fixture ve fake sağlayıcı kullanır; canlı veya ücretli çağrı yapmaz. Model ayrıntıları ve bağımsız çalışma komutları [Researcher Analysis Service README](../ResearcherAnalysisService/README.md) dosyasındadır.
