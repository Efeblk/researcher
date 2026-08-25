# Akademik Performans Modülü

Resmî ORCID Public API, Clarivate Web of Science Starter API v1 ve YÖKSİS
OzgecmisV2 SOAP servisi üzerinden akademik veri toplayan, Serenity
bileşenleriyle hazırlanmış .NET 10 prototipidir. Uygulama ORCID ve Web of Science
ile YÖKSİS yayınlarını yerel veritabanında saklar; akademisyen okul sitesinde
gösterilmesine izin verdiği yayınları ayrıca seçer.

## Mevcut iş akışı

1. Akademisyen ORCID, Web of Science ResearcherID ve/veya YÖKSİS sorgusu için
   T.C. kimlik numarasını girer.
2. ORCID profil/faaliyet verileri ile Web of Science yayın ve kullanılabiliyorsa
   atıf verileri bağımsız olarak alınır.
3. Eserler DOI'ye; DOI yoksa normalize başlık ve yıla göre tekilleştirilir.
4. Sade yayın kayıtları Serenity grid'inde gösterilir.
5. **Okulda Göster** seçimleri `PublicationDisplayApprovals` tablosuna kaydedilir.

PDF indirilmez; sağlayıcıların sunduğu DOI ve yayın bilgileri saklanır. OpenAlex
ve Google Scholar entegrasyonları çalışma zamanından kaldırılmıştır.

## Serenity servis mimarisi

Yeni client'lar veritabanı varlıklarına bağlı eski endpoint'ler yerine sürümlü
`V1` sözleşmesini kullanmalıdır. Endpoint'ler ince bir HTTP katmanıdır; UI ve
sunucu zamanlayıcısı aynı `IAcademicPerformanceApplicationService` iş akışını
kullanır.

```text
Başvuru modülü / başka client / Serenity UI
                │
                ▼
   Services/AcademicPerformance/V1/*
                │
                ▼
 IAcademicPerformanceApplicationService ◀── BackgroundService
                │
                ▼
       ORCID / Web of Science / EF Core
```

| İşlem | Serenity endpoint | Amaç |
| --- | --- | --- |
| Topla/güncelle | `V1/Collect` | ORCID ve/veya ResearcherID ile veriyi toplar |
| Akademisyen getir | `V1/GetResearcher` | ID, ORCID veya ResearcherID ile sade profil döndürür |
| Yayınları listele | `V1/ListPublications` | Sayfalama, arama ve yalnız onaylı yayın filtresi sunar |
| Onayları kaydet | `V1/SavePublicationSelections` | Okulda gösterilecek yayın listesini değiştirir |

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

Yanıtlar ham EF entity'leri veya sağlayıcı JSON'ları yerine kararlı profil ve
yayın DTO'ları döndürür. Eski `Researcher`, `PublicationSummary` ve
`PublicationDisplayApproval` endpoint'leri mevcut istemciler için korunur.
YÖKSİS kişisel veri içerdiğinden ayrı `Yoksis/Collect` endpoint'inde kalır ve
production'da ayrıca yetkilendirilmelidir.

Client bağımsızlığı endpoint'lerin anonim olduğu anlamına gelmez. Production
BYS host'u tüm `V1` işlemlerine Serenity oturum/izin kontrolü uygulamalı;
özellikle veri toplama ve yayın seçimi işlemlerinde oturumdaki akademisyen ile
istekteki kayıt arasında sunucu tarafında sahiplik doğrulaması yapmalıdır.

## Hedef servisler ve entegrasyon durumu

