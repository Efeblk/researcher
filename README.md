# Akademik Performans Veri Toplayıcı

Bu proje, bir akademisyene ait kimlikleri kullanarak akademik verileri farklı
kaynaklardan toplar ve veritabanına kaydeder.

Proje, .NET 10 üzerinde Serenity LTS `10.3.5` kullanan bir HTTP servisidir.
Yerel geliştirmede SQLite kullanır; üniversitenin sistemine geçildiğinde SQL
Server kullanılabilir. Şimdilik bir web arayüzü yoktur. İstekler Serenity
`ServiceEndpoint` adreslerine JSON olarak gönderilir.

## Uygulama akışı

```text
Program
  -> Serenity ResearcherEndpoint
  -> ResearcherCollectionHandler
  -> ResearcherCollectionService
  -> OpenAlex / Google Scholar / Scopus / Web of Science istemcileri
  -> ResearcherRepository
  -> SQLite veya SQL Server
```

- `Program`, ASP.NET Core web sunucusunu ve Serenity endpoint kurallarını başlatır.
- `ResearcherEndpoint`, `ServiceEndpoint` sınıfından türeyen gerçek HTTP giriş
  noktasıdır.
- `ResearcherCollectionHandler`, endpoint'ten gelen isteğin veri toplama ve
  kaydetme akışını koordine eder.
- `ResearcherCollectionService`, dış veri kaynaklarını birbirinden bağımsız
  sorgulayan iş katmanıdır.
- `ResearcherRepository`, akademisyen verilerinin kaydedilmesinden sorumludur.
- `AcademicPerformanceModule`, modüldeki servisleri dependency injection
  sistemine kaydeder.

## Akademisyen kimlikleri ve entegrasyon durumu

| Kimlik veya platform | Kullanılan kaynak | Hesap veya erişim | Durum |
| --- | --- | --- | --- |
| ORCID | OpenAlex API | Şu an anahtarsız genel erişim | Çalışıyor |
| Google Scholar ID | SerpAPI | Bireysel SerpAPI üyeliği ve API anahtarı | Çalışıyor |
| Scopus Author ID | Resmî Elsevier Scopus API | Bireysel Elsevier geliştirici üyeliği ve API anahtarı | Çalışıyor; tam erişim kurum aboneliğine bağlı olabilir |
| Web of Science ResearcherID | Resmî Clarivate Researcher API | Ücretli kurumsal API lisansı gerekli | Kod hazır, lisans bekleniyor |
| YÖKSİS | Resmî YÖK kurumsal web servisi | Üniversite tarafından kurumsal erişim gerekli | Servis bilgileri bekleniyor |
| ResearchGate | Herkese açık resmî API yok | API hesabı yok | Atlandı; otomatik veri çekme yasak |
| Academia.edu | Herkese açık resmî API yok | API hesabı yok | Atlandı; otomatik veri çekme yasak |

ORCID, Google Scholar ID, Scopus Author ID ve ResearcherID birbirinden bağımsız
kimliklerdir. Kullanıcı bunlardan yalnızca elinde bulunanları verebilir. Bir
kimliğin eksik olması diğer kaynakların sorgulanmasını engellemez.

ResearchGate ve Academia.edu birer akademik platformdur; yukarıdaki teknik
kimliklerle aynı türde standart araştırmacı kimliği değildir.

## API hesaplarının sahipliği

Geliştirme aşamasında SerpAPI ve Elsevier API anahtarları bireysel geliştirici
üyelikleri üzerinden alınmıştır. Bu hesaplar üniversiteye ait kurumsal hesaplar
değildir. Anahtarlar yalnızca yerel User Secrets içinde tutulur ve Git deposuna
eklenmez.

Üniversite sistemine geçilirken kişisel hesaplara bağlı anahtarlar kullanılmamalı;
kurum adına açılmış servis hesapları veya üniversitenin sağladığı API erişimleri
ile değiştirilmelidir. Böylece erişim bir kişinin hesabına bağlı kalmaz ve kota,
faturalandırma, anahtar yenileme ile yetki yönetimi üniversite tarafından
yürütülebilir.

OpenAlex sorguları şu anda API anahtarı olmadan yapılmaktadır. Web of Science
Researcher API için ücretli Clarivate lisansı, YÖKSİS için ise YÖK tarafından
üniversiteye sağlanan kurumsal servis erişimi beklenmektedir.

## Kullanılan veri kaynakları

