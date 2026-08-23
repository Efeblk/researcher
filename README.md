# Akademik Performans Veri Toplayıcı

Resmî ORCID Public API üzerinden akademisyen profili ve eser verilerini toplayan
.NET 10 / Serenity servisidir. OpenAlex, Google Scholar ve Web of Science
entegrasyonları şimdilik aktif değildir.

## Gereksinimler

- .NET 10 SDK
- Node.js 18 veya üzeri
- Git
- İstek komutları için isteğe bağlı olarak `make` ve `curl`

## Teknoloji yığını

| Teknoloji | Kullanım amacı |
| --- | --- |
| .NET 10 / ASP.NET Core | HTTP sunucusu ve uygulama altyapısı |
| Serenity LTS 10.3.5 | BYS ile uyumlu endpoint, Row ve grid yapısı |
| Entity Framework Core 10 | Veritabanı işlemleri |
| SQLite / SQL Server | Yerel geliştirme ve production veri katmanı |
| ORCID Public API 3.0 | Herkese açık profil, faaliyet ve eser verileri |

## Toplanan veriler

- ORCID adı, biyografisi, ülkesi, anahtar kelimeleri ve dış kimlikleri
- Eğitim, istihdam, fonlama ve hakemlik faaliyetleri
- ORCID'in grupladığı bütün herkese açık eserler
- Başlık, DOI, tarih, tür, yayın yeri, yazar ve yayın bağlantısı
- ORCID'in döndürdüğü özgün profil, faaliyet ve tam eser JSON yanıtları
- Site için sadeleştirilmiş `PublicationSummaries` kayıtları
- Akademisyenin okulda gösterilmesine izin verdiği yayınlar için ayrı onay kayıtları

Yayın türleri makale, kitap, kitap bölümü, bildiri, tez, veri seti ve benzeri
ortak kategorilere dönüştürülür. ORCID atıf sayısı, h-index, i10-index veya
doğrulanmış PDF bilgisi sağlamaz; bu alanlar uydurma sıfırlarla doldurulmaz.

## Çalıştırma

```shell
dotnet restore
dotnet run
```

Sunucu varsayılan olarak `http://localhost:5000` adresinde çalışır. Serenity
tabanlı prototip arayüz:

```text
http://localhost:5000/AcademicPerformance
```

Arayüzde ORCID girildiğinde resmî ORCID verileri toplanır ve benzersiz eserler
salt-okunur grid içinde gösterilir.

## Terminal komutları

```shell
make build
make run
make health
make collect ID="0000-0001-8560-7482"
make random
```

PowerShell üzerinden aynı işlem:

```powershell
.\collect.ps1 -Id "0000-0001-8560-7482"
```

PowerShell üzerinden yerel veritabanı ve `Storage` klasörünü temizlemek için:

```powershell
.\collect.ps1 clean
```

Doğrudan HTTP isteği:

```shell
curl --request POST \
  --header "Content-Type: application/json" \
  --data '{"Identifiers":["0000-0001-8560-7482"],"UseTestIdentifiers":false}' \
  http://localhost:5000/Services/AcademicPerformance/Researcher/Collect
```

Kimlik `0000-0000-0000-000X` biçiminde geçerli bir ORCID olmalıdır.

## Yapılandırma ve önbellek

`academicsettings.json` içindeki `ProviderCache:MaxAgeHours`, eksiksiz ORCID
verisinin yeniden sorgulanmadan kullanılacağı süreyi belirler. Varsayılan değer
24 saattir. Public API şu anda herkese açık kayıtlarda anahtarsız çalışır. Kurumsal
bir erişim belirteci kullanılacaksa repoya yazmadan ayarlayın:

```powershell
dotnet user-secrets set "Orcid:AccessToken" "<TOKEN>"
```

Yerel SQLite veritabanı proje kökündeki `academic.db` dosyasıdır. Veritabanını
ve çalışma çıktısını silen `make clean` veya `.\collect.ps1 clean` komutu geri
alınamaz. Temizlemeden önce çalışan sunucuyu kapatın.

## Veri modeli

| Tablo | Amaç |
| --- | --- |
| `Researchers` | Akademisyen ve ORCID |
| `ResearcherMetrics` | ORCID kayıtlı eser sayısı; desteklenmeyen metrikler boş |
| `OrcidProfiles` | Profil, faaliyet özetleri ve ham ORCID kayıt JSON'u |
| `OrcidWorks` | Tekilleştirilmiş ORCID eserleri ve tam eser JSON'u |
| `AcademicWorks` | Sağlayıcıdan bağımsız normalize edilmiş yayınlar |
| `PublicationSummaries` | Arayüz ve raporlama için sade yayın tablosu |
| `PublicationDisplayApprovals` | Akademisyenin okulda gösterilmesini onayladığı yayınlar |

Aynı akademisyenin yayınları önce DOI, DOI yoksa normalize edilmiş başlık ve
yayın yılıyla tekilleştirilir. Mevcut eski yerel veritabanlarında kullanılmayan
sağlayıcılara ait tablolar korunabilir; uygulama bu tabloları artık okumaz veya
güncellemez.

Toplanan bütün yayınlar `PublicationSummaries` içinde tutulur. Arayüzde
“Okulda Göster” seçeneği işaretlenip kaydedildiğinde yalnızca onaylanan yayınlar
`PublicationDisplayApprovals` tablosuna eklenir. Okul sitesinin kullanacağı sade
liste `PublicationDisplayApproval/ListApproved` servisinden alınabilir.

## Klasör yapısı

```text
Modules/AcademicPerformance/
├── Data/                   EF Core ve veritabanı başlatma
├── Endpoints/              Serenity servis endpoint'leri
├── Integrations/Orcid/     Aktif resmî ORCID istemcisi ve modelleri
├── Integrations/OpenAlex/  Askıdaki tarihsel entegrasyon
├── Researchers/            ORCID ayrıştırma ve toplama iş akışı
├── UI/                     Prototip sayfa, Row ve TypeScript grid
└── Works/                  Kategoriler ve yayın özetleri
Requests/                   Hazır HTTP istekleri
```

## Dokümantasyon

- [ORCID Public API](https://info.orcid.org/what-is-orcid/services/public-api/)
- [ORCID kayıtlarını okuma](https://info.orcid.org/documentation/api-tutorials/api-tutorial-read-data-on-a-record/)
- [Serenity Service Endpoints](https://docs.serenity.is/docs/services/service_endpoints)
