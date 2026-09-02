# ORCID API

Kontrol: 2 Eylül 2026 · Fiyatlar USD, vergi hariç.

## Ne Sağlıyor?

ORCID ID ile herkese açık profil, kurum, eğitim ve yayınlar alınır.
Atıf sayısı, h-index ve i10-index sağlamaz. Projede Public API v3 kullanılır.

## Fiyat ve Limitler

| Erişim | Ücret | Günlük kota | Hız |
| --- | --- | --- | --- |
| Anonim | Ücretsiz | 25.000 okuma/IP | 12 istek/sn |
| Kayıtlı Public | Ücretsiz | 100.000 okuma/Client ID | 12 istek/sn |
| Member — Basic, kamu/kâr amacı gütmeyen kurum | $4.775/yıl | Günlük kota yok | 24 istek/sn |

- Ayrı eşzamanlı istek sınırı açıklanmamış. Tüm türlerde 40 burst/sn kısa süreli kuyruk sınırıdır; paralel bağlantı sayısı değildir.
- Public API bireylerin ticari olmayan kullanımı içindir; BYS için uygunluk/kurum üyeliği teyit edilmeli.
- Diğer Member paketleri ve konsorsiyum ücretleri değişir; tablodaki fiyat Marmara'ya özel teklif değildir.

## Projedeki Kullanım

`GET /{ORCID}/record` ve en fazla 100 eserlik toplu detay çağrıları yapılır.
Önbelleksiz örnek: **250 eser = 1 profil + 3 detay çağrısı = 4 istek**.

Kaynaklar: [API rehberi](https://github.com/ORCID/orcid-model/blob/master/src/main/resources/record_3.0/README.md) · [Limitler](https://info.orcid.org/ufaqs/what-are-the-api-limits/) · [Ücretler](https://info.orcid.org/membership/) · [Kullanım koşulları](https://info.orcid.org/public-client-terms-of-service/)
