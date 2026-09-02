# Web of Science Starter API

Kontrol: 2 Eylül 2026 · API plan fiyatları USD.

## Ne Sağlıyor?

ResearcherID ile yayın metadatası ve planın izin verdiği atıf sayıları alınır.
Starter API doğrudan araştırmacı h-index/i10-index alanı sağlamaz. Proje,
bütün yayınların atıfları mevcutsa h-index ve toplam atıfı hesaplar; i10 üretmez.

## Fiyat ve Limitler

| Plan | API ücreti | Günlük kota | Hız |
| --- | --- | ---: | --- |
| Free Trial | $0 | 50 | 1 istek/sn |
| Free Institutional Member | $0 | 5.000 | 5 istek/sn |
| Free Institutional Integration | $0 | 20.000 | 5 istek/sn |

- Bildirilen mevcut planımız **Free Institutional Member**; atıf sayılarını destekler. Trial desteklemez.
- API ücretsiz olsa da kurumun WoS aboneliği ayrıca gerekir; fiyat ve veri kapsamı sözleşmeye bağlıdır.
- Ayrı eşzamanlı istek sınırı açıklanmamış. 5 istek/sn, aynı anda 5 akademisyen toplama garantisi değildir.

## Projedeki Kullanım

`X-ApiKey` ile Starter v1 `/documents` endpoint'ine `AI=(ResearcherID)` sorgusu
yollanır. WOS ve WOK ayrı çağrılır; sonuçlar tekilleştirilir.

Sayfa başına en fazla 50 yayın alınır; boş sonuçta bile her veri tabanına en az
bir çağrı yapılır. **İki sonuç da 50'yi aşmıyorsa toplam 2 istek** harcanır.
Saklama ve yeniden gösterim hakları kurum sözleşmesinden teyit edilmelidir.

Kaynaklar: [Planlar ve limitler](https://developer.clarivate.com/apis/wos-starter) · [API şeması](https://developer.clarivate.com/apis/wos-starter/swagger) · [Lisans koşulları](https://clarivate.com/download/web-of-science-incites-apis/)