| Kimlik veya sistem | Hedef servis | Erişim beklentisi | Proje durumu |
| --- | --- | --- | --- |
| ORCID | Resmî ORCID Public API 3.0 | Herkese açık kayıtlar; isteğe bağlı erişim belirteci | **Aktif**; profil, faaliyet ve eserler alınıyor |
| OpenAlex | OpenAlex API | Genel API erişimi | **Askıda**; entegrasyon kodu ve sağlayıcı tabloları kaldırıldı |
| Google Scholar ID | Belirlenecek uygun sağlayıcı | Google'ın resmî API'si olmadığı için sağlayıcı ve kota kararı gerekli | **Planlanan**; SerpAPI kaldırıldı |
| Scopus Author ID | Resmî Elsevier Scopus API | Kurumsal abonelik ve API yetkisi gerekebilir | **Hedef**; entegrasyon henüz yok |
| Web of Science ResearcherID | Resmî Clarivate Starter API v1 | API anahtarı gerekli; ücretsiz ve kurumsal planlar var | **Aktif**; `AI` sorgusuyla erişilebilen tüm WoS veri tabanlarındaki yayınlar ve plan izin verirse atıf sayıları alınıyor |
| YÖKSİS | OzgecmisV2 SOAP servisi | Kurumsal kullanıcı/şifre, T.C. kimlik no ve gerekirse izinli çıkış IP'si | **Aktif**; 21 ana kategori alınıyor, makale/bildiri/kitap/patent ayrıntıları ortak yayın tablolarına yazılıyor |
| BYS kullanıcı kaydı | Üniversitenin kimlik ve yetki servisleri | Oturum açmış akademisyen ve kurum içi personel ID'si | **Production için gerekli** |
| ResearchGate / Academia.edu | Resmî ve izinli API bulunursa değerlendirilecek | Scraping kullanılmayacak | **Kapsam dışı** |

ORCID erişimi, kota ve sınırlama ayrıntıları için
[`docs/ORCID_API_RAPORU.md`](docs/ORCID_API_RAPORU.md) dosyasına bakın.

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
POST http://localhost:5001/Services/AcademicPerformance/Yoksis/Collect
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
│   ├── Application/        V1 client sözleşmesi ve ortak iş akışı
│   ├── Endpoints/          Serenity HTTP endpoint'leri
│   ├── Data/               EF Core bağlamı ve şema hazırlığı
│   ├── Integrations/       ORCID, Web of Science ve YÖKSİS istemcileri
│   ├── Researchers/        Kimlik ayrıştırma ve toplama akışı
│   └── Works/              Normalizasyon, özet ve gösterim onayları
├── WebClient/              Razor sayfası, Row/Columns ve TypeScript grid
├── Background/             Periyodik akademik veri yenileme görevi
└── AcademicPerformanceModule.cs  DI ve modül kayıtları
Requests/                   Manuel HTTP istekleri
docs/                       Sağlayıcı ve entegrasyon notları
Views/                      Ortak Serenity yerleşimi
wwwroot/Content/            Uygulama stilleri
```

Klasörler çalışma ortamına göre ayrılmıştır. `Service` hem Serenity UI hem de
harici client'ların kullandığı sunucu tarafını, `WebClient` yalnız tarayıcı
arayüzünü, `Background` ise HTTP'den bağımsız zamanlanmış işleri içerir.

`wwwroot/esm/`, `bin/`, `obj/` ve `node_modules/`
yeniden üretilebilir veya çalışma zamanı çıktılarıdır; Git'e eklenmez.

## Veritabanı migration'ları

Şema FluentMigrator ile yönetilir. Uygulama başlarken bekleyen migration'lar
otomatik olarak uygulanır ve sürümler `VersionInfo` tablosunda tutulur. İlk
kurulum ve güncelleme için ayrıca bir CLI komutu çalıştırmak gerekmez:

```powershell
dotnet run
```

Yeni bir şema değişikliği için
`Service/Data/Migrations/` altında benzersiz, artan zaman damgalı bir migration
oluşturun; örneğin `202608260001_AddCollectionStatus.cs`. Daha önce uygulanmış
migration dosyalarını değiştirmeyin. Yeni migration eklemek yerine
`AcademicDbContext` modelini tek başına değiştirmek veritabanını güncellemez.

Migration'lar yalnız SQL Server içindir. `Up()` ileri şema değişikliğini,
`Down()` güvenli geri alma sırasını içermelidir. FluentMigrator uygulanmış
sürümleri `VersionInfo` üzerinden takip ettiği için migration gövdelerinde
manuel `table exists` kontrolleri kullanılmaz.

## Veri modeli

| Tablo | İçerik |
| --- | --- |
| `Researchers` | Akademisyen, ORCID, Web of Science ResearcherID ve YÖKSİS Araştırmacı ID eşleşmesi |
| `OrcidProfiles` / `OrcidWorks` | ORCID profil, faaliyet, eser ve ham JSON verisi |
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

ORCID atıf sayısı, h-index ve i10-index sağlamaz. Starter API v1 hazır profil
metrikleri sunmaz. Bütün yayınlarda atıf sayısı gelirse h-index ve toplam atıf
uygulama içinde yayınlardan hesaplanır. Profil, kurum ve hakemlik bilgileri Starter
API'den alınamaz. Onaylı yayınlar `PublicationDisplayApproval/ListApproved`
servisi üzerinden alınabilir.

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
dotnet user-secrets set "WebOfScience:ApiKey" "<CLARIVATE_API_KEY>"
dotnet user-secrets set "Yoksis:Username" "<KURUMSAL_KULLANICI>"
dotnet user-secrets set "Yoksis:Password" "<KURUMSAL_SIFRE>"
```

