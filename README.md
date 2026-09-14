# Academic Collector

Serenity ve .NET 10 ile geliştirilen collector; akademisyen profil ve yayınlarını dış sağlayıcılardan toplar, SQL Server'da kaynak bilgisiyle saklar, yayınları tekilleştirir ve okul sitesinde gösterilecek kayıtların seçilmesini sağlar. Aynı repodaki [ResearcherAnalysisService](ResearcherAnalysisService/README.md) ayrı bir HTTP uygulaması olarak tam bağlamlı AI analizlerini yürütür. İki servis ayrı ayrı build, run ve publish edilebilir.

Production öncesinde `DevelopmentPermissionService` yerine BYS oturum, yetki ve kayıt sahipliği denetimleri eklenmelidir. YÖKSİS T.C. kimlik numarası içerir; kimlik bilgileri ve API anahtarları repoya yazılmamalıdır.

## Collector başlangıcı

Gereksinimler: .NET 10 SDK, Node.js 18+ ve SQL Server. Aşağıdaki ilk kurulum örneği Windows LocalDB ile `sqlcmd` aracını kullanır.

```powershell
dotnet restore AcademicCollectorDemo.csproj
npm install
sqllocaldb start MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID(N'AcademicCollectorDemo') IS NULL EXEC(N'CREATE DATABASE [AcademicCollectorDemo]')"
dotnet run --project AcademicCollectorDemo.csproj
```

Collector `http://localhost:5001/AcademicPerformance` adresindedir. Kendi migration'larını her başlangıçta `ConnectionStrings:AcademicDatabase` üzerinde uygular; migration kaynağı `Modules/AcademicPerformance/Service/Data/Migrations/{Core,Providers}`, sürüm geçmişi `dbo.VersionInfo` tablosudur. Bağlantı cümlesi ve sağlayıcı sırları `dotnet user-secrets` veya güvenli deployment yapılandırmasıyla verilmelidir. Varsayılanlar ve tüm seçenekler için [`academicsettings.json`](academicsettings.json) ile [`appsettings.json`](appsettings.json) kaynak kabul edilir. SearchApi entegrasyonu desteklenir ancak kayıtlı varsayılan ayarda kapalıdır.

```powershell
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "<SQL_SERVER_CONNECTION_STRING>" --project AcademicCollectorDemo.csproj
dotnet user-secrets set "Orcid:AccessToken" "<TOKEN>" --project AcademicCollectorDemo.csproj
dotnet user-secrets set "SearchApi:ApiKey" "<KEY>" --project AcademicCollectorDemo.csproj
dotnet user-secrets set "OpenAlex:ApiKey" "<KEY>" --project AcademicCollectorDemo.csproj
dotnet user-secrets set "WebOfScience:ApiKey" "<KEY>" --project AcademicCollectorDemo.csproj
dotnet user-secrets set "Yoksis:Username" "<KULLANICI>" --project AcademicCollectorDemo.csproj
dotnet user-secrets set "Yoksis:Password" "<ŞİFRE>" --project AcademicCollectorDemo.csproj
```

Collector HTTP test sırası ve çalıştırılabilir örnekleri [Requests/AcademicCollector](Requests/AcademicCollector/README.md) altındadır. `/Services/AcademicPerformance/V1/` işlemleri; toplama ve yayın seçimine ek olarak kanonik yayınları, otomatik makale özeti/incelemesi/değerlendirmesini, yayın metriklerini, akademik kanıt aramasını, referans popülasyonlarını, kanıt grafiği dışa aktarımını, İK kanıt dosyalarını ve fakülte asistanını kapsar. Bu ürün uçları özne yetkilendirmesini, kalıcı kayıtları ve kuyrukları collector'da tutar; gerektiğinde Analysis Service'e tam bağlamlı iç istek gönderir. Production öncesinde bütçe, model, kota ve erişim ayarlarını doğrulayın.

## Analysis Service'i bağımsız çalıştırma

İki proje aynı SQL Server veritabanını kullanır: collector'ın `ConnectionStrings:AcademicDatabase` ve Analysis Service'in `ConnectionStrings:UsageDatabase` değerleri aynı veritabanı hedefini göstermelidir; hesap ve yetkileri servis başına sınırlandırabilirsiniz. Veritabanını bir kez oluşturduktan sonra servisleri herhangi bir sırada, tek başına veya birlikte başlatabilirsiniz. Her servis yalnız kendi migration grubunu ve ayrı sürüm geçmişini uygular; Analysis Service `analysis.GeminiUsageAttempts` tablosunun sahibidir.

Analysis Service kurulumu, ayarları, doğrudan API örnekleri ve çalıştırma komutları [kendi README dosyasındadır](ResearcherAnalysisService/README.md).

```powershell
dotnet build AcademicCollectorDemo.csproj
dotnet publish AcademicCollectorDemo.csproj -c Release

dotnet build ResearcherAnalysisService/ResearcherAnalysisService.csproj
dotnet publish ResearcherAnalysisService/ResearcherAnalysisService.csproj -c Release
```

## Belgeler

- [Kod rehberi](docs/CODEBASE_GUIDE.md): klasörler, istek akışı, veri katmanları ve doğrulama.
- [Toplu toplama](docs/BULK_COLLECTION.md): giriş, kuyruk, hız sınırı ve kurtarma.
- [Analysis Service](ResearcherAnalysisService/README.md) ve [analiz hattı](docs/ANALYSIS_PIPELINE.md): bağımsız servis kurulumu, doğrudan sözleşmeler, collector otomasyonu, kaynak edinme, inceleme, değerlendirme ve kullanım bütçeleri.
- [Kanonik veri](docs/CANONICAL_ACADEMIC_DATA.md), [AI ürünleri](docs/ACADEMIC_AI_PRODUCTS.md), [yayın metrikleri](docs/PUBLICATION_METRICS.md) ve [veri/bilgi katmanı](docs/DATA_KNOWLEDGE_LAYER.md): ayrıntılı ürün sözleşmeleri ve sınırları.
- [Sağlayıcılar](docs/PROVIDERS.md): entegrasyon kapsamı, durum/kota anlamları ve resmî başvurular.
- [Katkı rehberi](CONTRIBUTING.md): dal, PR ve test akışı.

Doğrulama kapsamı ve katkı akışı [katkı rehberindedir](CONTRIBUTING.md). Yalnız Markdown ve yaygın ignore dosyalarını değiştiren işler için uygulama build/test zorunluluğu yoktur.
