# Proje Notları

- Şema FluentMigrator ile yönetiliyor; yeni değişiklikler
  `Service/Data/Migrations/` altında ayrı migration olarak eklenmeli.
- Uygulama yalnız SQL Server kullanıyor; tablo ve indeksler FluentMigrator ile
  yönetiliyor.
- Prototip Serenity UI, BYS kimlik ve yetki servisine bağlanmalı.
- Akademisyenin kurum içi kaydı ORCID ile ilişkilendirilmeli.

## Servisleşme

- Yeni başvuru modülleri sürümlü `AcademicPerformance/V1` Serenity servislerini
  kullanmalı; EF varlıkları dış client sözleşmesine çıkarılmamalı.
- UI ve zamanlanmış görev aynı uygulama servisini çağırmalı.
- Yerleşik zamanlayıcı yalnız tek sunucu instance'ında etkin olmalı. Çoklu
  instance ortamında dağıtık kilitli merkezi görev altyapısına geçilmeli.
- T.C. kimlik numarası saklanmadığı için YÖKSİS otomatik yenilemeye dahil değil.

## Aktif akademik kaynak

Yayın ve akademisyen verileri resmî ORCID Public API üzerinden alınır. OpenAlex
entegrasyonu, sağlayıcıya özel tabloları ve kodu kaldırılmıştır. ORCID'in
sağladığı eser, istihdam, eğitim, fonlama ve hakemlik sayıları profil özetinde
kullanılır; atıf, h-index ve i10-index ORCID tarafından sağlanmaz.

## Veritabanı

Yerel geliştirmede varsayılan bağlantı SQL Server LocalDB'dir. Kurumsal ortamda
yalnız bağlantı cümlesi secret üzerinden değiştirilir.

```shell
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "SQL_SERVER_CONNECTION_STRING"
```

## YÖKSİS veri erişimi

Kurumsal `OzgecmisV2` SOAP istemcisi WSDL sözleşmesine göre eklendi. Kullanıcı
adı ve şifre `Yoksis:Username` ile `Yoksis:Password` User Secrets değerlerinden
okunur ve hem SOAP parametrelerinde hem HTTP Basic Authentication başlığında
gönderilir. Ana kategoriler ve yayın ayrıntıları ham XML korunarak alınır;
bütün başarılı kategori kayıtları `YoksisRecords` tablosunda JSON olarak
saklanır. Makale, bildiri, kitap ve patent ayrıntıları ayrıca ortak
`AcademicWorks` ve `PublicationSummaries` tablolarına yazılır. T.C. kimlik
numarası veritabanına yazılmaz. Production endpoint'i BYS yetkilendirmesi
arkasında olmalıdır.

## Diğer platformlar

ResearchGate ve Academia.edu için herkese açık, belgelenmiş ve uygun kullanımlı
bir geliştirici API'si bulunmadığından scraper yazılmayacaktır.
