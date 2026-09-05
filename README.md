# Akademik Performans Modülü

Resmî ORCID Public API, SearchApi Google Scholar Author API, Clarivate Web of
Science Starter API v1 ve YÖKSİS OzgecmisV2 SOAP servisi üzerinden akademik veri
toplayan, Serenity bileşenleriyle hazırlanmış .NET 10 prototipidir. Uygulama
sağlayıcı yayınlarını yerel veritabanında saklar; akademisyen okul sitesinde
gösterilmesine izin verdiği yayınları ayrıca seçer.

## Mevcut iş akışı

1. Akademisyen ORCID, Google Scholar ID, Web of Science ResearcherID ve/veya
   YÖKSİS sorgusu için T.C. kimlik numarasını girer.
2. ORCID profil/faaliyetleri, ORCID ile eşleşen ayrı OpenAlex karşılaştırması,
   Google Scholar profil/metrik/yayınları ve Web of Science yayın/atıf verileri
   bağımsız olarak alınır.
3. Eserler DOI'ye; DOI yoksa normalize başlık ve yıla göre tekilleştirilir.
4. Sade yayın kayıtları Serenity grid'inde gösterilir.
5. **Okulda Göster** seçimleri `PublicationDisplayApprovals` tablosuna kaydedilir.

PDF indirilmez; sağlayıcıların sunduğu DOI ve yayın bağlantıları saklanır.
OpenAlex verileri yalnız karşılaştırma amacıyla ayrı tablolarda tutulur ve ortak
yayın listesine eklenmez. Web arayüzündeki sağlayıcı karşılaştırma tablosu ORCID,
Google Scholar, OpenAlex ve Web of Science yayın/atıf metriklerini yan yana gösterir.
Bir ORCID birden fazla OpenAlex yazar kümesine bağlıysa en çok yayına, eşitlikte
en çok atıfa sahip küme seçilir; adayların tamamının ham arama yanıtı saklanır.

## API-first Serenity servis mimarisi

Yeni web, mobil, BYS ve başvuru client'ları veritabanı varlıklarına bağlı
Serenity UI endpoint'leri yerine sürümlü `V1` sözleşmesini kullanmalıdır.
Endpoint'ler ince bir HTTP katmanıdır; mevcut WebClient ve ileride eklenecek
sunucu zamanlayıcısı da aynı `IAcademicPerformanceApplicationService` iş akışını
kullanmalıdır. Bütün bileşenler şimdilik tek proje ve tek process içindedir.

```text
Web / mobil / BYS / başvuru client'ları
                │
                ▼
   Services/AcademicPerformance/V1/*
                │
                ▼
 IAcademicPerformanceApplicationService
                │
                ▼
 ORCID / OpenAlex / Google Scholar / Web of Science / EF Core
```

| İşlem | Serenity endpoint | Amaç |
| --- | --- | --- |
| Topla/güncelle | `V1/Collect` | ORCID ve/veya ResearcherID ile veriyi toplar |
| Akademisyen getir | `V1/GetResearcher` | ID, ORCID veya ResearcherID ile sade profil döndürür |
| Yayınları listele | `V1/ListPublications` | Sayfalama, arama ve yalnız onaylı yayın filtresi sunar |
| Onayları kaydet | `V1/SavePublicationSelections` | Okulda gösterilecek yayın listesini değiştirir |
| YÖKSİS topla | `V1/Yoksis/Collect` | Hassas ve yetkili YÖKSİS akışını ayrı tutar |

Tam adres biçimi
`/Services/AcademicPerformance/V1/Collect` şeklindedir ve Serenity servisleri
JSON gövdeli `POST` kullanır. Örnek:

```http
POST http://localhost:5001/Services/AcademicPerformance/V1/Collect
Content-Type: application/json

{
  "Orcid": "0000-0001-8560-7482",
  "WebOfScienceResearcherId": "A-1009-2008"
}
```

Yanıtlar ham EF entity'leri veya sağlayıcı JSON'ları yerine
`Service/Api/V1/Contracts` altındaki kararlı profil ve yayın DTO'larını
döndürür. `PublicationSummary` ve
`PublicationDisplayApproval` endpoint'leri yalnız mevcut Serenity WebClient'ın
grid adapter'larıdır; yeni client sözleşmesi olarak kullanılmamalıdır.
YÖKSİS kişisel veri içerdiğinden ayrı `V1/Yoksis/Collect` endpoint'inde kalır ve
production'da ayrıca yetkilendirilmelidir.