- ORCID, OpenAlex'te akademisyeni bulmak için kullanılır; akademisyen ve yayın
  bilgileri OpenAlex API üzerinden alınır. OpenAlex'in `next_cursor` değeri
  izlenerek akademisyenin bütün yayın sayfaları çekilir. Her çalışma için
  yayımlandığı kaynağın adı, kaynak türü, OpenAlex kaynak kimliği ve bağlantısı
  da veritabanında saklanır.
- Google Scholar ID ile profil ve yayın bilgileri SerpAPI üzerinden alınır.
  SerpAPI, Google'ın resmî API'si değildir; Google Scholar sayfalarını scrape
  ederek yapılandırılmış JSON üretir. Bütün yayınlar sayfalama ile çekilir;
  her 100 yayın için ayrı bir SerpAPI sorgusu kullanılır ve bu sorgular bireysel
  hesabın kotasından düşebilir. Google Scholar'ın çalışma başına verdiği dergi,
  konferans veya kitap bilgisi `Publication` alanında saklanır.
- Scopus Author ID ile profil ve metrik bilgileri resmî Elsevier Scopus Author
  Retrieval API üzerinden alınır.
- ResearcherID için Clarivate Web of Science Researcher API istemcisi hazırdır.
  Bu API, normal Web of Science aboneliğine ek ücretli lisans gerektirdiği için
  şimdilik kullanılmamaktadır.
- YÖKSİS için herkese açık endpoint ve veri sözleşmesi bulunmamaktadır.
  Üniversite servis adresini, yetkilendirme yöntemini ve WSDL/OpenAPI belgesini
  sağladığında entegrasyon yapılabilir. YÖK Akademik sayfası scrape edilmeyecektir.
- ResearchGate ve Academia.edu resmî API sunmadığı ve otomatik veri toplamayı
  kullanım şartlarında yasakladığı için bu platformlara entegrasyon yapılmaz.

## Kaynaklar ve dokümantasyon

