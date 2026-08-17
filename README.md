# Akademik Performans Veri Toplayıcı

Bu proje, bir akademisyene ait kimlikleri kullanarak akademik verileri farklı
kaynaklardan toplar ve veritabanına kaydeder.

Şu anda desteklenen kimlikler:

- ORCID
- Google Scholar ID

İleride eklenmesi planlanan kimlikler:

- Web of Science ResearcherID
- Scopus Author ID
-yöksis
-researchgate
-academia


## Kullanılan veri kaynakları

- ORCID ile OpenAlex üzerinden akademisyen ve yayın bilgileri alınır.
- Google Scholar ID ile SerpAPI üzerinden profil ve yayın bilgileri alınır.
- Bu iki sorgu birbirinden bağımsızdır. Kimliklerden biri yoksa diğer sorgu
  çalışmaya devam eder.

SerpAPI, Google tarafından sağlanan resmî bir Google Scholar API değildir.
Google Scholar sayfalarını scrape ederek sonuçları JSON biçiminde sunar.

## Gereksinimler

- .NET 10 SDK
- Google Scholar sorguları için bir SerpAPI anahtarı

SerpAPI anahtarı proje klasöründe User Secrets ile tanımlanır:

```shell
dotnet user-secrets set "SerpApi:ApiKey" "GERCEK_API_ANAHTARI"
```

Anahtar kaynak kodda veya `appsettings.json` dosyasında tutulmaz.

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
  "ConnectionStrings": {
    "AcademicDatabase": "Data Source=academic.db"
  }
}
```

Program ilk çalıştığında proje klasöründe `academic.db` dosyasını ve gerekli
tabloları otomatik olarak oluşturur. Bu dosya Git deposuna eklenmez.

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

Kimlikler ayrı ayrı verilebilir:

```shell
dotnet run -- --orcid 0000-0003-2812-9917
dotnet run -- --scholar dYpPMQEAAAAJ
```

İki kimlik birlikte de verilebilir:

```shell
dotnet run -- --orcid 0000-0003-2812-9917 --scholar dYpPMQEAAAAJ
```

Program API verilerini topladıktan sonra seçilen veritabanına kaydeder. Aynı
akademisyen tekrar sorgulanırsa yeni bir akademisyen kaydı açmak yerine mevcut
kayıt güncellenir.

## Veritabanı komutları

Tablolardaki kayıt sayılarını görmek için:

```shell
dotnet run -- --db-info
```

Yerel SQLite veritabanındaki bütün verileri temizlemek için:

```shell
dotnet run -- --clear-db
```

`--clear-db`, mevcut SQLite dosyasını sıfırlar ve boş tabloları yeniden
oluşturur. Güvenlik nedeniyle SQL Server seçiliyken çalışmaz.

## Klasör yapısı

```text
Clients/         API istemcileri
Configuration/   Uygulama ve veritabanı ayarları
Data/            DbContext, repository ve veritabanı bakım işlemleri
Models/          Akademisyen ve API veri modelleri
Program.cs       Uygulamanın başlangıç noktası
```
