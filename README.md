# Akademik Performans Veri Toplayıcı

Akademisyen kimlikleriyle farklı kaynaklardan profil ve akademik çalışma
verilerini toplayan Serenity HTTP servisidir.

## Gereksinimler

- .NET 10 SDK
- Git
- İstek komutları için `make` ve `curl`
- Google Scholar sorguları için SerpAPI hesabı ve API anahtarı
- İsteğe bağlı olarak SQLite verilerini görüntülemek için DBeaver

OpenAlex şu anda anahtarsız kullanılmaktadır. Web of Science için üniversitenin
ücretli Clarivate Researcher API lisansına ihtiyaç vardır.

## Teknoloji yığını

| Teknoloji | Kullanım amacı |
| --- | --- |
| .NET 10 / ASP.NET Core | HTTP sunucusu ve uygulama altyapısı |
| Serenity LTS 10.3.5 | Üniversite sistemiyle uyumlu servis endpoint yapısı |
| Entity Framework Core 10 | Veritabanı işlemleri |
| SQLite | Yerel geliştirme veritabanı |
| HttpClient | Harici API istekleri |
| .NET User Secrets | API anahtarlarını kaynak koddan ayrı tutma |

## Hedeflenen kaynaklar

| Kimlik veya platform | Kullanılan kaynak | Hesap veya erişim | Durum |
| --- | --- | --- | --- |
| ORCID | OpenAlex API | Şu an anahtarsız genel erişim | Çalışıyor |
| Google Scholar ID | SerpAPI | Bireysel SerpAPI üyeliği ve API anahtarı | Çalışıyor |
| Web of Science ResearcherID | Resmî Clarivate Researcher API | Ücretli kurumsal API lisansı gerekli | Kod hazır, lisans bekleniyor |
| YÖKSİS | Resmî YÖK kurumsal web servisi | Üniversite tarafından kurumsal erişim gerekli | Servis bilgileri bekleniyor |
| ResearchGate | Herkese açık resmî API yok | API hesabı yok | Atlandı; otomatik veri çekme yasak |
| Academia.edu | Herkese açık resmî API yok | API hesabı yok | Atlandı; otomatik veri çekme yasak |

SerpAPI, Google'ın resmî Google Scholar API'si değildir. Google Scholar
sayfalarını tarayarak yapılandırılmış JSON üretir ve her sorgu SerpAPI
kotasından düşebilir. Geliştirmede kullanılan hesap bireyseldir.

## Şu anda neler yapabiliyor?

- Bir istekte ORCID, Google Scholar ID ve ResearcherID değerlerini bağımsız
  olarak kabul eder.
- ORCID üzerinden OpenAlex akademisyen profilini ve bütün çalışma sayfalarını
  toplar.
- Google Scholar ID üzerinden profil, ilgi alanları, yayınlar, toplam atıf,
  h-index ve i10-index bilgilerini toplar.
- OpenAlex ve SerpAPI'nin döndürdüğü orijinal JSON yanıtlarını, C# modelinde
  tanımlanmayan alanlar kaybolmadan saklar.
- Makale, kitap, kitap bölümü, bildiri, tez ve veri seti gibi çalışma türlerini
  ortak kategorilere ayırır.
- Verileri sağlayıcıya özel tabloların yanında ortak `AcademicWorks` tablosuna
  yazar.
- İsteğe bağlı olarak erişilebilir PDF dosyalarını indirir; dosyayı diskte,
  dosya bilgilerini ve SHA-256 özetini `AcademicWorkFiles` tablosunda saklar.
- Her sağlayıcının `LastUpdatedAt` değerini ayrı tutar. Ham veri eksiksizse
  varsayılan 24 saatlik süre dolmadan aynı kimlik için yeniden API sorgusu yapmaz.
- Verileri yerelde SQLite'a kaydeder.
- Rastgele bir akademisyenin kısa profil ve metrik özetini döndürür.

> [!WARNING]
> Farklı zamanlarda yalnızca birbirinden farklı platform kimlikleri gönderilirse
> sistem bunların aynı kişiye ait olduğunu kesin olarak anlayamaz. Üniversite
> entegrasyonunda bütün kimlikler okulun değişmeyen `UniversityPersonnelId`
> değerine bağlanmalıdır.

### Ham API verisi

API'den gelen okunabilir alanların yanında orijinal JSON da saklanır:

| Tablo | Ham veri sütunu | İçerik |
| --- | --- | --- |
| `OpenAlexProfiles` | `RawDataJson` | OpenAlex yazar yanıtı |
| `OpenAlexProfiles` | `WorksResponsePagesJson` | Bütün OpenAlex çalışma sayfaları |
| `OpenAlexWorks` | `RawDataJson` | Tek çalışmanın eksiksiz OpenAlex nesnesi |
| `GoogleScholarProfiles` | `RawDataJson` | İlk SerpAPI profil yanıtı |
| `GoogleScholarProfiles` | `ResponsePagesJson` | Bütün SerpAPI profil/yayın sayfaları |
| `GoogleScholarWorks` | `RawDataJson` | Profil listesindeki tek yayın nesnesi |
| `GoogleScholarWorks` | `DetailRawDataJson` | Yayının ayrı SerpAPI ayrıntı yanıtı |
| `AcademicWorks` | `ProviderPayload` | Çalışmanın sağlayıcıdaki ham nesnesi |
| `AcademicWorks` | `ProviderDetailPayload` | Varsa ayrı yayın ayrıntı yanıtı |

