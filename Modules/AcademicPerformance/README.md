# Academic Performance Module

Start here: [Codebase guide](../../docs/CODEBASE_GUIDE.md) — folders, request flow, and where to make changes.

Bu modül üç ana çalışma alanına ayrılır:

- `Service/`: Client'tan bağımsız API, iş akışları, veri erişimi ve sağlayıcı
  entegrasyonları.
- `WebClient/`: Mevcut Serenity/Razor arayüzü ve yalnız bu arayüzün kullandığı
  grid adapter'ları.
- `Background/`: İleride aynı uygulama servislerini çağıracak zamanlanmış işler.

## Bağımlılık yönü

```text
WebClient veya Api/V1/Endpoints
            ↓
        Application
            ↓
Researchers / Works / Integrations
            ↓
            Data
```

Yeni bir dış client yalnız `Service/Api/V1/Contracts` sözleşmelerine ve
`Service/Api/V1/Endpoints` adreslerine bağlanmalıdır. EF entity'lerini,
sağlayıcı DTO'larını veya WebClient endpoint'lerini dış sözleşme olarak
kullanmayın.

Yeni sağlayıcı kodunu `Service/Integrations/<Provider>/` altında tutun. Ortak
akademisyen modellerini `Researchers/Models`, yayın modellerini `Works/Models`,
iş akışlarını ise ilgili `Collection` veya `Processing` klasörüne ekleyin.
Veritabanı değişiklikleri yalnız `Data/Migrations/Core` ya da `Providers`
altında yeni bir FluentMigrator dosyasıyla yapılmalıdır.
