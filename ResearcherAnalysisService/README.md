# Researcher Analysis Service

`ResearcherAnalysisService`, .NET 10 ile çalışan bağımsız analiz HTTP servisidir. Araştırmacı ve makale analizleri, özetler, incelemeler, yayın metrikleri, akademik bilgi/grafik, model değerlendirmesi, İK kanıt dosyası ve fakülte asistanı akışlarının API, kalıcılık ve worker sahibi bu projedir. Collector yalnız kaynak veriyi toplar ve normalize eder.

## Kurulum ve çalıştırma

Servis SQL Server kullanır. Yerel `ConnectionStrings:UsageDatabase` varsayılanı collector'ın `ConnectionStrings:AcademicDatabase` değeriyle aynı `AcademicCollectorDemo` LocalDB veritabanını hedefler. Deployment'ta iki projeye aynı SQL veritabanı hedefini ayrı güvenli yapılandırmayla verin; servis hesapları ve izinleri farklı olabilir.

Analysis Service başlangıçta `ResearcherAnalysisService/Data/Migrations` altındaki `202609140001`–`202609140017` migration serisini `dbo.ResearcherAnalysisVersionInfo` geçmişiyle uygular. `analysis`, `hr` ve `faculty` tablolarının DDL/yazma sahibi Analysis Service'tir; collector tablolarını oluşturmaz. Fresh veritabanında Analysis Service önce başlatılabilir: sağlık, profil keşfi ve sağlayıcı tanısı çalışır; kaynak isteyen ürünler açık `503` dönebilir ve worker'lar collector kaynakları hazır olana kadar bekler. Collector önce veya iki servis eşzamanlı da başlatılabilir.

```powershell
dotnet restore ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet user-secrets set "ConnectionStrings:UsageDatabase" "<SQL_SERVER_CONNECTION_STRING>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet run --project ResearcherAnalysisService/ResearcherAnalysisService.csproj --launch-profile http
```

Sağlık adresi `GET http://localhost:5011/health` olur. Ayrı build ve publish komutları:

```powershell
dotnet build ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet publish ResearcherAnalysisService/ResearcherAnalysisService.csproj -c Release
```

Repo kökünde aynı işlemler için `make run-analysis`, `make build-analysis` ve `make health-analysis` kullanılabilir.

## Veritabanı ve veri akışı

Analysis Service tek `AnalysisDbContext` içinde kendi yazılabilir entity'lerini ve aynı veritabanındaki collector tabloları için özel salt okunur kaynak modellerini eşler. `SaveChanges` kaynak modellerinde ekleme, değiştirme veya silmeyi reddeder. Servis collector tablolarını kopyalamaz; analiz kanıtı için gereken değişmez snapshot temsillerini kendi tablolarına yazar. Servisler arasında foreign key yoktur. `PersonelID`, `CanonicalWorkId` ve `AcademicWorkId` mantıksal kaynak referanslarıdır; güncel ilişki ve uygunluk salt okunur kaynak sorgularıyla denetlenir.

Yeni analiz çalışmaları kanonik kaynak kümesinin kararlı kimlik özetini saklar. Collector tabloları temizlenip tamsayı kimlikleri yeniden kullanılsa bile farklı bir çalışmaya ait eski özet, inceleme veya kanıt güncel kabul edilmez. Bu alan eklenmeden önce kaydedilmiş ve kimliği güvenle türetilemeyen çalışmalar korunur, fakat yeniden üretilene kadar güncelliği `Unknown` olarak raporlanır.

Collector başarılı normalizasyon transaction'ında `core.CollectionChanges` kaydı yazar. Analysis worker'ları bu kalıcı sinyali `analysis.CollectionChangeReceipts` ile idempotent işler ve özet/metrik işlerini kendi kuyruk tablolarına planlar. Collector offline iken Analysis Service mevcut normalize kaynaklardan ürün okuyup işleyebilir; Analysis Service offline iken collector sinyalleri kaybetmeden toplamaya devam eder.

Startup migration açıkken Analysis hesabının kendi şema ve history nesneleri için DDL, kendi tabloları için okuma/yazma ve collector kaynakları için okuma yetkisine ihtiyacı vardır. Migration deployment tarafından dışarıda uygulanıyorsa çalışma hesabından DDL kaldırılabilir.

## Ayarlar ve erişim

