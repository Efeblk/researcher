# Yayın türü normalizasyonu

Collector, sağlayıcının özgün türünü `AcademicWork.RawType`, yanıtını ise `ProviderPayload` alanında korur.
`AcademicWork.Category`, sağlayıcılar arasında filtreleme ve özetleme için kullanılan ortak enum değeridir.
Tanımlı olmayan bir değer `Unknown` kalır; benzer görünen bir türe tahminle dönüştürülmez. Bilinen fakat mevcut
enum içinde karşılığı olmayan koleksiyon, ortam ve idari kayıt türleri `Other` olur.

## Ortak taksonomi

Normalizasyon büyük-küçük harf, aksan ve ayraç farklarını yok sayar. Aşağıdaki Türkçe karşılıklar bütün
sağlayıcılarda tanınır:

| Kategori | Türkçe karşılık |
| --- | --- |
| `Article` | makale |
| `Book` | kitap |
| `BookChapter` | kitap bölümü |
| `BookReview` | kitap incelemesi, kitap tanıtımı |
| `ConferenceAbstract` | bildiri özeti |
| `ConferencePaper` | bildiri, kongre bildirisi |
| `DataPaper` | veri makalesi |
| `Dataset` | veri seti, veriseti |
| `Dissertation` | tez |
| `Editorial` | editör yazısı |
| `Erratum` | düzeltme |
| `Letter` | mektup |
| `LibGuide` | kütüphane rehberi |
| `Other` | diğer |
| `Patent` | patent |
| `Paratext` | yan metin |
| `PeerReview` | hakemlik, hakem değerlendirmesi |
| `Preprint` | önbaskı, ön baskı |
| `ReferenceEntry` | referans maddesi |
| `Report` | rapor |
| `Retraction` | geri çekme bildirimi, geri çekilme bildirimi |
| `Review` | derleme, literatür derlemesi |
| `Software` | yazılım |
| `SoftwarePaper` | yazılım makalesi |
| `Standard` | standart |
| `SupplementaryMaterials` | ek materyal, ek malzeme |

Benzer adlar ayrı tutulur: `book-chapter` kitap değildir; `data-paper` veri seti değildir; `peer-review`
derleme değildir; `retraction` özgün makale değildir.

## Sağlayıcı katalogları

- **OpenAlex:** [25 resmi work type](https://help.openalex.org/data/work-types/) doğrudan ortak taksonomiye
  eşlenir. Buna `libguides`, `software-paper`, `paratext` ve `supplementary-materials` dahildir.
- **ORCID:** [resmi `WorkType` kataloğundaki](https://raw.githubusercontent.com/ORCID/orcid-model/master/src/main/java/org/orcid/jaxb/model/common/WorkType.java)
  58 değer kapsanır. Kitap, makale, konferans, veri seti, tez, patent, önbaskı, rapor, yazılım ve standart
  türleri kesin karşılıklarına gider. Görsel, ses, web sitesi, araştırma aracı ve benzeri tanınan çıktılar
  `Other`; `undefined` ise `Unknown` olur.
- **Crossref:** [`/types` kataloğundaki](https://api.crossref.org/types) 30 kimlik kapsanır.
  `book-section`, `book-part` ve `book-chapter`, `BookChapter`; monograf ve referans kitapları `Book` olur.
  Dergi/cilt/sayı, seri, proceedings kabı, grant ve genel `posted-content` gibi kayıtlar `Other` olur.
  `posted-content`, tek başına önbaskı kanıtı sayılmaz.
- **Scopus:** [resmi DOCTYPE kodları](https://dev.elsevier.com/sc_search_tips.html) sağlayıcıya özel
  yorumlanır. `ar`, `bk`, `ch`, `cp`, `cr`, `dp`, `ed`, `er`, `le`, `re` ve `sh` kesin kategorilere;
  `ab`, `no` ve `pr` ise daha dar bir yayın türünü kanıtlamadıkları için `Other` kategorisine gider.
  Bu kısa kodlar başka sağlayıcılara sızmaz.
- **Web of Science:** [resmi belge türleri](https://webofscience.zendesk.com/hc/en-us/articles/26916283577745-Document-Types)
  virgül veya noktalı virgülle gelen çoklu değerler olarak okunur. Seçim önceliği Article, Review,
  ConferencePaper, ConferenceAbstract, BookChapter, Book, BookReview, Editorial, Letter, Erratum,
  Retraction ve DataPaper sırasını korur. `Early Access`, `Retracted Publication`, `Withdrawn Publication`
  ve `Publication with Expression of Concern` durum bilgisidir; tek başına kategori üretmez ve yanında
  belge türü varsa seçimde yok sayılır. Sanat, müzik, film, donanım ve veritabanı incelemeleri akademik
  `Review` yerine `Other` olur.
- **TR Dizin:** API değerleri açıkça eşlenir. `BOOK_PRESENTATION` → `BookReview`, `MEETING_SUMMARY` →
  `ConferenceAbstract`, `LETTER_TO_EDITOR` → `Letter`, `SHORT_REPORT` → `Report` ve `RETRACTED` →
  `Retraction` dönüşümleri korunur.

Sağlayıcıya özgü kısaltmalar yalnız ilgili sağlayıcının bağlamında yorumlanır. Normalizasyon DOI kimliğini,
tekilleştirme kurallarını veya kanonik kaynak önceliğini değiştirmez. Yeni eşlemeler yayınlar yeniden
toplanıp eşitlendiğinde uygulanır; geçiş için migration veya backfill endpoint'i yoktur.
