# Akademik Veri API'leri — Özet

Kontrol: 2 Eylül 2026 · Fiyatlar USD, vergi hariç.

## Karşılaştırma

| Sağlayıcı | Aldığımız veri | Fiyat | Kota | Hız sınırı |
| --- | --- | --- | --- | --- |
| [ORCID Public](ORCID/ORCID_API_RAPORU.md) | Açık profil ve yayınlar; atıf/h/i10 yok | Ücretsiz, kullanım koşullu | Anonim 25.000/gün/IP; kayıtlı 100.000/gün/Client ID | 12 istek/sn |
| [OpenAlex](OPENALEX/OPENALEX_API_RAPORU.md) | Yayınlar ve kendi atıf/h/i10 metrikleri | Ücretsiz günlük bütçe; ek kullanım ücretli | Anahtarsız $0,10/gün; anahtarla $1/gün | 100 istek/sn |
| [SearchApi / Scholar](GOOGLE_SCHOLAR/GOOGLE_SCHOLAR_API_RAPORU.md) | Scholar yayınları, atıf/h/i10 | Developer: $40/ay | 10.000 arama/ay | 2.000 arama/saat |
| [WoS Starter](WEB_OF_SCIENCE/WEB_OF_SCIENCE_API_RAPORU.md) | Yayın/atıf; h yerelde hesaplanır, i10 yok | API ücretsiz; kurum aboneliği ayrıca | Institutional Member: 5.000/gün | 5 istek/sn |
| [YÖKSİS](YOKSIS/YOKSIS_API_RAPORU.md) | Kurumsal özgeçmiş ve yayın beyanları | Teyit gerekli | Teyit gerekli | Teyit gerekli |

SearchApi paketi fiyat örneğidir; satın alınmış plan beyanı değildir.
WoS için bildirilen planımız Free Institutional Member'dır.
Diğer paketler, alternatif sağlayıcılar ve resmî kaynaklar bağlantılı raporlardadır.

## Aynı Anda Kaç İstek?

Bu beş servis için ayrı sayısal **eşzamanlı istek sınırı doğrulanmış değil**.
Tablodaki hızlar saniye/saat başına çağrı sayısıdır; paralel bağlantı sayısı
değildir. Açıklanmayan sınırlar sınırsız kabul edilmemelidir.

Bir akademisyen sorgusu sayfalama nedeniyle birden fazla dış çağrı harcar.
Mevcut kod tek toplama içinde sıralı çalışır; bütün kullanıcıları kapsayan
merkezi hız/kota sınırlayıcı henüz yoktur.

## Proje Kararları

- Sağlayıcı metrikleri birbirinin yerine kullanılmaz; OpenAlex karşılaştırma verisi ayrı tutulur.
- Tüm yayınlar saklanır; okulda yalnız akademisyenin onayladıkları gösterilir.
- Canlıya geçmeden kurumsal kullanım hakları, BYS yetkilendirmesi ve ortak kota kontrolü tamamlanmalıdır.