### PDF dosyaları

PDF indirme varsayılan olarak kapalıdır. Açıldığında:

- Yalnızca hocanın OpenAlex yazar kaydında veya Google Scholar profilinde
  listelenen kendi/ortak yazarlı çalışmaları için PDF aranır.
- Hocaya atıf yapan başka akademisyenlerin çalışmalarının PDF'leri indirilmez;
  şu anda yalnızca mevcut atıf sayısı ve bağlantısı saklanır.
- OpenAlex için yalnızca açık erişimli çalışmalardaki PDF adayları kullanılır.
- Google Scholar için SerpAPI yayın ayrıntısında `file_format: PDF` olarak gelen
  kaynaklar kullanılır. Bunun için `GoogleScholar:CollectArticleDetails` ayarının
  da açık olması gerekir.
- İndirilen içeriğin gerçekten PDF olduğu dosya imzasından doğrulanır.
- Her dosya varsayılan olarak
  `Storage/Pdfs/{ResearcherId}/{AcademicWorkId}.pdf` yoluna yazılır.
- Kaynak adresi, göreli dosya yolu, boyut, MIME türü, SHA-256 özeti, indirme
  zamanı, durum ve hata bilgisi `AcademicWorkFiles` tablosunda tutulur.
- Aynı dosya diskte varsa tekrar indirilmez.

`AcademicWorks` satırları sağlayıcı kayıtlarıdır. Aynı yayın OpenAlex ve Google
Scholar'da bulunuyorsa iki sağlayıcı satırı olabilir; bu nedenle feedback'teki
“incelenen kayıt” sayısı her zaman benzersiz yayın sayısı anlamına gelmez. PDF
feedback'i toplam incelenen kayıt, PDF kaynağı bulunan kayıt, indirilen,
önceden bulunan ve indirilemeyen sayılarını ayrı gösterir.

PDF dosyaları SQLite içine yazılmaz ve `Storage/` klasörü Git'e gönderilmez.
Yayınların telif ve yeniden kullanım koşulları kaynak lisansına tabidir.
Bu özellik açıkken sunucu, PDF kaynak adreslerine giden harici HTTP istekleri
yapar; sunucunun dışarıdan internete açılması gerekmez.

> [!WARNING]
> `GoogleScholar:CollectArticleDetails` ayarı varsayılan olarak `false` değerindedir.
> SerpAPI yayın ayrıntısı her çalışma için ayrı bir istek kullanır. Örneğin 58
> yayını olan bir profil, profil sayfası isteklerine ek olarak yaklaşık 58 arama
> kredisi daha kullanabilir. Bu nedenle ayrıntılar yalnızca bilinçli olarak
> açılmalıdır.

## Çalıştırma

Projeyi GitHub'dan indir ve proje klasörüne gir:

```shell
git clone https://github.com/Efeblk/researcher.git
cd researcher
```

SerpAPI anahtarını User Secrets'a kaydet:

```shell
dotnet user-secrets set "SerpApi:ApiKey" "GERCEK_API_ANAHTARI"
```

### Uygulama ayarları

Hassas olmayan bütün toplama ayarları proje kökündeki
`academicsettings.json` dosyasında birlikte tutulur. Google Scholar yayınlarının
ayrı ayrıntı yanıtlarını da toplamak istersen dosyadaki ilgili değeri değiştir:

```json
"GoogleScholar": {
  "CollectArticleDetails": true
}
```

PDF indirmeyi açmak istersen:

```json
"PdfDownload": {
  "Enabled": true
}
```

Varsayılan tek dosya boyutu sınırı 50 MB'dir. Değiştirmek için:

```json
"PdfDownload": {
  "MaxFileSizeMb": 100
}
```

| Ayar | Varsayılan | Anlamı |
| --- | --- | --- |
| `ProviderCache:MaxAgeHours` | `24` | Sağlayıcı verisinin yeniden sorgulanma süresi |
| `GoogleScholar:CollectArticleDetails` | `false` | Her Google Scholar yayını için ek ayrıntı isteği |
| `PdfDownload:Enabled` | `false` | Uygun PDF dosyalarını indirme |
| `PdfDownload:StorageRoot` | `Storage` | PDF ana klasörü |
| `PdfDownload:MaxFileSizeMb` | `50` | Tek PDF için boyut sınırı |
| `PdfDownload:RequestTimeoutSeconds` | `60` | PDF isteği zaman aşımı |
| `PdfDownload:MaxRedirects` | `5` | PDF isteği yönlendirme sınırı |


Sunucuyu başlat:

```shell
make run
```

Sunucu varsayılan olarak `http://localhost:5000` adresinde çalışır.