Yerel varsayılan araştırmacı raporu sağlayıcısı `Ollama`, modeli `qwen3:1.7b`, adresi `http://localhost:11434/` değeridir. Makale özeti, uzman incelemesi ve fakülte asistanı kayıtlı varsayılan `gemini-3.8-flash` modelini kullanır. Hosted Gemini üretimi için anahtarı secret olarak verin:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<GEMINI_KEY>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj

# Ai:Provider=OpenAI seçilirse araştırmacı raporu için:
dotnet user-secrets set "Ai:ApiKey" "<OPENAI_KEY>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj
```

AI sağlayıcı, model, timeout ve context ayarlarının kayıtlı değerleri [appsettings.json](appsettings.json) içindedir. `AnalysisProducts`, `ArticleSummary`, `ArticleSummaryAutomation`, `FacultyAssistant`, `ArticleReview`, `ArticleEvaluation`, `PublicationMetrics` ve `CollectionChanges` bölümleri kalıcı ürün/worker ayarlarına; `Ai`, `Gemini` ve `Evaluation` stateless model yürütmesine aittir. Değerlendirme varsayılanları [ArticleEvaluationOptions.cs](Configuration/ArticleEvaluationOptions.cs), sürümlü profil/model tanımları [ArticleEvaluationProfileCatalog.cs](Analysis/ArticleEvaluationProfileCatalog.cs) kaynak kodundadır; çalışırken geçerli parmak izi ve kullanılabilirlik için `GET /api/v1/evaluations/profiles` kullanın. Collector'ın `academicsettings.json` dosyası bu ürün bölümlerini taşımaz.

Bilgi/grafik, değerlendirme, İK ve fakülte kalıcı ürünleri `IAcademicProductAccessService` sınırını kullanır. Varsayılan ürün adaptörü kapalıdır: anonim istek `401`, kimliği doğrulanmış fakat eşlemesi yapılandırılmamış istek `503`, yetki reddi ise kayıt varlığını açığa çıkarmayan `404` alır. Güvenilir kimlik, actor ve `PersonelID` kapsamını deployment adaptörü sağlar; korunan ürünlerde `PersonelID` tek başına yetki değildir. Araştırmacı, makale ve metrik uyumluluk işlemleri mevcut kaynak ilişkisi kontrollerini korur, ayrıca ürün adaptörü çağırmaz.

Gemini üretim denemesi gönderilmeden önce kullanım kaydı `Pending` yazılır. Kullanım defteri erişilemiyorsa ücretli çağrı gönderilmez. Migration zinciri fresh veritabanında Analysis Service'in güncel final şemasını doğrudan oluşturur; şema değişikliklerinde geliştirme veritabanı sıfırlanır.

## HTTP yüzeyleri

Kalıcı ürün işlemleri doğrudan `/api/v1/...` altında bulunur; eski `/products` ve yinelenen stateless üretim yolları için alias yoktur.

- araştırmacı: `/api/v1/researchers/analysis/generate`, `/api/v1/researchers/analysis`;
- makale: `/api/v1/articles/summary/generate`, `/api/v1/articles/summary`, `/api/v1/articles/analysis`, `/api/v1/articles/review/generate`;
- metrik/bilgi: `/api/v1/researchers/metrics`, `/api/v1/researchers/metrics/refresh`, `/api/v1/knowledge/search`, `/api/v1/knowledge/reference-population`, `/api/v1/knowledge/reference-population/import`, `/api/v1/knowledge/graph/export`;
- değerlendirme: `/api/v1/evaluations/start`, `/api/v1/evaluations/status`;
- İK: `/api/v1/hr/dossiers/create`, `/api/v1/hr/dossiers`, `/api/v1/hr/dossiers/actions/append`, `/api/v1/hr/dossiers/actions`;
- fakülte: `/api/v1/faculty/context/save`, `/api/v1/faculty/context`, `/api/v1/faculty/assistant/start`, `/api/v1/faculty/assistant/run`.

`GET /api/v1/evaluations/profiles` değerlendirme profillerini keşfeder. `GET /api/v1/internal/provider-status/gemini` Gemini erişimini ve kaydedilmiş kullanım toplamını denetler; içerik üretmez.

Çalıştırılabilir örnekler [Requests/README.md](Requests/README.md) dosyasındadır. Collector örnekleri yalnız collector yüzeyinde [Requests/AcademicCollector](../Requests/AcademicCollector/README.md) altında tutulur.
