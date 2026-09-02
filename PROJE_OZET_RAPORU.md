# Proje Özet Raporu

3 Eylül 2026 · Fiyatlar USD, vergi hariç.

## Mevcut Bileşenler

- [x] **Raw data:** Sağlayıcıların ham yanıt ve kayıtları saklanıyor.
- [x] **Summary data:** Sade, tekilleştirilmiş yayınlar `PublicationSummaries` tablosunda.
- [x] **Service:** Client bağımsız, Serenity uyumlu V1 API.
- [x] **YÖKSİS:** SOAP entegrasyonu ve 21 kategori için veri toplama.
- [x] **Web client:** Yayınları inceleme ve okulda gösterilecekleri seçme.
- [x] **Veritabanı:** SQL Server ve FluentMigrator migration'ları.
- [x] **Karşılaştırma:** OpenAlex verileri diğer kaynaklardan ayrı tutuluyor.

## API Planları, Fiyat ve Hız

| API | Plan / erişim | Fiyat | Hız ve kota |
| --- | --- | --- | --- |
| [ORCID](https://info.orcid.org/ufaqs/what-are-the-api-limits/) | Public v3; anonim veya token | Ücretsiz, kullanım koşullu | 12 istek/sn; anonim 25.000/gün/IP, kayıtlı 100.000/gün/Client ID |
| [OpenAlex](https://help.openalex.org/access/example-costs/) | Anahtarsız / ücretsiz anahtar | Ücretsiz bütçe; ek kullanım ücretli | [100 istek/sn](https://help.openalex.org/api/authentication/); sırasıyla $0,10 / $1 günlük bütçe |
| [SearchApi — Scholar](https://www.searchapi.io/pricing) | Hesap planı teyitsiz | Pakete bağlı | Pakete bağlı |
| [Web of Science](https://developer.clarivate.com/apis/wos-starter) | Free Institutional Member — bildirilen planımız | API ücretsiz; kurum aboneliği ayrıca | 5 istek/sn; 5.000 istek/gün |
| [YÖKSİS](docs/YOKSIS/YOKSIS_API_RAPORU.md) | Kurumsal Özgeçmiş V2 erişimi | Kurum/YÖK teyidi gerekli | Kamuya açık limit doğrulanamadı |

SearchApi fiyat örneği: **Developer $40/ay → 10.000 arama/ay, 2.000 arama/saat**.
Bu, satın alınmış planımızın doğrulandığı anlamına gelmez.

Hız sınırı, eşzamanlı bağlantı sayısı değildir; bir akademisyen sorgusu birden
fazla dış çağrı harcar. ORCID token ve OpenAlex anahtar durumu ortam ayarlarına bağlıdır.

**Henüz yok:** Cron/background işi, production BYS yetkilendirmesi ve merkezi hız/kota sınırlayıcı.

[Ayrıntılı API raporları ve kaynaklar](docs/API_OZET_RAPORU.md)
