# OpenAlex API

Kontrol: 2 Eylül 2026 · Fiyatlar USD, vergi hariç.

## Ne Sağlıyor?

Yayınlar, toplam atıf, h-index ve i10-index sağlar. Crossref, DataCite, ORCID ve
depolar gibi kaynaklardan kendi indeksini oluşturur. Google Scholar'dan veri
aldığına dair resmî kanıt yoktur; metrikleri Scholar değeri değildir.

## Fiyat ve Limitler

| Erişim | Ücret | Günlük kullanım bütçesi |
| --- | --- | --- |
| Anahtarsız | Ücretsiz | $0,10 |
| Ücretsiz API anahtarı | Ücretsiz | $1 |
| Member | $5.000/yıl | $20 |
| Member+ | $10.000/yıl | $100 |
| Partner | $20.000/yıldan başlayan | $200+ |

- ID/DOI ile tek kayıt ücretsiz; 1.000 filtreleme $0,10, 1.000 kelime araması $1 bütçe tüketir.
- Yalnız filtrelemede ücretsiz anahtar **10.000 çağrı/gün** sağlar. Bütçe aşımı için ön ödemeli bakiye alınabilir.
- Kullandığımız endpoint'lerde **100 istek/sn**; ayrı eşzamanlı istek sınırı açıklanmamış. Bütçe/hız aşımında `429` döner.

## Projedeki Kullanım

ORCID ile yazar eşleşmesi aranır; birden fazla aday varsa en çok yayını olan,
eşitlikte en çok atıf alan seçilir. Sonuç ORCID kaydıyla birebir örtüşmeyebilir.

Veri `OpenAlexProfiles` ve `OpenAlexWorks` tablolarında ayrı tutulur; ortak yayın
listesine katılmaz. Çağrı hesabı: **1 yazar sorgusu + istenen eser sayfaları**
(varsa son boş sayfa dahil). Dört filtreleme çağrısı $0,0004 bütçe tüketir.

Kaynaklar: [Veri kaynakları](https://help.openalex.org/data/how-its-built/) · [ORCID eşleştirme](https://help.openalex.org/data/authors/orcid/) · [Ücretler](https://help.openalex.org/access/pricing/) · [Birim maliyetler](https://help.openalex.org/access/example-costs/) · [Limitler](https://help.openalex.org/api/authentication/)
