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
