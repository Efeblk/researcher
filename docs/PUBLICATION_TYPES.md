# Publication type normalization

Collector providers keep their original publication type in `AcademicWork.RawType` and their payload in
`ProviderPayload`. `AcademicWork.Category` is the shared, stable enum used for cross-provider filtering and
summaries. Unknown values map to `Unknown`; they are never assumed to be articles.

The shared dictionary ignores case, Turkish diacritics, and separators such as spaces, hyphens, underscores,
and slashes. Its principal multilingual aliases are:

| Category | Examples |
| --- | --- |
| `Article` | `article`, `makale` |
| `Review` | `review`, `derleme` |
| `ConferencePaper` | `conference-paper`, `proceedings paper`, `bildiri` |
| `Book` | `book`, `kitap` |
| `BookChapter` | `book-chapter`, `kitap bölümü` |
| `BookReview` | `book-review`, `kitap incelemesi` |

Provider-specific values are interpreted only in their provider context. For example, Scopus `ar`, `bk`,
`ch`, and `cp` are Article, Book, BookChapter, and ConferencePaper, but those short strings remain unknown
for other providers. TR Dizin `BOOK_PRESENTATION` is BookReview, `MEETING_SUMMARY` is ConferenceAbstract,
and `RETRACTED` is Retraction. Crossref values such as `journal-article`, `monograph`, and
`proceedings-article` retain their established mappings.

Web of Science may supply several comma-separated document types. Selection remains deterministic in this
order: Article, Review, ProceedingsPaper, ConferenceAbstract, BookChapter, Book, BookReview, Editorial,
Letter, Erratum, Retraction, then DataPaper. Canonical publication selection remains source-aware and keeps
its existing ORCID-first and metadata/identifier ordering; normalization does not change DOI identity or
deduplication rules.

New mappings take effect the next time a researcher's works are collected and synchronized. No migration or
backfill endpoint is involved, and recalculating metrics does not call providers or rewrite publication types.
