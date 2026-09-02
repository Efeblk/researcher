# YÖKSİS Özgeçmiş V2

Kontrol: 2 Eylül 2026.

## Ne Sağlıyor?

Kurumsal kullanıcı/şifre ve T.C. kimlik numarasıyla özgeçmiş, görev, faaliyet ve
yayın beyanları alınır. SOAP 1.1 servisidir; Scholar benzeri standart
h-index/i10-index profili değildir.

## Fiyat ve Limitler

| Konu | Durum |
| --- | --- |
| Ücret | Kamuya açık resmî fiyat doğrulanamadı |
| Günlük/aylık kota | Doğrulanamadı |
| Saniyelik hız | Doğrulanamadı |
| Eşzamanlı istek | Doğrulanamadı |

Kurum/YÖK'den yazılı teyit gerekir. Açıklanmamış olması ücretsiz veya sınırsız
olduğu anlamına gelmez.

## Projedeki Kullanım

- `POST /Services/AcademicPerformance/V1/Yoksis/Collect`: 21 kategori ve makale/bildiri/kitap/proje/patent detaylarını toplar.
- **21 kategori + 30 detay = 51 SOAP çağrısı**. Çağrılar tek toplama içinde sıralıdır.
- Kayıtlar `YoksisRecords`'a, yayınlar `AcademicWorks` ve `PublicationSummaries`'e işlenir.
- Ham kayıt/XML varsayılan yanıtta yoktur. T.C. kimlik numarası saklanmaz veya loglanmaz; kurumsal yetki kontrolü zorunludur.

Kaynak: [Resmî WSDL](https://servisler.yok.gov.tr/ws/OzgecmisV2?wsdl)
(son incelemede canlı okunamadı).
