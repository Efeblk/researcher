# Proje Özet Raporu

**Proje durumu:** 11 Eylül 2026 · Çalışan entegrasyon prototipi; production'a hazır değil.

## Mevcut Bileşenler

### Sağlayıcılar

- [x] **ORCID:** Profil ve eser toplama.
- [x] **Google Scholar / SearchApi:** Profil, metrik ve yayın toplama uygulanmıştır;
  sağlayıcı `academicsettings.json` içinde varsayılan olarak kapalıdır.
- [x] **OpenAlex:** ORCID üzerinden profil, metrik ve yayın toplama.
- [x] **Web of Science:** Profil ve yayın toplama; WOS/WOK sonuçlarını tekilleştirme.
- [x] **YÖKSİS:** SOAP entegrasyonu ve desteklenen kategorilerden veri toplama.
- [x] **TR Dizin:** Yalnızca tam ORCID eşleşmesiyle yazar ve yayın toplama.
- [x] **Crossref:** Toplanmış eserlerin DOI'leriyle metaveri zenginleştirme ve önbellek.
- [x] **Semantic Scholar:** Normal Gönder (Submit) ve toplu toplama akışında mevcut eser DOI'leri
  otomatik zenginleştirilir; ortak makale/atıf önbelleği, bağlamlar, niyetler, TLDR ve açık erişim
  PDF kaynağı saklanır. Atıf yapan eserler araştırmacının yayın listesine eklenmez.

### Ham Veri

- [x] Sağlayıcıların ham yanıt ve kayıtları SQL Server'da saklanıyor.
- [x] OpenAlex ve TR Dizin ham kayıtlarında kaynak bilgisi korunuyor.
- [x] Crossref'te olumlu ve olumsuz sonuçlar zaman damgasıyla önbelleğe alınıyor.
- [x] Semantic Scholar makale yanıtları, atıf ilişkileri ve bağlamları ayrı sağlayıcı tablolarında
  tutuluyor; yarım kalan yenilemede tamamlanmış eski satırlar korunuyor.
- [x] SQL ilişkileri normalize DOI ile ortak Semantic Scholar makalesini, hedef makale ile atıf
  yapan eseri ve atıf ile sıralı bağlamlarını bağlıyor; `AcademicWorkSources` kaynak sağlayıcı
  atfını koruyor. Bilinmeyen sayaçlar `null` kalıyor; `Found`, `CitationsComplete`,
  `CitationNextOffset`, `CitationsRefreshing` ve `HasPendingWork` sonuç ve tamamlanma durumunu
  açıkça gösteriyor. Üst sınıra ulaşan `CitationsComplete=false` tek başına yeni iş kaldığı anlamına gelmiyor.

### Özet Veri

- [x] Tekilleştirilmiş yayınlar `PublicationSummaries` tablosunda tutuluyor.
- [x] OpenAlex ve TR Dizin yayınları ortak listede kaynak bilgisiyle birleştiriliyor.
- [x] Crossref çağrısı sonraki bir DOI'de başarısız olsa bile daha önce tamamlanan
  zenginleştirmeler özetlere işleniyor; tekrar denemede önbellek kullanılıyor.
- [x] Semantic Scholar açık erişim PDF bağlantıları aynı DOI'ye sahip personele ait eserlerin
  kaynaklarına ekleniyor. S2 sayaçları sağlayıcı kaydında kalıyor; yayın özetinin sağlayıcı
  metrikleriyle veya yerel AI raporuyla birleştirilmiyor.
- [x] Makale özetleri kayıtlı PDF metni veya veritabanındaki özetten üretiliyor. Model iddiaları
  yalnızca kayıtlı kaynak parçalarına bağlanıyor, ayrı doğrulama geçişinde desteklenmeyen veya
  belirsiz iddialar eleniyor ve kanıt kalmazsa başarılı rapor kaydedilmiyor.

### Uygulama ve İş Akışları

- [x] **Service / V1 API:** Client bağımsız, Serenity uyumlu toplama ve yayın API'si.
- [x] **SQL Server:** FluentMigrator migration'ları ve kalıcı veri katmanı.
- [x] **Sağlayıcı metrikleri:** WoS, OpenAlex ve Scholar metrikleri `Researchers` tablosunda `PersonelID` bazında sorgulanabilir.
- [x] **Web client:** Profil, yayın listesi ve okulda gösterilecek yayın seçimi.
- [x] **Akademik metrikler özeti:** Dar ve geniş ekranlarda açılıp kapanabilir görünüm;
  ORCID yayın sayısını, Scholar, WoS ve OpenAlex yayın/atıf/h-indeksi değerlerini ayrı gösterir.
  Eksik değer `—`, gerçek sıfır `0` olarak gösterilir; sağlayıcı sayıları birleştirilmez.