Başka bir terminalde sunucuyu kontrol et:

```shell
make health
```

### Akademisyen verisi toplama

Tek kimlik:

```shell
make collect ID="0000-0001-8560-7482"
```

Birden fazla kimlik:

```shell
make collect ID="o3ujRIMAAAAJ 0000-0001-8560-7482"
```

`make collect` işlem sürerken geçen süreyi ve diskte oluşan toplam PDF sayısını
aynı terminalde gösterir. Her PDF'nin ayrıntılı `[sıra/toplam]` ilerlemesi ise
`make run` komutunun çalıştığı sunucu terminalinde görünür.

İşlem sonunda profil, metrikler, çalışmalar, türler, yayın alanları, ham JSON,
PDF ve veritabanı kategorileri kısa durum satırlarıyla raporlanır:

- `[OK]`: kategori eksiksiz veya beklenen biçimde toplandı.
- `[KISMİ]`: kategorinin yalnızca bir bölümü alınabildi.
- `[EKSİK]`: kategori alınamadı veya uygun kaynak bulunamadı.
- `[ATLANDI]`: kimlik verilmedi ya da ilgili ayar kapalıydı.

Normal `Collect` endpoint'i JSON döndürmeye devam eder. Terminalde okunabilir
metin döndüren `CollectText` endpoint'ini `make collect` kullanır.

Kimlik türleri biçimlerinden otomatik belirlenir:

- `0000-0000-0000-000X`: ORCID
- 12 karakterlik harf, rakam, `_` veya `-`: Google Scholar ID
- `A-1009-2008` benzeri değer: Web of Science ResearcherID

Aynı istek doğrudan HTTP ile de gönderilebilir:

```shell
curl --silent --show-error \
  --request POST \
  --header "Content-Type: application/json" \
  --data '{"Identifiers":["o3ujRIMAAAAJ","0000-0001-8560-7482"],"UseTestIdentifiers":false}' \
  http://localhost:5000/Services/AcademicPerformance/Researcher/Collect
```

### Rastgele akademisyen özeti

```shell
make random
```

Bu istek yayınları tek tek döndürmez; akademisyen bilgilerini, çalışma
sayılarını, kategori sayılarını ve mevcut metrikleri özetler.

### Veritabanı

İlk çalıştırmada proje kökünde `academic.db` oluşturulur. Dosya DBeaver ile
SQLite bağlantısı olarak açılabilir.

Yerel veritabanını ve indirilen PDF dosyalarını tamamen silmek için:

```shell
make clean
```

Bu komut `academic.db`, `academic.db-shm`, `academic.db-wal` dosyalarını ve
`Storage/` klasörünün tamamını siler. Bu işlem geri alınamaz.
Veritabanı DBeaver'da açıksa önce bağlantıya sağ tıklayıp **Disconnect** seç;
`make clean`, dosyayı kullanan uygulamayı göstererek işlemi güvenli biçimde durdurur.

## Klasör yapısı

```text
akademi_projesi/
├── Modules/AcademicPerformance/
│   ├── Data/                   EF Core ve SQLite veritabanı işlemleri
│   ├── Endpoints/              Serenity HTTP endpoint'leri
│   ├── Integrations/
│   │   ├── OpenAlex/           OpenAlex istemcisi ve veri modelleri
│   │   ├── GoogleScholar/      SerpAPI istemcisi ve veri modelleri
│   │   └── WebOfScience/       Clarivate istemcisi ve veri modelleri
│   ├── Researchers/            Akademisyen modeli ve toplama iş akışı
│   ├── Works/                  Ortak çalışma modeli, tür ve PDF işlemleri
│   └── AcademicPerformanceModule.cs
├── Storage/Pdfs/               İndirilen PDF'ler; Git tarafından izlenmez
├── Requests/                   Hazır HTTP istekleri
├── Properties/                 Yerel çalıştırma ayarları
├── Program.cs                  Uygulamanın başlangıç noktası
├── appsettings.json            Veritabanı ayarları
├── academicsettings.json       Veri toplama, önbellek ve PDF ayarları
├── Makefile                    Sık kullanılan terminal komutları
└── AcademicCollectorDemo.csproj
```

## API ve dokümantasyon kaynakları

- [OpenAlex API](https://help.openalex.org/api/)
- [OpenAlex çalışma türleri](https://help.openalex.org/data/work-types/)
- [OpenAlex tam metin ve PDF erişimi](https://help.openalex.org/access/fulltext/)
- [SerpAPI Google Scholar Author API](https://serpapi.com/google-scholar-author-api)
- [SerpAPI Google Scholar Author Citation API](https://serpapi.com/google-scholar-author-citation)
- [SerpAPI planları ve kotaları](https://serpapi.com/pricing)
- [Clarivate Web of Science Researcher API](https://developer.clarivate.com/apis/wos-researcher)
- [YÖK Akademik](https://akademik.yok.gov.tr/AkademikArama/index.jsp)
- [Serenity Service Endpoints](https://docs.serenity.is/docs/services/service_endpoints)