Client bağımsızlığı endpoint'lerin anonim olduğu anlamına gelmez. Production
BYS host'u tüm `V1` işlemlerine Serenity oturum/izin kontrolü uygulamalı;
özellikle veri toplama ve yayın seçimi işlemlerinde oturumdaki akademisyen ile
istekteki kayıt arasında sunucu tarafında sahiplik doğrulaması yapmalıdır.

## Hedef servisler ve entegrasyon durumu

| Kimlik veya sistem | Hedef servis | Erişim beklentisi | Proje durumu |
| --- | --- | --- | --- |
| ORCID | Resmî ORCID Public API 3.0 | Herkese açık kayıtlar; isteğe bağlı erişim belirteci | **Aktif**; profil, faaliyet ve eserler alınıyor |
| OpenAlex | Resmî OpenAlex API | Anahtarsız düşük kota; ücretsiz anahtarla günlük kullanım bütçesi | **Aktif, ayrı**; ORCID ile profil/metrik/yayınlar alınır, ortak yayın listesine katılmaz |
| Google Scholar ID | SearchApi Google Scholar Author API | API anahtarı ve aylık istek kotası | **Aktif**; profil, h-index, i10-index, atıf metrikleri ve yayınlar alınıyor |
| Scopus Author ID | Resmî Elsevier Scopus API | Kurumsal abonelik ve API yetkisi gerekebilir | **Hedef**; entegrasyon henüz yok |
| Web of Science ResearcherID | Resmî Clarivate Starter API v1 | API anahtarı gerekli; ücretsiz ve kurumsal planlar var | **Aktif**; `AI` sorgusuyla erişilebilen tüm WoS veri tabanlarındaki yayınlar ve plan izin verirse atıf sayıları alınıyor |
| YÖKSİS | OzgecmisV2 SOAP servisi | Kurumsal kullanıcı/şifre, T.C. kimlik no ve gerekirse izinli çıkış IP'si | **Aktif**; 21 ana kategori alınıyor, makale/bildiri/kitap/patent ayrıntıları ortak yayın tablolarına yazılıyor |
| BYS kullanıcı kaydı | Üniversitenin kimlik ve yetki servisleri | Oturum açmış akademisyen ve kurum içi personel ID'si | **Production için gerekli** |
| ResearchGate / Academia.edu | Resmî ve izinli API bulunursa değerlendirilecek | Scraping kullanılmayacak | **Kapsam dışı** |

ORCID erişimi, kota ve sınırlama ayrıntıları için
[`docs/ORCID/ORCID_API_RAPORU.md`](docs/ORCID/ORCID_API_RAPORU.md) dosyasına bakın.
Web of Science Starter API planları, kota hesabı ve metrik sınırlamaları için
[`docs/WEB_OF_SCIENCE/WEB_OF_SCIENCE_API_RAPORU.md`](docs/WEB_OF_SCIENCE/WEB_OF_SCIENCE_API_RAPORU.md)
dosyasına bakın.
Google Scholar'ın API durumu, h-index/i10-index sağlayıcı karşılaştırması ve
önerilen entegrasyon yaklaşımı için
[`docs/GOOGLE_SCHOLAR/GOOGLE_SCHOLAR_API_RAPORU.md`](docs/GOOGLE_SCHOLAR/GOOGLE_SCHOLAR_API_RAPORU.md)
dosyasına bakın.
OpenAlex'in veri kaynakları ve Google Scholar ile ilişkisi için
[`docs/OPENALEX/OPENALEX_API_RAPORU.md`](docs/OPENALEX/OPENALEX_API_RAPORU.md),
YÖKSİS SOAP akışı için
[`docs/YOKSIS/YOKSIS_API_RAPORU.md`](docs/YOKSIS/YOKSIS_API_RAPORU.md), bütün
sağlayıcıların kısa karşılaştırması için
[`docs/API_OZET_RAPORU.md`](docs/API_OZET_RAPORU.md) dosyasına bakın.

ORCID ve Web of Science ortak yayın toplama akışında çağrılır. YÖKSİS kişisel
veri içerdiği için UI içinden ayrı bir endpoint üzerinden çalışır. T.C. kimlik
numarası tarayıcıda hatırlanmaz ve istek tamamlanınca formdan temizlenir. Yeni
bir servis, erişim sözleşmesi ve veri sahipliği netleşmeden mevcut toplama
akışına eklenmez. T.C. kimlik numarası veritabanına yazılmaz; YÖKSİS'in döndürdüğü
Araştırmacı ID akademisyen eşleştirmesinde kullanılır.

## Gereksinimler ve çalıştırma

