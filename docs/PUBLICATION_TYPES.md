# Yayın türü normalizasyonu

Collector sağlayıcıları özgün yayın türünü `AcademicWork.RawType`, sağlayıcı yanıtını ise
`ProviderPayload` alanında korur. `AcademicWork.Category`, sağlayıcılar arası filtreleme ve özetlerde
kullanılan ortak, sabit enum değeridir. Tanınmayan değerler `Unknown` olur; makale kabul edilmez.

Ortak sözlük büyük-küçük harf, Türkçe karakter ve boşluk, tire, alt çizgi ile eğik çizgi gibi ayraç
farklarını yok sayar. Başlıca çok dilli karşılıklar şunlardır:

| Kategori | Örnekler |
| --- | --- |
| `Article` | `article`, `makale` |
| `Review` | `review`, `derleme` |
| `ConferencePaper` | `conference-paper`, `proceedings paper`, `bildiri` |
| `Book` | `book`, `kitap` |
| `BookChapter` | `book-chapter`, `kitap bölümü` |
| `BookReview` | `book-review`, `kitap incelemesi` |

Sağlayıcıya özgü değerler yalnızca ilgili sağlayıcı bağlamında yorumlanır. Örneğin Scopus `ar`, `bk`,
`ch` ve `cp` değerleri sırasıyla Article, Book, BookChapter ve ConferencePaper olur; aynı kısa değerler
diğer sağlayıcılarda `Unknown` kalır. TR Dizin `BOOK_PRESENTATION` değeri BookReview,
`MEETING_SUMMARY` değeri ConferenceAbstract, `RETRACTED` değeri Retraction olur. Crossref
`journal-article`, `monograph` ve `proceedings-article` gibi mevcut eşlemelerini korur.

Web of Science virgülle ayrılmış birden çok belge türü sağlayabilir. Seçim sırası değişmez: Article,
Review, ConferencePaper, ConferenceAbstract, BookChapter, Book, BookReview, Editorial, Letter, Erratum,
Retraction ve DataPaper. Çakışan kanonik kategoriler mevcut ORCID öncelikli metadata/kimlik sırasıyla
seçilir; bütün kaynak gözlemleri korunur. Normalizasyon DOI kimliği veya tekilleştirme kurallarını değiştirmez.

Yeni eşlemeler araştırmacının yayınları yeniden toplandığında ve eşitlendiğinde uygulanır. Metrikleri yeniden
hesaplamak sağlayıcı çağrısı yapmaz ve yayın türlerini güncellemez; geçiş için migration veya backfill endpoint'i
yoktur.
