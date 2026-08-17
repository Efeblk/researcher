# Proje Notları

## Google Scholar veri erişimi

Google Scholar, OpenAlex gibi resmi ve doğrudan kullanılabilen bir JSON API
sunmamaktadır. Google Scholar verilerine erişmek için iki seçenek bulunmaktadır.

### 1. SerpAPI kullanmak

- Google Scholar profilini ScholarID ile sorgulayabilir.
- Profil, yayın ve atıf bilgilerini JSON biçiminde döndürür.
- Google tarafından sunulan resmî bir Google Scholar API değildir.
- Google Scholar web sayfalarını scrape ederek verileri toplar ve
  yapılandırılmış JSON biçimine dönüştürür.
- API anahtarı gerektirir.
- Kullanım miktarına göre ücret veya sorgu sınırı olabilir.
- Üniversite sistemi için doğrudan sayfa okumaya göre daha düzenli ve güvenilirdir.

Dokümantasyon: https://serpapi.com/google-scholar-api

### 2. Google Scholar sayfasını doğrudan okumak

- Ayrı bir API hizmeti ve API anahtarı gerektirmez.
- CAPTCHA veya geçici erişim engelleriyle karşılaşabilir.
- Google Scholar sayfasının HTML yapısı değiştiğinde kod bozulabilir.
- Uzun süre çalışan bir üniversite entegrasyonu için güvenilir değildir.

Google Scholar profil bilgisi:
https://scholar.google.com/intl/el/scholar/citations.html

## Geçici karar

Google Scholar entegrasyonunda şimdilik **SerpAPI kullanılacaktır**.

ScholarID ile doğrudan akademisyen profili sorgulamak için kullanılan endpoint:

```text
https://serpapi.com/search?engine=google_scholar_author&author_id=SCHOLAR_ID
```

API anahtarı kaynak koda yazılmayacaktır. Daha sonra ortam değişkeni veya güvenli
bir yapılandırma yöntemi üzerinden uygulamaya verilecektir.

Yerel geliştirmede .NET User Secrets kullanılmaktadır. SerpAPI anahtarı proje
klasöründe şu komutla tanımlanır:

```shell
dotnet user-secrets set "SerpApi:ApiKey" "GERCEK_API_ANAHTARI"
```

Anahtar proje dosyalarının içinde tutulmaz ve Git deposuna eklenmez.

## Veritabanı seçimi

Yerel geliştirmede varsayılan olarak SQLite ve `academic.db` dosyası kullanılır.
Üniversite entegrasyonunda sağlayıcı SQL Server olarak değiştirilecektir.

SQL Server bağlantı cümlesi kaynak koda yazılmaz. Sağlayıcı ve bağlantı cümlesi
User Secrets içine şu anahtarlarla kaydedilir:

```shell
dotnet user-secrets set "Database:Provider" "SqlServer"
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "SQL_SERVER_CONNECTION_STRING"
```

Bu değerler `appsettings.json` içindeki varsayılan SQLite ayarlarının üzerine
yazılır.

Şema geliştirme aşamasında olduğu için şimdilik migration kullanılmamaktadır.
Tablolar Entity Framework Core `EnsureCreated` yöntemiyle oluşturulur. Veritabanı
şeması kesinleştiğinde SQL Server migration'ları yeniden üretilecektir.

## Scopus veri erişimi

Scopus verileri için Elsevier'ın resmî Scopus Author Retrieval API'si
kullanılacaktır.

Scopus Author ID ile profil sorgulanan endpoint:

```text
https://api.elsevier.com/content/author/author_id/SCOPUS_AUTHOR_ID
```

API anahtarı isteğin `X-ELS-APIKey` başlığında gönderilir ve kaynak kodda
tutulmaz. Yerel geliştirmede User Secrets kullanılır:

```shell
dotnet user-secrets set "Elsevier:ApiKey" "GERCEK_ELSEVIER_API_ANAHTARI"
```

İlk aşamada ad, kurum, yayın sayısı, atıf sayıları ve varsa H-index alınır.
Scopus yayın listesi ayrı bir resmî Scopus Search API çağrısıyla daha sonra
eklenecektir. Tam veri erişimi üniversitenin Scopus aboneliğine bağlı olabilir.

Resmî dokümantasyon:
https://dev.elsevier.com/documentation/AuthorRetrievalAPI.wadl

## Web of Science veri erişimi