- .NET 10 SDK
- Node.js 18 veya üzeri
- SQL Server; Windows geliştirme ortamında SQL Server Express LocalDB yeterlidir

```powershell
dotnet restore
sqllocaldb start MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -Q "IF DB_ID(N'AcademicCollectorDemo') IS NULL EXEC(N'CREATE DATABASE [AcademicCollectorDemo]')"
dotnet run
```

Veritabanını yalnız ilk kurulumda oluşturun. Sonraki şema değişikliklerini
uygulama başlangıcında FluentMigrator uygular.

Çalışan uygulamayı durdurduktan sonra geliştirme veritabanındaki migration
tablolarını ve bütün uygulama verilerini silmek için:

```powershell
dotnet run -- --clean-database
```

Bu komut migration'ları `Down(0)` ile geri alır, FluentMigrator'ın `VersionInfo`
takip tablosunu da siler ve web sunucusunu başlatmadan çıkar. Sonraki normal
`dotnet run`, `MigrateUp()` ile takip tablosunu ve uygulama şemasını tamamen
sıfırdan kurar. Komut yalnız `Development` ortamında çalışır ve bütün geliştirme
verilerini kalıcı olarak siler.

Build sırasında `npm install` ve TypeScript derlemesi gerektiğinde otomatik
çalışır. Arayüz `http://localhost:5001/AcademicPerformance`, sağlık yanıtı ise
`http://localhost:5001/` adresindedir.

Yalnızca derlemek için:

```powershell
dotnet build
```

## Veri toplama komutları

PowerShell:

```powershell
.\collect.ps1 -Id "0000-0001-8560-7482"
```

WSL, Linux veya macOS üzerinde isteğe bağlı Make hedefleri:

```shell
make build
make run
make health
make collect ID="0000-0001-8560-7482"
make collect ID="0000-0001-8560-7482 A-1009-2008"
```

Hazır IDE istekleri `Requests/AcademicPerformance.http` dosyasındadır. Temizleme
komutu yalnız `bin/` ve `obj/` build çıktılarını temizler; SQL Server
veritabanına dokunmaz:

```powershell
.\collect.ps1 clean
```

YÖKSİS için `Requests/AcademicPerformance.http` içindeki örnek isteği kullanın:

```http
POST http://localhost:5001/Services/AcademicPerformance/V1/Yoksis/Collect
Content-Type: application/json

{
  "TcKimlikNo": "11_HANELI_TC_KIMLIK_NO"
}
```

Yanıt varsayılan olarak kısa durum ve kayıt sayılarını içerir. İstenirse
`IncludeRecords` ve `IncludeRawResponses` alanları `true` gönderilerek düz
kayıtlar ile ham SOAP XML'i yanıta eklenebilir; bu alanlar `false` olsa da
toplanan veriler veritabanına yazılır.
`UpdatedAfter` alanı verilirse WSDL'deki isteğe bağlı `P_TARIH` alanına gönderilir.
T.C. kimlik numarasını `.http` dosyasına kaydedip Git'e göndermeyin.

## Proje yapısı

```text
Modules/AcademicPerformance/
├── Service/                Sunucu ve akademik veri servisi
│   ├── Api/V1/
│   │   ├── Contracts/      Client bağımsız request/response sözleşmeleri
│   │   └── Endpoints/      Desteklenen V1 HTTP giriş noktaları
│   ├── Application/        Ortak kullanım senaryoları ve iş akışı
│   ├── Data/               EF Core bağlamı ve SQL Server şeması
│   │   └── Migrations/
│   │       ├── Core/       Ortak şema geçmişi
│   │       └── Providers/  Sağlayıcıya özel şema geçmişi
│   ├── Integrations/       Sağlayıcı istemcileri ve sağlayıcıya özel akışlar
│   ├── Researchers/
│   │   ├── Models/         Akademisyen veri modeli
│   │   ├── Collection/     Kimlik ayrıştırma ve toplama akışı
│   │   └── Persistence/    Akademisyen veritabanı işlemleri
│   └── Works/
│       ├── Models/         Yayın, özet ve gösterim onayı modelleri
│       └── Processing/     Normalizasyon ve eşitleme işlemleri
├── WebClient/
│   ├── Pages/              Razor sayfaları, layout ve TypeScript
│   ├── Publications/       Serenity Row/Columns tanımları
│   └── Endpoints/          Yalnız arayüzün kullandığı grid adapter'ları
├── Background/             Planlanan periyodik yenileme görevi için ayrılmıştır
└── AcademicPerformanceModule.cs  DI ve modül kayıtları
Host/                       Uygulama host'una özel geliştirme servisleri
Requests/                   Manuel HTTP istekleri
docs/                       Sağlayıcı ve entegrasyon notları
wwwroot/Content/            Uygulama stilleri
```

