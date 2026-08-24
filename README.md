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

ORCID ve Web of Science ortak yayın toplama akışında çağrılır. YÖKSİS kişisel
veri içerdiği için UI içinden ayrı bir endpoint üzerinden çalışır. T.C. kimlik
numarası tarayıcıda hatırlanmaz ve istek tamamlanınca formdan temizlenir. Yeni
bir servis, erişim sözleşmesi ve veri sahipliği netleşmeden mevcut toplama
akışına eklenmez. T.C. kimlik numarası veritabanına yazılmaz; YÖKSİS'in döndürdüğü
Araştırmacı ID akademisyen eşleştirmesinde kullanılır.

## Gereksinimler ve çalıştırma

- .NET 10 SDK
- Node.js 18 veya üzeri

```powershell
dotnet restore
dotnet run
```

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
komutu yerel SQLite dosyalarını ve `Storage/` klasörünü siler; önce sunucuyu
kapatın:

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

Yanıt; her kategori için kayıt sayısını, düz alanları ve varsayılan olarak
YÖKSİS'in döndürdüğü ham SOAP XML'ini içerir. UI, gereksiz büyük yanıtı önlemek
için kayıtları ve ham XML'i yanıta ekletmez; yayınlar yine veritabanına yazılır.
`UpdatedAfter` alanı verilirse WSDL'deki isteğe bağlı `P_TARIH` alanına gönderilir.
T.C. kimlik numarasını `.http` dosyasına kaydedip Git'e göndermeyin.

## Proje yapısı

```text
Modules/AcademicPerformance/
├── Data/                   EF Core bağlamı ve şema hazırlığı
├── Endpoints/              Serenity HTTP servisleri
├── Integrations/Orcid/     Resmî ORCID istemcisi
├── Integrations/WebOfScience/ Clarivate Starter API v1 istemcisi
├── Integrations/Yoksis/    OzgecmisV2 SOAP istemcisi ve toplama akışı
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
| `Researchers` | Akademisyen, ORCID, Web of Science ResearcherID ve YÖKSİS Araştırmacı ID eşleşmesi |
| `OrcidProfiles` / `OrcidWorks` | ORCID profil, faaliyet, eser ve ham JSON verisi |
| `WebOfScienceProfiles` | Starter API sorgu özeti ve ham yayın sayfası yanıtları |
| `WebOfScienceWorks` | Web of Science yayınları ve varsa atıf sayıları |
| `AcademicWorks` | ORCID, Web of Science ve YÖKSİS'ten gelen normalize yayınlar |
| `PublicationSummaries` | Arayüz ve raporlama için sade yayın listesi |
| `PublicationDisplayApprovals` | Okulda gösterilmesine izin verilen yayınlar |

YÖKSİS makale, bildiri, kitap ve patent ayrıntıları `AcademicWorks` tablosuna;
grid'de kullanılacak tekilleştirilmiş halleri `PublicationSummaries` tablosuna
yazılır. Diğer YÖKSİS kategorileri yalnızca servis yanıtında döner. T.C. kimlik
numarası saklanmaz.

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

Varsayılan SQLite bağlantısı `appsettings.json`; sağlayıcı adresleri ve önbellek
süresi `academicsettings.json` içindedir. Gizli değerleri repoya yazmayın. Web
of Science API anahtarını User Secrets'a ekleyin:

```powershell
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

Mevcut UI bir entegrasyon prototipidir. **Yayınlarımı Getir** için sağlayıcı
kimliği tarayıcı `localStorage` alanında hatırlanır. BYS entegrasyonunda ORCID,
oturum açmış kullanıcı kaydından sunucu tarafında alınmalı; prototip izin
servisi gerçek kimlik/yetki servisiyle değiştirilmelidir. Yayın onay endpoint'leri
de production'a geçmeden önce kullanıcı sahipliği kontrolü uygulamalıdır.

SQL Server'a geçişte `Database:Provider=SqlServer` ve
`ConnectionStrings:AcademicDatabase` değerlerini güvenli yapılandırmadan verin.
Kalıcı şema yönetimi için `EnsureCreated` yaklaşımı yerine migration kullanılması
önerilir.

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
