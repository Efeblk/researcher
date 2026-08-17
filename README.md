# Akademik Performans Veri Toplayıcı

Bu proje, bir akademisyene ait kimlikleri kullanarak akademik verileri farklı
kaynaklardan toplar ve veritabanına kaydeder.

Proje, okulun Serenity uygulamasına daha sonra modül olarak eklenebilmesi için
Serenity'nin modül, endpoint, service, repository ve dependency injection
yaklaşımına göre düzenlenmiştir. Şimdilik konsol uygulaması olarak çalışır ve
yerel geliştirmede SQLite kullanır; henüz Serenity web paketleri ve arayüzü
eklenmemiştir.

## Uygulama akışı

```text
Program
  -> AcademicPerformanceConsoleHost
  -> ResearcherEndpoint
  -> ResearcherCollectionService
  -> OpenAlex / Google Scholar / Scopus / Web of Science istemcileri
  -> ResearcherRepository
  -> SQLite veya SQL Server
```

- `Program`, dependency injection sistemini kurar ve geçici konsol hostunu
  çalıştırır.
- `ResearcherEndpoint`, isteği alır ve bütün işlemi koordine eder. Okulun
  Serenity projesine geçildiğinde gerçek bir Serenity `ServiceEndpoint` sınıfına
  dönüştürülecektir.
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
  izlenerek akademisyenin bütün yayın sayfaları çekilir.
- Google Scholar ID ile profil ve yayın bilgileri SerpAPI üzerinden alınır.
  SerpAPI, Google'ın resmî API'si değildir; Google Scholar sayfalarını scrape
  ederek yapılandırılmış JSON üretir. Bütün yayınlar sayfalama ile çekilir;
  her 100 yayın için ayrı bir SerpAPI sorgusu kullanılır ve bu sorgular bireysel
  hesabın kotasından düşebilir.
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

Migration kullanmadığımız bu geliştirme aşamasında modele yeni bir tablo veya
alan eklendiğinde eski SQLite şeması otomatik güncellenmez. Böyle bir değişiklikten
sonra `dotnet run -- --clear-db` komutu bir kez çalıştırılmalıdır.

OpenAlex, Google Scholar, Scopus ve Web of Science verilerinin her birinin kendi
`LastUpdatedAt` değeri vardır. Aynı kimlik tekrar verildiğinde ilgili sağlayıcının
verisi son 24 saat içinde güncellenmişse API çağrısı yapılmaz; kayıtlı veri
kullanılır. Süre dolduğunda yalnızca süresi dolan ve kimliği o istekte verilen
sağlayıcı yeniden sorgulanır. Süre `ProviderCache:MaxAgeHours` ayarından
değiştirilebilir.

Bu dört `LastUpdatedAt` sütunu eski SQLite ve SQL Server veritabanlarına veri
silinmeden otomatik olarak eklenir. Daha kapsamlı model değişikliklerinde, migration
kullanılana kadar yukarıdaki `--clear-db` açıklaması geçerlidir.

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

Argüman verilmeden çalıştırıldığında test ORCID ve Google Scholar ID değerleri
kullanılır:

```shell
dotnet run
```

Kimlikler türleri biçimlerinden otomatik olarak anlaşıldığı için başlarına
`--orcid`, `--scholar`, `--scopus` veya `--wos` yazılması gerekmez. Tek bir
kimlik verilebilir:

```shell
dotnet run -- 0000-0003-2812-9917
dotnet run -- dYpPMQEAAAAJ
dotnet run -- 56962745700
dotnet run -- A-1009-2008
```

Birden fazla kimlik herhangi bir sırayla birlikte verilebilir:

```shell
dotnet run -- 56962745700 dYpPMQEAAAAJ 0000-0003-2812-9917
```

Program aşağıdaki biçimleri kullanarak kimlik türünü belirler:

- `0000-0000-0000-000X` biçimi: ORCID
- Yalnızca rakamlardan oluşan değer: Scopus Author ID
- 12 karakterlik harf, rakam, `_` veya `-` içeren değer: Google Scholar ID
- `A-1009-2008` benzeri değer: Web of Science ResearcherID

Eski, isimlendirilmiş argüman biçimleri de geriye dönük uyumluluk için çalışmaya
devam eder. Aynı türden iki kimlik verilirse program hangisini kullanacağını
tahmin etmek yerine hata gösterir.

Program API verilerini topladıktan sonra seçilen veritabanına kaydeder. Aynı
akademisyen tekrar sorgulanırsa yeni bir akademisyen kaydı açmak yerine mevcut
kayıt kullanılır. İlgili sağlayıcının kayıtlı verisi 24 saatten eskiyse API'den
yenilenir; güncelse API kotası harcanmaz.

## Veritabanı komutları

Tablolardaki kayıt sayılarını görmek için:

```shell
dotnet run -- --db-info
```

Veritabanındaki akademisyenlerden birini rastgele seçip kayıtlı bilgilerini
görmek için:

```shell
dotnet run -- -db--random
```

Daha standart yazımdaki `dotnet run -- --db-random` komutu da aynı işlemi
yapar. Bu özet görünüm yayınları tek tek yazmaz. OpenAlex ve Google Scholar yayın
sayılarını; Google Scholar için toplam atıf, h-index ve i10-index değerlerini;
varsa Scopus ve Web of Science metriklerini gösterir. Veritabanında hiç
akademisyen yoksa bilgilendirme mesajı gösterilir.

Yerel SQLite veritabanındaki bütün verileri temizlemek için:

```shell
dotnet run -- --clear-db
```

`--clear-db`, mevcut SQLite dosyasını sıfırlar ve boş tabloları yeniden
oluşturur. Güvenlik nedeniyle SQL Server seçiliyken çalışmaz.

## Klasör yapısı

```text
Initialization/
  ApplicationConfiguration.cs          Uygulama ayarlarını yükler

Modules/AcademicPerformance/
  AcademicPerformanceModule.cs         Dependency injection kayıtları
  Console/                              Geçici konsol hostu ve çıktı sınıfları
  Data/                                 EF Core, SQLite ve SQL Server altyapısı
  Integrations/
    OpenAlex/                           OpenAlex istemcisi ve veri modelleri
    GoogleScholar/                      SerpAPI istemcisi ve veri modelleri
    Scopus/                             Elsevier istemcisi ve veri modelleri
    WebOfScience/                       Clarivate istemcisi ve veri modelleri
  Researchers/
    Researcher.cs                       Ana akademisyen modeli
    ResearcherCollectRequest.cs         Endpoint istek modeli
    ResearcherCollectResponse.cs        Endpoint cevap modeli
    ResearcherEndpoint.cs               İşlemi koordine eden giriş noktası
    ResearcherCollectionService.cs      Veri toplama iş mantığı
    ResearcherIdentifierParser.cs       Kimlik türlerini belirler
    ResearcherRepository.cs             Veritabanı işlemleri

Program.cs                              Uygulamanın başlangıç noktası
```
