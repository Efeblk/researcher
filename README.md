# Akademik Performans Modülü

Resmî ORCID Public API üzerinden akademisyen profili ve yayınlarını toplayan,
Serenity bileşenleriyle hazırlanmış .NET 10 prototipidir. Uygulama bütün
yayınları yerel veritabanında saklar; akademisyen okul sitesinde gösterilmesine
izin verdiği yayınları ayrıca seçer.

## Mevcut iş akışı

1. Akademisyen ORCID numarasını girer veya **Yayınlarımı Getir** düğmesini kullanır.
2. Herkese açık profil, faaliyet ve eser kayıtları ORCID'den alınır.
3. Eserler DOI'ye; DOI yoksa normalize başlık ve yıla göre tekilleştirilir.
4. Sade yayın kayıtları Serenity grid'inde gösterilir.
5. **Okulda Göster** seçimleri `PublicationDisplayApprovals` tablosuna kaydedilir.

PDF indirilmez; ORCID'in sağladığı DOI ve yayın bağlantıları saklanır. OpenAlex
entegrasyonu askıdadır ve yalnızca eski veri modeliyle uyumluluk için tutulur.
Google Scholar ve Web of Science entegrasyonu yoktur.

## Hedef servisler ve entegrasyon durumu

| Kimlik veya sistem | Hedef servis | Erişim beklentisi | Proje durumu |
| --- | --- | --- | --- |
| ORCID | Resmî ORCID Public API 3.0 | Herkese açık kayıtlar; isteğe bağlı erişim belirteci | **Aktif**; profil, faaliyet ve eserler alınıyor |
| OpenAlex | OpenAlex API | Genel API erişimi | **Askıda**; kod ve eski tablolar uyumluluk için korunuyor |
| Google Scholar ID | Belirlenecek uygun sağlayıcı | Google'ın resmî API'si olmadığı için sağlayıcı ve kota kararı gerekli | **Planlanan**; SerpAPI kaldırıldı |
| Scopus Author ID | Resmî Elsevier Scopus API | Kurumsal abonelik ve API yetkisi gerekebilir | **Hedef**; entegrasyon henüz yok |
| Web of Science ResearcherID | Resmî Clarivate Researcher API | Kurumsal API lisansı gerekli | **Hedef**; entegrasyon şimdilik kaldırıldı |
| YÖKSİS | YÖK kurumsal web servisi | Üniversitenin servis sözleşmesi, test ortamı ve kimlik bilgileri gerekli | **Beklemede**; belgeler gelmeden istemci yazılmayacak |
| BYS kullanıcı kaydı | Üniversitenin kimlik ve yetki servisleri | Oturum açmış akademisyen ve kurum içi personel ID'si | **Production için gerekli** |
| ResearchGate / Academia.edu | Resmî ve izinli API bulunursa değerlendirilecek | Scraping kullanılmayacak | **Kapsam dışı** |

Bu tablo ürün hedeflerini gösterir; yalnızca **Aktif** durumundaki ORCID çalışma
zamanında kayıtlı ve çağrılan akademik veri sağlayıcısıdır. Yeni bir servis,
erişim sözleşmesi ve veri sahipliği netleşmeden mevcut toplama akışına eklenmez.

## Gereksinimler ve çalıştırma

- .NET 10 SDK
- Node.js 18 veya üzeri

```powershell
dotnet restore
dotnet run
```

Build sırasında `npm install` ve TypeScript derlemesi gerektiğinde otomatik
çalışır. Arayüz `http://localhost:5000/AcademicPerformance`, sağlık yanıtı ise
`http://localhost:5000/` adresindedir.

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
```

Hazır IDE istekleri `Requests/AcademicPerformance.http` dosyasındadır. Temizleme
komutu yerel SQLite dosyalarını ve `Storage/` klasörünü siler; önce sunucuyu
kapatın:

```powershell
.\collect.ps1 clean
```

## Proje yapısı

```text
Modules/AcademicPerformance/
├── Data/                   EF Core bağlamı ve şema hazırlığı
├── Endpoints/              Serenity HTTP servisleri
├── Integrations/Orcid/     Aktif resmî ORCID istemcisi
├── Integrations/OpenAlex/  Askıdaki tarihsel uyumluluk kodu
├── Researchers/            Kimlik ayrıştırma ve toplama akışı
├── UI/                     Razor sayfası, Row/Columns ve TypeScript grid
└── Works/                  Normalizasyon, özet ve gösterim onayları
Requests/                   Manuel HTTP istekleri
Views/                      Ortak Serenity yerleşimi
wwwroot/Content/            Uygulama stilleri
```

`wwwroot/esm/`, `bin/`, `obj/`, `node_modules/`, `Storage/` ve `academic.db*`
yeniden üretilebilir veya çalışma zamanı çıktılarıdır; Git'e eklenmez.

## Veri modeli

| Tablo | İçerik |
| --- | --- |
| `Researchers` | Akademisyen ve ORCID eşleşmesi |
| `OrcidProfiles` / `OrcidWorks` | ORCID profil, faaliyet, eser ve ham JSON verisi |
| `AcademicWorks` | Sağlayıcıdan bağımsız normalize yayınlar |
| `PublicationSummaries` | Arayüz ve raporlama için sade yayın listesi |
| `PublicationDisplayApprovals` | Okulda gösterilmesine izin verilen yayınlar |
| `ResearcherMetrics` | ORCID eser sayısı ve kaynak bilgisi |

ORCID atıf sayısı, h-index ve i10-index sağlamaz; bu alanlar boş bırakılır.
Onaylı yayınlar `PublicationDisplayApproval/ListApproved` servisi üzerinden
alınabilir.

## Yapılandırma ve production notları

Varsayılan SQLite bağlantısı `appsettings.json`, ORCID adresi ve önbellek süresi
`academicsettings.json` içindedir. Gizli değerleri repoya yazmayın; gerekiyorsa
.NET user secrets veya ortam değişkeni kullanın:

```powershell
dotnet user-secrets set "Orcid:AccessToken" "<TOKEN>"
```

Mevcut UI bir entegrasyon prototipidir. **Yayınlarımı Getir** için sağlayıcı
kimliği tarayıcı `localStorage` alanında hatırlanır. BYS entegrasyonunda ORCID,
oturum açmış kullanıcı kaydından sunucu tarafında alınmalı; prototip izin
servisi gerçek kimlik/yetki servisiyle değiştirilmelidir. Yayın onay endpoint'leri
de production'a geçmeden önce kullanıcı sahipliği kontrolü uygulamalıdır.

SQL Server'a geçişte `Database:Provider=SqlServer` ve
`ConnectionStrings:AcademicDatabase` değerlerini güvenli yapılandırmadan verin.
Kalıcı şema yönetimi için `EnsureCreated` yaklaşımı yerine migration kullanılması
önerilir.

## Doğrulama

Projede henüz otomatik test projesi yoktur. Değişikliklerden sonra en az
`dotnet build` çalıştırın ve etkilenen akışı arayüzden veya `.http` dosyasından
kontrol edin. Yeni iş kuralları için dış API'leri taklit eden birim testleri
eklenmelidir.
