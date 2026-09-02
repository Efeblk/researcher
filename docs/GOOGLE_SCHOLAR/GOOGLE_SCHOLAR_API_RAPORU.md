# Google Scholar API Seçenekleri

Kontrol: 2 Eylül 2026 · Fiyatlar USD, aylık ödeme, vergi hariç.

## Ne Kullanıyoruz?

Google Scholar'ın resmî API'si yok. Projede **SearchApi** üzerinden Scholar ID
ile profil, yayınlar, toplam/yıllık atıf, h-index ve i10-index alınır.

## SearchApi Fiyat ve Limitleri

| Plan | Ücret/ay | Arama/ay | Arama/saat sınırı |
| --- | --- | ---: | ---: |
| Developer | $40 | 10.000 | 2.000 |
| Production | $100 | 35.000 | 7.000 |
| BigData | $250 | 100.000 | 20.000 |
| Scale | $500 | 250.000 | 50.000 |

- Standart hız fiyatlarıdır; 100 ücretsiz deneme isteği aylık yenilenmez.
- Saatlik sınır paket kotasının %20'si. Ayrı saniyelik/eşzamanlı sınır açıklanmamış; saatlik kota paralellik hakkı değildir.
- Yalnız başarılı `200` yanıtları ücretli. Projede **3 yayın sayfası = 3 arama kredisi**; tek akademisyen birden fazla kredi harcayabilir.
- Paketler karşılaştırma içindir; satın alınmış planımız teyit edilmemiştir.

## Alternatifler — Başlangıç Paketleri

| Sağlayıcı | Fiyat ve kota | Hız / eşzamanlılık |
| --- | --- | --- |
| SerpApi Free | $0; 250 arama/ay | 50 arama/saat; eşzamanlı sınır açıklanmamış |
| SerpApi Starter | $25/ay; 1.000 arama/ay | 200 arama/saat; eşzamanlı sınır açıklanmamış |
| Apify Starter | $19/ay + aşım; $19 kullanım kredisi | 32 eşzamanlı Actor işi; HTTP istek sayısı değil |
| Semantic Scholar | Ücretsiz; ayrı günlük kota açıklanmamış | Anahtarlı başlangıç toplam 1 istek/sn; eşzamanlı sınır açıklanmamış |

Apify'de sonuç fiyatı seçilen Actor'a bağlıdır. Semantic Scholar ve
[OpenAlex](../OPENALEX/OPENALEX_API_RAPORU.md) kendi indekslerini kullanır;
Scholar verisinin yerine geçmez. Semantic Scholar doğrudan i10 sağlamaz.
[WoS](../WEB_OF_SCIENCE/WEB_OF_SCIENCE_API_RAPORU.md) de ayrı bir metrik kaynağıdır.

Kaynaklar: [Google açıklaması](https://scholar.google.com/intl/us/scholar/help.html) · [SearchApi endpoint](https://www.searchapi.io/docs/google-scholar-author) · [SearchApi fiyat/limit](https://www.searchapi.io/pricing) · [SerpApi](https://serpapi.com/pricing) · [Apify](https://apify.com/pricing) · [Semantic Scholar](https://www.semanticscholar.org/product/api)