Klasörler çalışma ortamına göre ayrılmıştır. `Service` hem Serenity UI hem de
harici client'ların kullandığı sunucu tarafını, `WebClient` yalnız tarayıcı
arayüzünü içerir. `Background` henüz uygulanmamış zamanlanmış işler için
ayrılmıştır.

Modül içindeki bağımlılık yönü `WebClient/Api -> Application -> domain ve
entegrasyonlar -> Data` şeklindedir. Sağlayıcıdan gelen ham modeller
`Integrations/` dışına taşınmaz; client'lara yalnız `Api/V1/Contracts`
sözleşmeleri açılır.

`wwwroot/esm/`, `bin/`, `obj/` ve `node_modules/`
yeniden üretilebilir veya çalışma zamanı çıktılarıdır; Git'e eklenmez.

## Veritabanı migration'ları

Şema FluentMigrator ile yönetilir. Uygulama başlarken bekleyen migration'lar
otomatik olarak uygulanır ve sürümler `VersionInfo` tablosunda tutulur. İlk
kurulum ve güncelleme için ayrıca bir CLI komutu çalıştırmak gerekmez:

```powershell
dotnet run
```

Yeni bir şema değişikliği için `Service/Data/Migrations/Core/` veya
`Service/Data/Migrations/Providers/` altında benzersiz, artan zaman damgalı bir
migration oluşturun; örneğin `202609010001_AddCollectionStatus.cs`. Ortak şema
değişiklikleri `Core/`, yalnız bir dış sağlayıcıyı ilgilendiren değişiklikler
`Providers/` altında tutulur. Ayrıntılı kurallar migration klasöründeki
`README.md` dosyasındadır. Daha önce uygulanmış migration dosyalarını
değiştirmeyin. Yeni migration eklemek yerine `AcademicDbContext` modelini tek
başına değiştirmek veritabanını güncellemez.

Migration'lar yalnız SQL Server içindir. `Up()` ileri şema değişikliğini,
`Down()` güvenli geri alma sırasını içermelidir. FluentMigrator uygulanmış
sürümleri `VersionInfo` üzerinden takip ettiği için migration gövdelerinde
manuel `table exists` kontrolleri kullanılmaz.

## Veri modeli

| Tablo | İçerik |
| --- | --- |
| `Researchers` | Akademisyen, ORCID, Web of Science ResearcherID ve YÖKSİS Araştırmacı ID eşleşmesi |
| `OrcidProfiles` / `OrcidWorks` | ORCID profil, faaliyet, eser ve ham JSON verisi |
| `GoogleScholarProfiles` / `GoogleScholarWorks` | Scholar profil metrikleri, yayınlar ve SearchApi ham JSON verisi |
| `OpenAlexProfiles` / `OpenAlexWorks` | ORCID ile bulunan ayrı karşılaştırma metrikleri, yayınlar ve ham JSON |
| `WebOfScienceProfiles` | Starter API sorgu özeti ve ham yayın sayfası yanıtları |
| `WebOfScienceWorks` | Web of Science yayınları ve varsa atıf sayıları |
| `YoksisRecords` | YÖKSİS kategorilerinden gelen tüm kayıtların eksiksiz JSON içeriği |
| `AcademicWorks` | ORCID, Web of Science ve YÖKSİS'ten gelen normalize yayınlar |
| `PublicationSummaries` | Arayüz ve raporlama için sade yayın listesi |
| `PublicationDisplayApprovals` | Okulda gösterilmesine izin verilen yayınlar |

YÖKSİS'in başarılı kategorilerde döndürdüğü bütün kayıtlar `YoksisRecords`
tablosuna yazılır. Farklı kategorilerin alanları değiştiği için özgün alanlar
`RecordJson` içinde kayıpsız tutulur. Makale, bildiri, kitap ve patent ayrıntıları
ayrıca `AcademicWorks` tablosuna; grid'de kullanılacak tekilleştirilmiş halleri
`PublicationSummaries` tablosuna yazılır. T.C. kimlik numarası saklanmaz.

ORCID atıf sayısı, h-index ve i10-index sağlamaz. Google Scholar metrikleri
SearchApi üzerinden, OpenAlex metrikleri ise ORCID eşleşmesi üzerinden alınır ve
iki kaynak ayrı tutulur. OpenAlex yayınları karşılaştırma aşamasında
`AcademicWorks` ile `PublicationSummaries` tablolarına eklenmez. Starter API v1
hazır profil metrikleri sunmaz; bütün Web of Science yayınlarında atıf sayısı
gelirse h-index ve toplam atıf uygulama içinde hesaplanır.