Web of Science ResearcherID verileri için Clarivate'ın resmî Web of Science
Researcher API'si kullanılacaktır.

ResearcherID ile profil sorgulanan endpoint:

```text
https://api.clarivate.com/apis/wos-researcher/researchers/RESEARCHER_ID
```

API anahtarı isteğin `X-ApiKey` başlığında gönderilir. Yerel geliştirmede User
Secrets kullanılır:

```shell
dotnet user-secrets set "Clarivate:ApiKey" "GERCEK_CLARIVATE_API_ANAHTARI"
```

İlk aşamada ad, kurum, profil sahipliği, yayın sayısı, atıf metrikleri ve H-index
alınır. Yayın listesi `/researchers/{rid}/documents` endpoint'iyle daha sonra
eklenebilir.

Bu API, normal Web of Science aboneliğine ek olarak ücretli API lisansı
gerektirir. Üniversiteden API erişimi ve anahtar istenmelidir.

Bu nedenle Web of Science entegrasyonu şimdilik atlanmıştır. Üniversite resmî
API lisansı sağlarsa mevcut kod yeniden etkinleştirilebilir.

Resmî dokümantasyon:
https://developer.clarivate.com/apis/wos-researcher

## YÖKSİS veri erişimi

YÖK'ün resmî kaynakları, Akademik Özgeçmiş sisteminin YÖKSİS ile entegre
olduğunu ve üniversite bilgi sistemlerinin kullanabildiği web servislerinin
bulunduğunu belirtiyor. Buna karşılık akademisyen verilerini dışarıdan
sorgulamak için herkese açık bir endpoint, geliştirici dokümanı veya API anahtarı
başvuru yöntemi yayımlanmamıştır.

YÖK Akademik'in herkese açık arama sayfası bir web arayüzüdür. Projede yalnızca
resmî API'lerle ilerleme kararı alındığı için bu sayfa scrape edilmeyecektir.

YÖKSİS istemcisini doğru biçimde yazabilmek için üniversiteden şunlar
istenmelidir:

- Servis adresi ile WSDL veya OpenAPI dokümanı
- Yetkilendirme yöntemi ve test ortamı bilgileri
- Test kullanıcı bilgisi, sertifika veya API anahtarı
- Akademisyeni sorgularken kullanılacak kimlik türü
- Dönen alanların veri sözleşmesi ve kullanım izinleri

Bu bilgiler gelene kadar YÖKSİS entegrasyonu beklemeye alınmıştır; tahminî bir
endpoint veya veri modeli oluşturulmayacaktır.

Resmî kaynaklar:

- https://akademik.yok.gov.tr/AkademikArama/index.jsp
- https://eski.yok.gov.tr/Documents/Kurumsal/strateji_dairesi/faaliyet_raporlari/2024-idare-faaliyet-raporu.pdf

## ResearchGate veri erişimi

ResearchGate'in akademisyen profillerini veya yayınlarını sorgulamak için
herkese açık, belgelenmiş resmî bir API'si bulunmamaktadır.

Ayrıca ResearchGate kullanım şartlarının 4.2 bölümünde yazılım, script, robot,
crawler veya benzeri otomatik yöntemlerle içerik, veri ve profil erişimi,
scraping ya da kopyalama yasaklanmıştır. Bu nedenle resmî API kullanma
kararımızın yanında kullanım şartları açısından da scraper yazılmayacaktır.

ResearchGate entegrasyonu şimdilik atlanmıştır. İleride ResearchGate resmî bir
API yayımlarsa yeniden değerlendirilebilir.

Resmî kaynak:
https://www.researchgate.net/terms-of-service

## Academia.edu veri erişimi

Academia.edu'nun akademisyen profillerini veya yayınlarını sorgulamak için
herkese açık, belgelenmiş resmî bir geliştirici API'si bulunmamaktadır.

Academia.edu kullanım şartlarının "General Prohibitions" bölümünde profillerin
ve diğer kişilere ait bilgilerin scraping yoluyla kopyalanması yasaklanmıştır.
Aynı bölüm bot, crawler ve benzeri otomatik yöntemlerle erişimi ve Academia.edu
tarafından açıkça sunulan arayüzler dışından servise erişmeyi de yasaklıyor.

Bu nedenle Academia.edu için istemci veya scraper yazılmayacaktır. Platform
ileride resmî ve izinli bir API yayımlarsa entegrasyon yeniden değerlendirilebilir.

Resmî kaynak:
https://www.academia.edu/terms
