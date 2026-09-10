# Proje Özet Raporu

**Proje durumu:** 10 Eylül 2026 · Çalışan entegrasyon prototipi; production'a hazır değil.

## Mevcut Bileşenler

- [x] **Raw data:** Sağlayıcıların ham yanıt ve kayıtları SQL Server'da saklanıyor.
- [x] **Summary data:** Tekilleştirilmiş yayınlar `PublicationSummaries` tablosunda.
- [x] **Service / V1 API:** Client bağımsız, Serenity uyumlu toplama ve yayın API'si.
- [x] **SQL Server:** FluentMigrator migration'ları ve kalıcı veri katmanı.
- [x] **Sağlayıcı metrikleri:** WoS, OpenAlex ve Scholar metrikleri `Researchers` tablosunda `PersonelID` bazında sorgulanabilir.
- [x] **Web client:** Profil, yayın listesi ve okulda gösterilecek yayın seçimi.
- [x] **ORCID:** Profil ve eser toplama.
- [x] **Google Scholar / SearchApi:** Profil, metrik ve yayın toplama.
- [x] **OpenAlex:** ORCID üzerinden profil, metrik ve yayın verisi; ham kayıtlar korunur, yayınlar ortak listede kaynak bilgisiyle tekilleştirilir.
- [x] **Web of Science:** Profil ve yayın toplama; WOS/WOK sonuçlarını tekilleştirme.
- [x] **YÖKSİS:** SOAP entegrasyonu ve desteklenen kategorilerden veri toplama.
- [x] **TR Dizin:** Yalnızca tam ORCID eşleşmesiyle yazar ve yayın toplama; ham yanıtlar ve kaynak bilgisi saklanır.
- [x] **Crossref:** Toplanmış eserlerin DOI'leriyle metaveri zenginleştirme; olumlu ve olumsuz sonuçlar önbelleğe alınır.
- [x] **Toplu iş kuyruğu:** Kalıcı SQL kuyruğu ve yapılandırılabilir SQL içe aktarma;
  arka plan işleyicisi ve içe aktarma varsayılan olarak kapalı.
- [x] **Merkezi hız/kota yönetimi:** Sağlayıcı bazlı sınırlar, bekleme ve yeniden deneme.
- [x] **Provider Status:** Erişilebilirlik, yerel bütçe ve bildirilen sağlayıcı kotası ayrı.
  Yerel sayaç gerçek sağlayıcı kotası değildir.
- [x] **AI raporu üretme:** Araştırmacı ID'siyle kayıtlı veriden Qwen/Ollama raporu üretme ve saklama.
- [x] **Kayıtlı raporu getirme:** Son raporu sağlayıcıya veya modele yeniden çağrı yapmadan döndürme.
  Analiz veri toplamaz; snapshot zamanı rapora giren mevcut kaydı etiketler.

## Bir İstek Ne Yapar?

Bir araştırmacıyı toplamak, aşağıdaki dış çağrıların birden fazlasını yapabilir.

| Sağlayıcı | İstek ne getirir? | İstek birimi |
| --- | --- | --- |
| ORCID | `/record`: profil ve yayın özetleri; ek çağrılar: eser ayrıntıları | Profil için 1; eserler en fazla 100'lük gruplar halinde |
| Google Scholar / SearchApi | `google_scholar_author`: profil, atıf metrikleri ve yayın sayfası | Her sonuç sayfası için 1; tüm yayınlar tek çağrıya sığmayabilir |
| OpenAlex | ORCID ile yazar profili/metrikleri; ardından yazarın eserleri | Yazar için 1; eserler en fazla 100'lük sayfalar halinde |
| Web of Science | ResearcherID ile `documents`: yayınlar ve atıf bilgileri | Her seçili veritabanında en fazla 50 sonuçluk sayfa için 1 |
| YÖKSİS | Kategori listeleri ve desteklenen eser ayrıntıları | 21 liste işlemi; bulunan bazı eserler için ayrıca detay çağrısı |
| TR Dizin | ORCID ile doğrulanmış yazar ve o yazarın yayın ayrıntıları | 1 yazar + 1 yayın listesi + listedeki her yayın için 1 ayrıntı çağrısı; eksik liste yanıtı başarısız sayılır |
| Crossref | Yalnızca diğer kaynaklarda zaten bulunan DOI'lerin bibliyografik metaverisi ve atıf sayısı | Önbellekte olmayan her benzersiz DOI için en fazla 1 `/works/{doi}` çağrısı; 404 sonuçları da zaman damgasıyla önbelleğe alınır |
| Yerel Ollama | `/api/chat` ile kayıtlı snapshot analizi | Üretilen rapor başına model çağrısı |

Önbellek kullanılırsa dış çağrı atlanabilir. Provider Status ayrıca küçük sağlık/kota sorguları yapar.

## Gereklilikler