- [x] **Toplu iş kuyruğu:** Kalıcı SQL kuyruğu ve yapılandırılabilir SQL içe aktarma;
  arka plan işleyicisi ve içe aktarma varsayılan olarak kapalı.
- [x] **Toplu iş dayanıklılığı:** Karışık sağlayıcı hatalarında yeniden deneme sayısı sınırlıdır;
  iptal edilen Crossref çağrıları iptal olarak iletilir.
- [x] **Merkezi hız/kota yönetimi:** Sağlayıcı bazlı sınırlar, bekleme ve yeniden deneme.
- [x] **Provider Status:** Erişilebilirlik, yerel bütçe ve bildirilen sağlayıcı kotası ayrı.
  Yerel sayaç gerçek sağlayıcı kotası değildir.
- [x] **AI raporu üretme:** Araştırmacı ID'siyle kayıtlı veriden Qwen/Ollama raporu üretme ve saklama.
- [x] **Kayıtlı raporu getirme:** Son raporu sağlayıcıya veya modele yeniden çağrı yapmadan döndürme.
  Analiz veri toplamaz; snapshot zamanı rapora giren mevcut kaydı etiketler.
- [x] **Makale özeti API'si:** Seçilen eserin kayıtlı PDF'si veya özetiyle kanıt bağlantılı ve
  doğrulanmış iddialar üretme; son başarılı sonucu model çağrısı yapmadan getirme.

## Bir İstek Ne Yapar?

Bir araştırmacıyı toplamak, aşağıdaki dış çağrıların birden fazlasını yapabilir.