Web of Science sorguları hem `db=WOS` hem `db=WOK` ile ayrı ayrı yapılır. Böylece
Core Collection sonuçları korunurken API anahtarının erişebildiği diğer Web of
Science veri tabanları da taranır. Aynı eserin farklı veri tabanlarındaki kayıtları
UID, DOI veya başlık-yıl bilgisine göre tekilleştirilir. İki sorgunun ham sayfaları
sağlayıcı kapsamı belirtilerek ayrı ayrı saklanır.

## Yapılandırma ve production notları

Uygulama yalnız SQL Server kullanır. Yerel geliştirme için `appsettings.json`
Windows LocalDB bağlantısı içerir. Kurumsal SQL Server bağlantısını User
Secrets, environment variable veya deployment secret üzerinden değiştirin;
gerçek kullanıcı adı ve parolaları repoya yazmayın:

```powershell
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "<SQL_SERVER_CONNECTION_STRING>"
dotnet user-secrets set "Orcid:AccessToken" "<TOKEN>"
dotnet user-secrets set "SearchApi:ApiKey" "<SEARCHAPI_API_KEY>"
# İsteğe bağlı: anahtarsız kotayı yükseltir
dotnet user-secrets set "OpenAlex:ApiKey" "<OPENALEX_API_KEY>"
dotnet user-secrets set "WebOfScience:ApiKey" "<CLARIVATE_API_KEY>"
dotnet user-secrets set "Yoksis:Username" "<KURUMSAL_KULLANICI>"
dotnet user-secrets set "Yoksis:Password" "<KURUMSAL_SIFRE>"
```

YÖKSİS kimlik bilgileri hem SOAP gövdesinde hem HTTP Basic Authentication
başlığında gönderilir; uygulama bunları yanıta, ham XML alanına veya loglara
yazmaz. Basic Authentication yalnızca Base64 kodlaması kullandığı için HTTPS
adresi korunmalıdır. Endpoint production'da BYS kimlik/yetki kontrolü arkasına
alınmadan dış ağa açılmamalıdır.

Mevcut UI bir entegrasyon prototipidir. **Yayınlarımı Getir** için sağlayıcı
kimliği tarayıcı `localStorage` alanında hatırlanır. BYS entegrasyonunda ORCID,
oturum açmış kullanıcı kaydından sunucu tarafında alınmalı; prototip izin
servisi gerçek kimlik/yetki servisiyle değiştirilmelidir. Yayın onay endpoint'leri
de production'a geçmeden önce kullanıcı sahipliği kontrolü uygulamalıdır.

Hedef SQL Server veritabanı önceden oluşturulmuş olmalıdır. Uygulama açılışta
tablo, foreign key ve indeks migration'larını otomatik uygular; veritabanını
oluşturma veya silme yetkisi istemez.

Proje Web of Science Starter API'nin Free Institutional Member planını kullanır;
plan 5 istek/saniye, 5.000 istek/gün ve abonelik kapsamındaki atıf alanlarını
sağlar. Resmî kaynaklar:
[Starter API](https://developer.clarivate.com/apis/wos-starter) ve
[Swagger şeması](https://developer.clarivate.com/apis/wos-starter/swagger).

YÖKSİS sözleşmesi:
[OzgecmisV2 WSDL](https://servisler.yok.gov.tr/ws/OzgecmisV2?WSDL). WSDL'de
servis adresi HTTP görünse de yapılandırmada şifreli HTTPS adresi kullanılır.

## Doğrulama

Doğrulama komutları:

```powershell
dotnet build --configuration Release
dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj --configuration Release
npm run typecheck
npm test
```

xUnit testleri sağlayıcı yanıtlarını taklit eder; gerçek ücretli API çağrısı
yapmaz. Veritabanı ve HTTP testleri Windows'ta LocalDB kullanır. Diğer ortamlarda
`ACADEMIC_TEST_SQLSERVER` ile ayrı bir test SQL Server bağlantısı verin.
Her çalıştırma yalnız kendisinin oluşturduğu rastgele isimli test veritabanını
kullanır ve sonunda kaldırır; uygulamanın veritabanına veya User Secrets'a
bağlanmaz. GitHub CI, SQL Server'ı geçici bir container'da çalıştırır.

İnceleme bulguları ve kapsam sınırları: [proje incelemesi](docs/PROJECT_REVIEW.md).