| Durum | Bileşen | Gereken karar veya ayar |
| --- | --- | --- |
| [ ] | [ORCID](https://info.orcid.org/documentation/integration-guide/registering-a-public-api-client/) | Hesap açılacak; e-posta doğrulanıp Public API istemcisi ve erişim tokenı yapılandırılacak. Anonim okuma da mümkündür. |
| [ ] | [Google Scholar / SearchApi](https://www.searchapi.io/google-scholar) | Yöntem ve plan kararlaştırılacak; Scholar'ın resmî API'si olmadığı için şu an üçüncü taraf scraping servisi kullanılıyor. |
| [ ] | [OpenAlex](https://help.openalex.org/api/authentication/) | Ücretsiz hesap açılıp API anahtarı kullanılacak. [Günlük bütçe](https://help.openalex.org/access/example-costs/) $0,10 → $1 (10 kat); kart gerekmez, 100 istek/sn değişmez. |
| [x] | Web of Science | Kurumsal API anahtarı, erişilen plan ve veritabanları doğrulanacak. |
| [x] | YÖKSİS | Kurumsal kullanıcı bilgileri ve servis yetkisi sağlanacak. |
| [x] | [TR Dizin](https://development.trdizin.gov.tr/) | Kamuya açık uçlar kullanılır; ORCID eşleşmesi zorunludur. Sağlayıcı hız sınırı yayımlanmadığından üretim aralığı kurumla teyit edilecek. |
| [ ] | [Crossref](https://www.crossref.org/documentation/retrieve-metadata/rest-api/access-and-authentication/) | Public pool kullanılabilir. Polite pool için gerçek iletişim e-postası `Crossref:Mailto` deployment ayarına eklenecek. |
| [x] | Yerel Ollama | Kurulu; Qwen modeliyle smoke testi yapıldı. |

## API Planları, Fiyat ve Hız

*API tablosu: 3 Eylül 2026 kaynak notları; güncel plan ve kotalar hesap bazında teyit edilmeli.*

| API | Plan / erişim | Fiyat | Hız ve kota |
| --- | --- | --- | --- |
| [ORCID](https://info.orcid.org/ufaqs/what-are-the-api-limits/) | Public v3; anonim veya token | Ücretsiz, kullanım koşullu | 12 istek/sn; anonim 25.000/gün/IP, kayıtlı 100.000/gün/Client ID |
| [OpenAlex](https://help.openalex.org/access/example-costs/) | Anahtarsız / ücretsiz anahtar | Ücretsiz bütçe; ek kullanım ücretli | [100 istek/sn](https://help.openalex.org/api/authentication/); sırasıyla $0,10 / $1 günlük bütçe |
| [SearchApi — Scholar](https://www.searchapi.io/pricing) | Hesap planı teyitsiz | Pakete bağlı | Pakete bağlı |
| [Web of Science](https://developer.clarivate.com/apis/wos-starter) | Free Institutional Member — bildirilen plan | API ücretsiz; kurum aboneliği ayrıca | 5 istek/sn; 5.000 istek/gün |
| [YÖKSİS](docs/YOKSIS/YOKSIS_API_RAPORU.md) | Kurumsal Özgeçmiş V2 erişimi | Kurum/YÖK teyidi gerekli | Kamuya açık limit doğrulanamadı |
| [TR Dizin](https://development.trdizin.gov.tr/) | Kamuya açık veri uçları | Ücretsiz kamu erişimi | Yayımlanmış kota bulunmadı; uygulama varsayılanı 1 istek/sn |
| [Crossref](https://www.crossref.org/documentation/retrieve-metadata/rest-api/access-and-authentication/) | Public pool; isteğe bağlı polite pool | Ücretsiz | Tek DOI public pool: 5 istek/sn, eşzamanlılık 1; uygulama varsayılanı 200 ms ve sıralı |

SearchApi fiyat örneği: **Developer $40/ay → 10.000 arama/ay, 2.000 arama/saat**;
satın alınmış planı doğrulamaz. Ayrıntılar: [API raporu](docs/API_OZET_RAPORU.md).

## Öncelikli Yol Haritası

- [ ] **Türkçe AI:** Qwen rapor kalitesini gerçek örneklerle iyileştirmek.
- [ ] **Production ayarları:** Gerçek sağlayıcı hesaplarını, bütçeleri ve SQL kolon eşlemelerini doğrulamak.
- [ ] **BYS yetkisi:** Production öncesi oturum, yetki ve kayıt sahipliği denetimlerini eklemek.
- [ ] **Rapor UI:** Kayıtlı AI raporlarını gösteren ekranı isteğe bağlı olarak eklemek.

PR CI, sentetik akışlar ve yerel Qwen smoke testi geçti; gerçek sağlayıcı hesaplarıyla
tam uçtan uca doğrulama yapılmadı. Teknik ayrıntılar: [bulk](docs/BULK_COLLECTION.md),
[analiz](docs/RESEARCHER_ANALYSIS.md), [durum](docs/PROVIDER_STATUS.md).