- [Serenity geliştirici rehberi](https://serenity.is/docs)
- [Serenity Service Endpoints](https://docs.serenity.is/docs/services/service_endpoints)
- [OpenAlex API dokümantasyonu](https://help.openalex.org/api/)
- [SerpAPI Google Scholar Author API](https://serpapi.com/google-scholar-author-api)
- [Elsevier Scopus Author Retrieval API](https://dev.elsevier.com/documentation/AuthorRetrievalAPI.wadl)
- [Clarivate Web of Science Researcher API](https://developer.clarivate.com/apis/wos-researcher)
- [YÖK Akademik](https://akademik.yok.gov.tr/AkademikArama/index.jsp)
- [YÖK 2024 İdare Faaliyet Raporu](https://eski.yok.gov.tr/Documents/Kurumsal/strateji_dairesi/faaliyet_raporlari/2024-idare-faaliyet-raporu.pdf)
- [ResearchGate kullanım şartları](https://www.researchgate.net/terms-of-service)
- [Academia.edu kullanım şartları](https://www.academia.edu/terms)

## Gereksinimler

- .NET 10 SDK
- Google Scholar sorguları için bir SerpAPI anahtarı
- Scopus sorguları için bir Elsevier API anahtarı
- Web of Science sorguları için bir Clarivate API anahtarı ve Researcher API
  lisansı

Buradaki SerpAPI ve Elsevier anahtarları geliştirme ortamında bireysel üyeliklere
aittir. Üretim ortamında üniversitenin yönettiği kurumsal anahtarlarla
değiştirilmelidir.

SerpAPI anahtarı proje klasöründe User Secrets ile tanımlanır:

```shell
dotnet user-secrets set "SerpApi:ApiKey" "GERCEK_API_ANAHTARI"
```

Anahtar kaynak kodda veya `appsettings.json` dosyasında tutulmaz.

Elsevier API anahtarı da User Secrets ile tanımlanır:

```shell
dotnet user-secrets set "Elsevier:ApiKey" "GERCEK_ELSEVIER_API_ANAHTARI"
```

Scopus tam veri erişimi üniversitenin Elsevier aboneliğine ve ağ yetkilerine
bağlı olabilir.

Clarivate API anahtarı User Secrets ile tanımlanır:

```shell
dotnet user-secrets set "Clarivate:ApiKey" "GERCEK_CLARIVATE_API_ANAHTARI"
```

Web of Science Researcher API, normal Web of Science aboneliğine ek olarak
ücretli API lisansı gerektirir. Bu anahtarın üniversiteden alınması beklenir.

## Veritabanı

Uygulama iki veritabanı sağlayıcısını destekler:

- `Sqlite`: Yerel geliştirme için kullanılır.
- `SqlServer`: Üniversitenin sistemiyle entegrasyon için kullanılacaktır.

Varsayılan sağlayıcı SQLite'tır. Ayarlar `appsettings.json` dosyasındadır:

```json
{
  "Database": {
    "Provider": "Sqlite"
  },
  "ProviderCache": {
    "MaxAgeHours": 24
  },
  "ConnectionStrings": {
    "AcademicDatabase": "Data Source=academic.db"
  }
}
```

Program ilk çalıştığında proje klasöründe `academic.db` dosyasını ve gerekli
tabloları otomatik olarak oluşturur. Bu dosya Git deposuna eklenmez.

Migration kullanmadığımız bu geliştirme aşamasında bilinen yeni sütunlar
`AcademicDatabaseInitializer` tarafından veri silinmeden eklenir. Daha kapsamlı
şema değişikliklerinde veritabanı yeniden oluşturulmalı veya migration
hazırlanmalıdır.

OpenAlex, Google Scholar, Scopus ve Web of Science verilerinin her birinin kendi
`LastUpdatedAt` değeri vardır. Aynı kimlik tekrar verildiğinde ilgili sağlayıcının
verisi son 24 saat içinde güncellenmişse API çağrısı yapılmaz; kayıtlı veri
kullanılır. Süre dolduğunda yalnızca süresi dolan ve kimliği o istekte verilen
sağlayıcı yeniden sorgulanır. Süre `ProviderCache:MaxAgeHours` ayarından
değiştirilebilir.

Bu dört `LastUpdatedAt` sütunu eski SQLite ve SQL Server veritabanlarına veri
silinmeden otomatik olarak eklenir.

### SQL Server'a geçmek

Sağlayıcı ve güvenli bağlantı cümlesi User Secrets üzerinden değiştirilebilir:

```shell
dotnet user-secrets set "Database:Provider" "SqlServer"
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "SQL_SERVER_CONNECTION_STRING"
```

User Secrets içindeki değerler `appsettings.json` ayarlarının üzerine yazılır.
Şema henüz geliştirme aşamasında olduğu için migration kullanılmamaktadır.
SQLite ve boş bir SQL Server veritabanındaki tablolar ilk çalıştırmada oluşturulur.
Şema kesinleştiğinde SQL Server migration'ları yeniden eklenecektir.

Tekrar SQLite'a dönmek için bu iki User Secrets ayarı temizlenebilir:

```shell
dotnet user-secrets remove "Database:Provider"
dotnet user-secrets remove "ConnectionStrings:AcademicDatabase"
```

## Çalıştırma

Birinci terminalde proje klasörüne girip sunucuyu başlatın:

```shell
make run
```

Sunucu varsayılan olarak aşağıdaki adreste çalışır:

```text
http://localhost:5000
```

`dotnet run` komutu da `make run` ile aynı işi yapar. Sunucu çalışırken birinci
terminal açık bırakılmalıdır.

## Sunucuya istek gönderme

İstekler ikinci bir terminalden gönderilir. Önce sunucunun çalıştığını kontrol
edin:

```shell
make health
```

Aynı kontrol doğrudan `curl` ile de yapılabilir:

```shell
curl --silent --show-error http://localhost:5000/
```

Başarılı cevap:

```json
{
  "application": "AcademicCollectorDemo",
  "status": "Running"
}
```

### Akademisyen verisi toplama

Serenity endpoint adresi:

```text
POST /Services/AcademicPerformance/Researcher/Collect
```

Örnek istek:

```http
POST http://localhost:5000/Services/AcademicPerformance/Researcher/Collect
Content-Type: application/json

{
  "Identifiers": [
    "0000-0003-2812-9917",
    "tQgMPzcAAAAJ"
  ],
  "UseTestIdentifiers": false
}
```

Tek bir kimliği Makefile ile göndermek için:

```shell
make collect ID=tQgMPzcAAAAJ
```

Aynı isteğin `curl` karşılığı:

```shell
curl --silent --show-error \
  --request POST \
  --header "Content-Type: application/json" \
  --data '{"Identifiers":["tQgMPzcAAAAJ"],"UseTestIdentifiers":false}' \
  http://localhost:5000/Services/AcademicPerformance/Researcher/Collect
```

Birden fazla kimlik gönderilecekse JSON içindeki `Identifiers` listesine
eklenir:

```shell
curl --silent --show-error \
  --request POST \
  --header "Content-Type: application/json" \
  --data '{"Identifiers":["0000-0003-2812-9917","tQgMPzcAAAAJ"],"UseTestIdentifiers":false}' \
  http://localhost:5000/Services/AcademicPerformance/Researcher/Collect
```

Kimliklerin türleri biçimlerinden otomatik olarak belirlenir:

- `0000-0000-0000-000X` biçimi: ORCID
- Yalnızca rakamlardan oluşan değer: Scopus Author ID
- 12 karakterlik harf, rakam, `_` veya `-` içeren değer: Google Scholar ID
- `A-1009-2008` benzeri değer: Web of Science ResearcherID

Endpoint API verilerini topladıktan sonra seçilen veritabanına kaydeder. Aynı
akademisyen tekrar sorgulanırsa yeni bir akademisyen kaydı açmak yerine mevcut
kayıt kullanılır. İlgili sağlayıcının kayıtlı verisi 24 saatten eskiyse API'den
yenilenir; güncelse API kotası harcanmaz.

### Rastgele akademisyen özeti

```http
POST http://localhost:5000/Services/AcademicPerformance/Researcher/Random
Content-Type: application/json

{}
```

Makefile ile:

```shell
make random
```

Doğrudan `curl` ile:

```shell
curl --silent --show-error \
  --request POST \
  --header "Content-Type: application/json" \
  --data '{}' \
  http://localhost:5000/Services/AcademicPerformance/Researcher/Random
```

Bu cevap yayınları tek tek döndürmez. OpenAlex ve Google Scholar yayın sayılarını,
Google Scholar için toplam atıf, h-index ve i10-index değerlerini, varsa Scopus
ve Web of Science metriklerini içeren kısa bir JSON özeti döndürür.

Hazır HTTP istekleri `Requests/AcademicPerformance.http` dosyasındadır. VS Code,
Visual Studio veya JetBrains HTTP Client ile çalıştırılabilir.

Sık kullanılan işlemler için proje kökünde bir `Makefile` da vardır:

```shell
make help
make run
make clean
make health
make collect ID=tQgMPzcAAAAJ
make random
```

`make health`, `make collect` ve `make random` çalıştırılmadan önce sunucu ayrı
bir terminalde `make run` ile başlatılmalıdır. Varsayılan adres
`http://localhost:5000` değeridir. Başka bir adres için örneğin
`make health HOST=http://localhost:5078` kullanılabilir.

`make clean`, proje kökündeki `academic.db` SQLite veritabanını ve ona ait WAL
dosyalarını siler. Sunucu çalışıyorsa güvenlik amacıyla silme işlemi durdurulur.
Program sonraki açılışında boş veritabanını yeniden oluşturur.

Endpoint'ler şu anda yalnızca yerel geliştirme için yetkilendirmesizdir ve
`launchSettings.json` localhost adresini kullanır. Üniversitenin Serenity
uygulamasına alınırken endpoint'e kurumun `ServiceAuthorize` izin anahtarı
eklenmelidir.

## Klasör yapısı

```text
Modules/AcademicPerformance/
  AcademicPerformanceModule.cs         Dependency injection kayıtları
  Data/                                 EF Core, SQLite ve SQL Server altyapısı
  Endpoints/
    ResearcherEndpoint.cs               Serenity HTTP ServiceEndpoint sınıfı
  Integrations/
    OpenAlex/                           OpenAlex istemcisi ve veri modelleri
    GoogleScholar/                      SerpAPI istemcisi ve veri modelleri
    Scopus/                             Elsevier istemcisi ve veri modelleri
    WebOfScience/                       Clarivate istemcisi ve veri modelleri
  Researchers/
    Researcher.cs                       Ana akademisyen modeli
    ResearcherCollectRequest.cs         Endpoint istek modeli
    ResearcherCollectResponse.cs        Endpoint cevap modeli
    ResearcherCollectionHandler.cs      İsteğin iş akışını koordine eder
    ResearcherCollectionService.cs      Veri toplama iş mantığı
    ResearcherIdentifierParser.cs       Kimlik türlerini belirler
    ResearcherRepository.cs             Veritabanı işlemleri
    ResearcherSummaryFactory.cs         Rastgele kayıt özetini hazırlar

Properties/launchSettings.json          Yerel HTTP adresi
Requests/AcademicPerformance.http       Hazır HTTP istekleri
Makefile                                Kısa geliştirme ve HTTP komutları
Program.cs                              Web uygulamasının başlangıç noktası
```
