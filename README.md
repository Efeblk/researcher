# Akademik Performans Modülü

Serenity ve .NET 10 ile geliştirilen bu prototip; akademisyen profil ve yayınlarını dış sağlayıcılardan toplar, SQL Server'da kaynak bilgisiyle saklar, yayınları tekilleştirir ve okul sitesinde gösterilecek kayıtların seçilmesini sağlar. Ayrı `ResearcherAnalysisService`, kayıtlı araştırmacı verisini ve makale metnini AI ile analiz eder.

Production öncesinde `DevelopmentPermissionService` yerine BYS oturum, yetki ve kayıt sahipliği denetimleri eklenmelidir. YÖKSİS T.C. kimlik numarası içerir; kimlik bilgileri ve API anahtarları repoya yazılmamalıdır.

## Başlangıç

Gereksinimler: .NET 10 SDK, Node.js 18+ ve SQL Server. Aşağıdaki ilk kurulum örneği Windows LocalDB ile `sqlcmd` aracını kullanır.

```powershell
dotnet restore AcademicCollectorDemo.sln
npm install
sqllocaldb start MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID(N'AcademicCollectorDemo') IS NULL EXEC(N'CREATE DATABASE [AcademicCollectorDemo]')"
dotnet run
```

Uygulama `http://localhost:5001/AcademicPerformance` adresindedir. Migration'lar başlangıçta otomatik uygulanır. Bağlantı cümlesi ve sağlayıcı sırları `dotnet user-secrets` veya güvenli deployment yapılandırmasıyla verilmelidir. Varsayılanlar ve tüm seçenekler için [`academicsettings.json`](academicsettings.json) ile [`appsettings.json`](appsettings.json) kaynak kabul edilir. SearchApi entegrasyonu desteklenir ancak kayıtlı varsayılan ayarda kapalıdır.

```powershell
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "<SQL_SERVER_CONNECTION_STRING>"
dotnet user-secrets set "Orcid:AccessToken" "<TOKEN>"
dotnet user-secrets set "SearchApi:ApiKey" "<KEY>"
dotnet user-secrets set "OpenAlex:ApiKey" "<KEY>"
dotnet user-secrets set "WebOfScience:ApiKey" "<KEY>"
dotnet user-secrets set "Yoksis:Username" "<KULLANICI>"
dotnet user-secrets set "Yoksis:Password" "<ŞİFRE>"
```

Hazır örnekler [`Requests/`](Requests/) klasöründedir. Temel V1 uçları `/Services/AcademicPerformance/V1/` altında `Collect`, `GetResearcher`, `ListPublications`, `SavePublicationSelections` ve `Yoksis/Collect` işlemlerini sunar. Uzun toplama işleri için kalıcı kuyruk kullanılabilir.

## Belgeler

- [Kod rehberi](docs/CODEBASE_GUIDE.md): klasörler, istek akışı, veri katmanları ve doğrulama.
- [Toplu toplama](docs/BULK_COLLECTION.md): giriş, kuyruk, hız sınırı ve kurtarma.
- [Makale ve araştırmacı analizi](docs/ANALYSIS_PIPELINE.md): AI servisinin sözleşmesi, kaynak edinme ve kanıt doğrulama.
- [Sağlayıcılar](docs/PROVIDERS.md): entegrasyon kapsamı, durum/kota anlamları ve resmî başvurular.
- [Katkı rehberi](CONTRIBUTING.md): dal, PR ve test akışı.

Doğrulama kapsamı ve katkı akışı [katkı rehberindedir](CONTRIBUTING.md). Yalnız Markdown ve yaygın ignore dosyalarını değiştiren işler için uygulama build/test zorunluluğu yoktur.