YÖKSİS kimlik bilgileri hem SOAP gövdesinde hem HTTP Basic Authentication
başlığında gönderilir; uygulama bunları yanıta, ham XML alanına veya loglara
yazmaz. Basic Authentication yalnızca Base64 kodlaması kullandığı için HTTPS
adresi korunmalıdır. Endpoint production'da BYS kimlik/yetki kontrolü arkasına
alınmadan dış ağa açılmamalıdır.

### Zamanlanmış toplama

Sunucu içindeki periyodik yenileme varsayılan olarak kapalıdır. Açmak için
`academicsettings.json` veya production yapılandırmasında şu bölümü değiştirin:

```json
"AcademicPerformance": {
  "ScheduledCollection": {
    "Enabled": true,
    "InitialDelaySeconds": 60,
    "IntervalMinutes": 1440,
    "BatchSize": 100
  }
}
```

Görev kayıtlı akademisyenleri gruplar halinde okur ve V1 endpoint'ine HTTP
isteği göndermek yerine aynı uygulama servisini çağırır. Sağlayıcı önbelleği
güncel kayıtlar için gereksiz dış API çağrılarını engeller. T.C. kimlik numarası
saklanmadığı için YÖKSİS otomatik göreve dahil edilmez.

Bu yerleşik zamanlayıcı tek sunucu örneği içindir. Uygulama birden fazla instance
ile çalışacaksa görevi yalnız bir instance'ta etkinleştirin veya veritabanı
kilitli merkezi bir görev sistemi kullanın.

Mevcut UI bir entegrasyon prototipidir. **Yayınlarımı Getir** için sağlayıcı
kimliği tarayıcı `localStorage` alanında hatırlanır. BYS entegrasyonunda ORCID,
oturum açmış kullanıcı kaydından sunucu tarafında alınmalı; prototip izin
servisi gerçek kimlik/yetki servisiyle değiştirilmelidir. Yayın onay endpoint'leri
de production'a geçmeden önce kullanıcı sahipliği kontrolü uygulamalıdır.

Hedef SQL Server veritabanı önceden oluşturulmuş olmalıdır. Uygulama açılışta
tablo, foreign key ve indeks migration'larını otomatik uygular; veritabanını
oluşturma veya silme yetkisi istemez.

Web of Science Starter API'nin ücretsiz deneme planı günde 50 istek sunar fakat
atıf sayılarını döndürmez. Uygun Web of Science aboneliğine bağlı kurumsal planda
atıf sayıları ve daha yüksek kota kullanılabilir. Resmî kaynaklar:
[Starter API](https://developer.clarivate.com/apis/wos-starter) ve
[Swagger şeması](https://developer.clarivate.com/apis/wos-starter/swagger).

YÖKSİS sözleşmesi:
[OzgecmisV2 WSDL](https://servisler.yok.gov.tr/ws/OzgecmisV2?WSDL). WSDL'de
servis adresi HTTP görünse de yapılandırmada şifreli HTTPS adresi kullanılır.

## Doğrulama

Projede henüz otomatik test projesi yoktur. Değişikliklerden sonra en az
`dotnet build` çalıştırın ve etkilenen akışı arayüzden veya `.http` dosyasından
kontrol edin. Yeni iş kuralları için dış API'leri taklit eden birim testleri
eklenmelidir.
