# API Notes
https://info.orcid.org/ufaqs/what-are-the-api-limits/
https://github.com/ORCID/orcid-model/blob/master/src/main/resources/record_3.0/README.md
# Proje Notları

- Şema kesinleştiğinde `EnsureCreated` yerine Fluent Migrations kullanılmalı.
- Production entegrasyonunda SQLite yerine SQL Server kullanılmalı.
- Prototip Serenity UI, BYS kimlik ve yetki servisine bağlanmalı.
- Akademisyenin kurum içi kaydı ORCID ile ilişkilendirilmeli.

## Aktif akademik kaynak

Yayın ve akademisyen verileri resmî ORCID Public API üzerinden alınır. OpenAlex
entegrasyonu, sağlayıcıya özel tabloları ve kodu kaldırılmıştır. ORCID'in
sağladığı eser, istihdam, eğitim, fonlama ve hakemlik sayıları profil özetinde
kullanılır; atıf, h-index ve i10-index ORCID tarafından sağlanmaz.

## Veritabanı seçimi

Yerel geliştirmede varsayılan olarak SQLite ve `academic.db` dosyası kullanılır.
Üniversite entegrasyonunda sağlayıcı SQL Server olarak değiştirilecektir.

```shell
dotnet user-secrets set "Database:Provider" "SqlServer"
dotnet user-secrets set "ConnectionStrings:AcademicDatabase" "SQL_SERVER_CONNECTION_STRING"
```

## YÖKSİS veri erişimi

Kurumsal `OzgecmisV2` SOAP istemcisi WSDL sözleşmesine göre eklendi. Kullanıcı
adı ve şifre `Yoksis:Username` ile `Yoksis:Password` User Secrets değerlerinden
okunur. Ana kategoriler ve yayın ayrıntıları ham XML korunarak alınır; YÖKSİS
yanıtları veri sahipliği ve saklama kuralları kesinleşene kadar veritabanına
yazılmaz. Production endpoint'i BYS yetkilendirmesi arkasında olmalıdır.

## Diğer platformlar

ResearchGate ve Academia.edu için herkese açık, belgelenmiş ve uygun kullanımlı
bir geliştirici API'si bulunmadığından scraper yazılmayacaktır.
