# Researcher Analysis Service

`ResearcherAnalysisService`, .NET 10 ile çalışan bağımsız AI HTTP servisidir. `http://localhost:5011` üzerinde tam araştırmacı snapshot'larını, çıkarılmış makale metinlerini ve fakülte kanıt kataloglarını işler. `PersonelID` ile collector veritabanından içerik çekmez; gerekli bağlam doğrudan istek gövdesinde bulunmalıdır.

## Kurulum ve çalıştırma

Servis SQL Server kullanır. Yerel varsayılan `ConnectionStrings:UsageDatabase`, collector'ın `ConnectionStrings:AcademicDatabase` değeriyle aynı `AcademicCollectorDemo` LocalDB veritabanını gösterir; deployment'ta iki projeye aynı güvenli bağlantıyı ayrı ayrı verin. Veritabanı mevcut olduktan sonra collector'ı başlatmadan Analysis Service'i çalıştırabilirsiniz. Analysis Service başlangıçta `ResearcherAnalysisService/Data/Migrations` altındaki kendi migration'larını `dbo.ResearcherAnalysisVersionInfo` geçmişiyle uygular ve yalnız `analysis.GeminiUsageAttempts` tablosunun migration sahipliğini taşır. Aynı veritabanında collector kendi migration geçmişini bağımsız uygular; eşzamanlı başlangıç SQL uygulama kilidiyle koordine edilir.

```powershell
dotnet restore ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet user-secrets set "ConnectionStrings:UsageDatabase" "<SQL_SERVER_CONNECTION_STRING>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet user-secrets set "Service:ApiKey" "<ANALYSIS_SERVICE_KEY>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet run --project ResearcherAnalysisService/ResearcherAnalysisService.csproj --launch-profile http
```

Sağlık adresi `GET http://localhost:5011/health` olur. Ayrı build ve publish komutları:

```powershell
dotnet build ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet publish ResearcherAnalysisService/ResearcherAnalysisService.csproj -c Release
```

## Ayarlar ve erişim

Yerel varsayılan araştırmacı raporu sağlayıcısı `Ollama`, modeli `qwen3:1.7b`, adresi `http://localhost:11434/` değeridir. Makale özeti, uzman incelemesi ve fakülte asistanı için kayıtlı varsayılan Gemini modelini kullanmak üzere anahtarı secret olarak verin:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<GEMINI_KEY>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj

# Ai:Provider=OpenAI seçilirse araştırmacı raporu için:
dotnet user-secrets set "Ai:ApiKey" "<OPENAI_KEY>" --project ResearcherAnalysisService/ResearcherAnalysisService.csproj
```

AI sağlayıcı, model, timeout ve context ayarlarının kayıtlı değerleri [appsettings.json](appsettings.json) içindedir. Değerlendirme varsayılanları [ArticleEvaluationOptions.cs](Configuration/ArticleEvaluationOptions.cs), sürümlü profil/model tanımları [ArticleEvaluationProfileCatalog.cs](Analysis/ArticleEvaluationProfileCatalog.cs) kaynak kodundadır; çalışırken geçerli parmak izi ve kullanılabilirlik için `GET /api/v1/evaluations/profiles` kullanın. Production'da bağlantıyı, `Service:ApiKey` değerini ve sağlayıcı sırlarını deployment secret'larıyla verin. Korunan API uçları `Authorization: Bearer` yerine `X-Analysis-Key` header'ı kullanır. Development'ta anahtar yoksa yalnız loopback erişimine izin verilir; başka ortamlarda eksik anahtar servisi uzaktan erişime kapatır.

Gemini üretim denemesi gönderilmeden önce kullanım kaydı `Pending` yazılır. Kullanım defteri erişilemiyorsa ücretli çağrı gönderilmez. Var olan `analysis.GeminiUsageAttempts` tablosu Analysis Service'in ayrı migration geçmişine güvenle benimsenir; mevcut kullanım satırları değiştirilmez.

## Doğrudan API

Çalıştırılabilir örnekler [Requests/README.md](Requests/README.md) dosyasındadır. Yüzeyler:

- `POST /api/v1/analyze`: tam araştırmacı snapshot'ından rapor üretir.
- `POST /api/v1/articles/summarize`: sayfa ve deterministik kaynak span'larından destekli özet üretir.
- `POST /api/v1/articles/review` ve `/review/stages/*`: tam uzman incelemesi veya quote/dispatch aşamalarını çalıştırır.
- `GET /api/v1/evaluations/profiles` ve `POST /api/v1/evaluations/execute`: sürümlü profilleri okur ve tam kaynakla değerlendirme çalıştırır.
- `POST /api/v1/faculty-assistant`: kesin metin ve konum içeren kanıt kataloğundan yanıt üretir.
- `GET /api/v1/internal/provider-status/gemini`: Gemini erişimini ve kaydedilmiş kullanım toplamını denetler; içerik üretmez.

Collector'ın son kullanıcıya yönelik, kalıcı ve özne-yetkili ürün uçları `http://localhost:5001/Services/AcademicPerformance/V1/` altında kalır. Collector bunları yürütürken `AnalysisService:BaseUrl` ve `AnalysisService:ApiKey` ile bu servisi çağırabilir; doğrudan Analysis API çağrıları collector kaydı, kuyruğu veya ürün erişim denetimi oluşturmaz.