| Sağlayıcı | İstek ne getirir? | İstek birimi |
| --- | --- | --- |
| ORCID | `/record`: profil ve yayın özetleri; ek çağrılar: eser ayrıntıları | Profil için 1; eserler en fazla 100'lük gruplar halinde |
| Google Scholar / SearchApi | `google_scholar_author`: profil, atıf metrikleri ve yayın sayfası | Her sonuç sayfası için 1; tüm yayınlar tek çağrıya sığmayabilir |
| OpenAlex | ORCID ile yazar profili/metrikleri ve eserler; PR #52 birleşince DOI metaveri geri dönüşü | Yazar için 1; eserler en fazla 100'lük sayfalar halinde; bekleyen geri dönüş yalnız özet isteğinde ve önbellek kaçırıldığında |
| Web of Science | ResearcherID ile `documents`: yayınlar ve atıf bilgileri | Her seçili veritabanında en fazla 50 sonuçluk sayfa için 1 |
| YÖKSİS | Kategori listeleri ve desteklenen eser ayrıntıları | 21 liste işlemi; bulunan bazı eserler için ayrıca detay çağrısı |
| TR Dizin | ORCID ile doğrulanmış yazar ve o yazarın yayın ayrıntıları | 1 yazar + 1 yayın listesi + listedeki her yayın için 1 ayrıntı çağrısı; eksik liste yanıtı başarısız sayılır |
| Crossref | Mevcut DOI'lerin bibliyografik metaverisi ve atıf sayısı; PR #52 birleşince makale kaynağı/özet geri dönüşü | Önbellekte olmayan her benzersiz DOI için en fazla 1 `/works/{doi}` çağrısı; 404 sonuçları da zaman damgasıyla önbelleğe alınır |
| Semantic Scholar | Normal Submit ve toplu akışta mevcut `AcademicWorks` DOI'leri için makale metaverisi, açık erişim PDF, TLDR, atıf ve bağlamlar | Önbellekte olmayan DOI için 1 makale çağrısı; atıflar varsayılan 100'lük sayfalarla, makale başına en fazla 500 taranan kayıt ve çalıştırma başına 10 DOI |
| Gemini | Makale kaynağından yapılandırılmış özet ve her iddia için ayrı kanıt doğrulaması | En az 1 özet + doğrulama çağrısı; bağlam sınırında parça ve doğrulama grubu sayısına göre ek çağrı olabilir |
| Yerel Ollama | `/api/chat` ile araştırmacı snapshot analizi; ayrıca açıkça seçilirse makale özeti ve iddia doğrulaması | Üretilen rapor başına model çağrısı; makale doğrulamasında ek çağrılar olabilir |
| Unpaywall *(PR #52, birleşme bekliyor)* | Kayıtlı kaynaklar kullanılamazsa DOI ile açık erişim PDF ve açılış sayfası adayı bulma | Yalnız `Unpaywall:Email` ayarlıysa, DOI metaveri önbelleği kaçırıldığında çağrılır |
| Tesseract *(yerel; PR #52, birleşme bekliyor)* | Metin katmanı olmayan sınırlı sayıdaki PDF sayfasına OCR uygulama | Harici HTTP isteği yoktur; sayfa, süre, DPI, piksel ve çıktı boyutu sınırları vardır |

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
| [x] | [Semantic Scholar](https://www.semanticscholar.org/product/api) | API anahtarı isteğe bağlıdır; kullanılacaksa `SemanticScholar:ApiKey` güvenli deployment ayarıyla verilecek. Anonim kullanımda uygulama varsayılanı 1 istek/sn'dir. |
| [x] | Yerel Ollama | Kurulu; Qwen modeliyle smoke testi yapıldı. |
| [ ] | Gemini | Makale özeti için ücretli API anahtarı user-secret ile verilecek; gerçek makalelerde kalite ve maliyet henüz tam benchmark edilmedi. |
| [ ] | Tesseract OCR *(PR #52)* | OCR kullanılacak hostta Tesseract ile `eng` ve `tur` dil verileri isteğe bağlı kurulacak; yol gerekirse `ArticleSummary:TesseractPath` ile ayarlanacak. |
| [ ] | Unpaywall *(PR #52)* | Entegrasyon kullanılacaksa isteğe bağlı `Unpaywall:Email` değeri kişisel varsayılan eklenmeden user-secret veya güvenli deployment ayarıyla verilecek. |
| [ ] | Scite | Yalnızca araştırma konusu; entegrasyon uygulanmadı ve production sağlayıcısı olarak sayılmıyor. |

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
| [Semantic Scholar](https://www.semanticscholar.org/product/api) | Anonim veya isteğe bağlı API anahtarı | Ücretsiz erişim; sağlayıcı koşullarına bağlı | Uygulama varsayılanı 1 istek/sn; gerçek sağlayıcı kotası hesap/erişim biçimine göre teyit edilmeli |

SearchApi fiyat örneği: **Developer $40/ay → 10.000 arama/ay, 2.000 arama/saat**;
satın alınmış planı doğrulamaz. Ayrıntılar: [API raporu](docs/API_OZET_RAPORU.md).

## Öncelikli Yol Haritası

- [ ] **Türkçe AI:** Qwen rapor kalitesini gerçek örneklerle iyileştirmek.
- [ ] **PR #52'yi tamamlamak:** Ayrı incelenen dalda amaçlanan semantik HTML gövdesi,
  sınırlı Tesseract OCR, OpenAlex/Crossref/Unpaywall DOI metaveri geri dönüşü ve kaynak kapsamı
  davranışlarını düzeltmelerden sonra birleştirip yeniden doğrulamak. Bu yetenekler henüz `main`de değildir.
- [ ] **Makale özeti kalite ölçümü:** Ücretli Gemini akışını temsilî Türkçe/İngilizce, metin PDF,
  taranmış PDF ve yalnız özet örnekleriyle kalite, doğruluk, süre ve maliyet açısından benchmark etmek.
- [ ] **Production ayarları:** Gerçek sağlayıcı hesaplarını, bütçeleri ve SQL kolon eşlemelerini doğrulamak.
- [ ] **BYS yetkisi:** Production öncesi oturum, yetki ve kayıt sahipliği denetimlerini eklemek.
- [ ] **Rapor UI:** Kayıtlı AI raporlarını gösteren ekranı isteğe bağlı olarak eklemek.

Birleşmiş değişikliklerin PR CI ve sentetik akışları ile yerel Qwen smoke testi geçti; gerçek sağlayıcı
hesaplarıyla tam uçtan uca doğrulama ve ücretli Gemini kalite benchmark'ı yapılmadı. PR #52'nin son
amaçlanan HTML/OCR/metaveri geri dönüş davranışı ayrı committe incelendi ve birleşmeyi bekliyor.
Teknik ayrıntılar: [bulk](docs/BULK_COLLECTION.md),
[analiz](docs/RESEARCHER_ANALYSIS.md), [durum](docs/PROVIDER_STATUS.md).
