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

Normal test sırası ve çalıştırılabilir örnekler [`Requests/README.md`](Requests/README.md) dosyasındadır. `/Services/AcademicPerformance/V1/` altındaki collector işlemleri; toplama ve yayın seçimine ek olarak kanonik yayınları, otomatik makale özeti/incelemesi/değerlendirmesini, yayın metriklerini, akademik kanıt aramasını, referans popülasyonlarını, kanıt grafiği dışa aktarımını, İK kanıt dosyalarını ve fakülte asistanını kapsar. Toplama, özet, değerlendirme, metrik ve asistan işleri varsayılan olarak etkin kalıcı SQL Server worker'larıyla yürür; deployment öncesinde bütçe, model, kota ve erişim ayarlarını doğrulayın.

## Belgeler

- [Kod rehberi](docs/CODEBASE_GUIDE.md): klasörler, istek akışı, veri katmanları ve doğrulama.
- [Toplu toplama](docs/BULK_COLLECTION.md): giriş, kuyruk, hız sınırı ve kurtarma.
- [Analiz hattı](docs/ANALYSIS_PIPELINE.md): servis sözleşmeleri, otomasyon, kaynak edinme, inceleme, değerlendirme ve kullanım bütçeleri.
- [Kanonik veri](docs/CANONICAL_ACADEMIC_DATA.md), [AI ürünleri](docs/ACADEMIC_AI_PRODUCTS.md), [yayın metrikleri](docs/PUBLICATION_METRICS.md) ve [veri/bilgi katmanı](docs/DATA_KNOWLEDGE_LAYER.md): ayrıntılı ürün sözleşmeleri ve sınırları.
- [Sağlayıcılar](docs/PROVIDERS.md): entegrasyon kapsamı, durum/kota anlamları ve resmî başvurular.
- [Katkı rehberi](CONTRIBUTING.md): dal, PR ve test akışı.

Doğrulama kapsamı ve katkı akışı [katkı rehberindedir](CONTRIBUTING.md). Yalnız Markdown ve yaygın ignore dosyalarını değiştiren işler için uygulama build/test zorunluluğu yoktur.
