# Tam servis ayrımı kabul planı

Bu planın hedefi iki bağımsız çalıştırılabilir servis ve tek SQL Server veritabanıdır. Collector yalnız toplama, sağlayıcı entegrasyonları, normalizasyon, kanonik çekirdek, yayın seçimi, bulk ve sağlayıcı durumunu yürütür. `ResearcherAnalysisService` araştırmacı/makale analizlerini, metrikleri, bilgi ve grafik ürünlerini, değerlendirmeleri, İK/fakülte akışlarını, bunların kalıcılığını ve worker'larını yürütür. Üçüncü servis veya ayna veritabanı yoktur.

## Veritabanı sahipliği

Collector aşağıdaki 31 veri tablosunun DDL ve yazma sahibidir:

| Mevcut DDL migration'ı | Collector tabloları |
| --- | --- |
| `202608250001` | `core.Researchers`, `core.AcademicWorks`, `core.PublicationSummaries`, `core.PublicationDisplayApprovals`; `orcid.OrcidProfiles`, `orcid.OrcidWorks`; `wos.WebOfScienceProfiles`, `wos.WebOfScienceWorks`, `wos.WebOfSciencePeerReviews`; `yoksis.YoksisRecords` |
| `202608270001` | `googlescholar.GoogleScholarProfiles`, `googlescholar.GoogleScholarWorks` |
| `202608280001` | `openalex.OpenAlexProfiles`, `openalex.OpenAlexWorks` |
| `202609060001` | `bulk.BulkCollectionBatches`, `bulk.BulkCollectionJobs`, `integrations.ProviderRequestBudgets` |
| `202609090001` | `integrations.ProviderStatusObservations` |
| `202609100002` | `trdizin.TrDizinProfiles`, `trdizin.TrDizinWorks`, `crossref.CrossrefWorks` |
| `202609100004` | `core.AcademicWorkSources` |
| `202609100005` | `semanticscholar.SemanticScholarPapers`, `semanticscholar.SemanticScholarCitations`, `semanticscholar.SemanticScholarCitationContexts` |
| `202609110003` | `core.CanonicalWorks`, `core.CanonicalWorkObservations`, `core.CanonicalResearcherWorks` |
| `202609110008` | `core.AcademicWorkResearchContexts`, `core.AcademicWorkTopics` |
| Yeni ayrım migration'ı | `core.CollectionChanges` |

Analysis Service aşağıdaki 30 veri tablosunun DDL ve yazma sahibidir:

| Mevcut DDL migration'ı | Analysis Service tabloları |
| --- | --- |
| `202609080001` | `analysis.ResearcherAnalyses` |
| `202609100003` | `analysis.ArticleSummaries` |
| `202609140001` | `analysis.GeminiUsageAttempts` |
| `202609110004` | `analysis.ArticleSourceSnapshots`, `analysis.ArticleSourcePages`, `analysis.ArticleSourceSpans`, `analysis.CanonicalArticleAnalysisRuns`, `analysis.CanonicalArticleClaims`, `analysis.CanonicalArticleClaimEvidence` |
| `202609110005` | `analysis.ArticleSummaryAutomationJobs` |
| `202609110006` | `analysis.PublicationMetricSnapshots`, `analysis.PublicationMetricsRefreshStates` |
| `202609110007` | `analysis.PublicationMetricProviderSnapshots` |
| `202609110009` | `analysis.CanonicalArticleReviewRuns`, `analysis.CanonicalArticleReviewFindings`, `analysis.CanonicalArticleReviewEvidence` |
| `202609110010`, `202609120013` | `analysis.ArticleEvaluationRuns`, `analysis.ArticleEvaluationCases`, `analysis.ArticleEvaluationWorkItems`, `analysis.ArticleEvaluationAttempts`, `analysis.ArticleEvaluationResults` |
| `202609120002` | `analysis.ReferencePopulationManifests`, `analysis.ReferencePopulationMembers` |
| `202609120012` | `analysis.ArticleReviewWorkItems`, `analysis.ArticleReviewStageCheckpoints` |
| `202609120010` | `hr.EvidenceDossiers`, `hr.DossierReviewActions` |
| `202609120011` | `faculty.AssistantContextVersions`, `faculty.AssistantRuns` |
| Yeni ayrım migration'ı | `analysis.CollectionChangeReceipts` |

`core.PublicationSummaries` bibliyografik normalizasyon çıktısıdır ve collector'da kalır; `analysis.ArticleSummaries` AI makale raporudur ve Analysis Service'e geçer. `core.CollectionChanges` normalize kaynak değişikliklerini kalıcı olarak bildirir; `analysis.CollectionChangeReceipts` Analysis Service'in idempotent tüketim durumunu saklar.

Migration kabulü:

- Collector yalnız `dbo.VersionInfo`, Analysis Service yalnız `dbo.ResearcherAnalysisVersionInfo` geçmişini kullanır. Salt analitik migration tipleri collector assembly'sinden çıkarılır; mevcut `dbo.VersionInfo` satırları korunur ve no-op/tombstone migration yığını eklenmez. Gemini ledger ve diğer analitik DDL'ler Analysis Service'e aittir.
- Karışık eski `202609110002` migration'ı collector tablolarına daraltılır. Analysis Service'in yeni final-schema baseline'ı mevcut analitik tabloları ve satırları değiştirmeden benimser; fresh kurulumda aynı son şemayı kurar. Kimlik, audit, maliyet ve kuyruk satırları kaybolmaz veya yeniden yazılmaz.
- Servisler arası foreign key oluşturulmaz. Analysis tablolarındaki `PersonelID`, `CanonicalWorkId`, `AcademicWorkId` gibi kaynak kimlikleri mantıksal referanstır; uygunluk Analysis Service'in salt okunur kaynak sorgularıyla doğrulanır. Analysis/hr/faculty içindeki kendi-servis foreign key ve unique/index kuralları korunur.
- Fresh veritabanında Analysis-first yalnız `analysis`, `hr`, `faculty` ve kendi history/receipt tablolarını; Collector-first yalnız collector şemaları, history ve değişiklik sinyalini oluşturur. Eşzamanlı başlangıç ortak SQL uygulama kilidiyle yarışmadan tamamlanır.
- Development collector temizliği yalnız collector tablolarını/history/sinyalini indirir; Analysis Service tablolarına ve `dbo.ResearcherAnalysisVersionInfo` geçmişine dokunmaz.

## Kod, worker ve HTTP sınırı

Collector'da yalnız `BulkCollectionWorker` kalır. `ArticleSummaryAutomationWorker`, `PublicationMetricsWorker`, `ArticleEvaluationWorker` ve `FacultyAssistantWorker` aynı varsayılan iş davranışlarıyla Analysis Service'e taşınır. Analysis Service, collector'ın kalıcı `CollectionChanges` kayıtlarını salt okunur olarak izler ve kendi receipt/kuyruk tablolarına idempotent planlama yazar; collector Analysis HTTP çağrısı veya AI sağlık proxy'si yapmaz.

Collector `http://localhost:5001` altında `Collect`, `GetResearcher`, `ListPublications`, `SavePublicationSelections`, `ListCanonicalPublications`, `RebuildCanonicalPublications`, `Bulk/{Submit,Status,ImportSql}`, YÖKSİS, Semantic Scholar ve yalnız toplama sağlayıcılarının `ProviderStatus` işlemlerini tutar.

Analysis Service 25 kalıcı ürün sözleşmesini `http://localhost:5011/api/v1/products/[action]` altında aynı action adları ve gövde anlamlarıyla sunar:

- Araştırmacı analizi: `AnalyzeResearcher`, `GetResearcherAnalysis`, `GetResearcherSourceCoverage`.
- Makale özeti/incelemesi: `SummarizeArticle`, `GetArticleSummary`, `GetArticleSummaryAutomationStatus`, `GetCanonicalArticleEvidence`, `ReviewCanonicalArticle`, `GetCanonicalArticleReview`.
- Metrik: `GetResearcherPublicationMetrics`, `RefreshResearcherPublicationMetrics`.
- Bilgi/grafik: `SearchAcademicEvidence`, `GetReferencePopulation`, `ImportReferencePopulation`, `ExportAcademicEvidenceGraph`.
- Değerlendirme: `StartArticleEvaluation`, `GetArticleEvaluation`.
- İK: `CreateHrEvidenceDossier`, `GetHrEvidenceDossier`, `AppendHrDossierReviewAction`, `ListHrDossierReviewActions`.
- Fakülte: `SaveFacultyAssistantContext`, `GetFacultyAssistantContext`, `StartFacultyAssistant`, `GetFacultyAssistantRun`.

Mevcut stateless generation uçları (`/api/v1/analyze`, `/api/v1/articles/*`, `/api/v1/evaluations/*`, `/api/v1/faculty-assistant`) korunur. Kalıcı ürün yüzeyi güvenilir kimlik/özne kapsamı denetimini Analysis Service'te uygular; `PersonelID` tek başına yetki değildir. Analysis sağlayıcı durumu Analysis Service'te kalır; collector sağlayıcı durumu AI proxy çağrısı yapmaz.

## Çalışma ve veri akışı kabulü

1. Collector dış sağlayıcıları çağırır, provider/core tablolarını tek normalizasyon transaction'ında günceller ve aynı sahiplik sınırında kalıcı değişiklik sinyali yazar.
2. Analysis Service aynı veritabanındaki collector tablolarını açık salt okunur SQL projection'larıyla okur. Analiz kanıtı için gereken değişmez snapshot temsillerini kendi tablolarında saklar; collector tablolarını kopyalamaz, collector modellerini yazmaz ve bir ayna veritabanı üretmez.
3. Analysis worker'ları değişiklik sinyalini kendi receipt kaydıyla idempotent işler; özet, metrik ve diğer ürün durumlarını yalnız `analysis`, `hr` ve `faculty` tablolarına yazar.
4. Collector kapalıyken Analysis Service mevcut normalize kaynaklardan ürün okuma/işleme yapabilir. Collector şeması henüz yoksa Analysis Service başlar, stateless uçları çalışır ve kalıcı worker'lar kaynak hazır olana kadar hata üretmeden bekler.
5. Analysis Service kapalıyken collector toplama, normalize etme, kanonik listeleme, bulk ve sağlayıcı durumu işlemlerine devam eder; bir Analysis endpoint'ine veya sağlık proxy'sine bağımlı değildir.
6. Her iki servis aynı SQL veritabanı hedefini farklı, en-az-yetkili hesaplarla kullanabilir. Startup migration açıkken her hesap kendi şema/history nesneleri için gereken DDL yetkisini de alır; migration dışarıda uygulanıyorsa collector kendi tablolarında yazma, Analysis kendi tablolarında yazma ve collector kaynaklarında yalnız okuma yetkisiyle çalışır.

## Değişmez davranış ve doğrulama

- Taşıma, dış sağlayıcı toplama eşlemesini, kanonikleştirme kurallarını, yayın seçimini, prompt/sözleşme sürümlerini, model adlarını, thinking/timeout/token/maliyet varsayılanlarını veya AI fail-closed kullanım defteri davranışını değiştirmez.
- Kalıcı product request/response alanları, action adları, idempotency anahtarları, pagination ve erişim hata semantiği korunur. Host/prefix değişir; collector kaynak şeması veya gerekli kaynak satırı henüz hazır değilse Analysis Service'in verdiği açık `503` kaynak-hazır-değil yanıtı yeni servis sınırının kasıtlı sonucudur. Stateless uç sözleşmeleri ve `X-Analysis-Key` davranışı değişmez.
- Fresh Collector-only, fresh Analysis-only, iki başlangıç sırası, eşzamanlı migration, mevcut dolu veritabanı upgrade'i ve servislerden birinin offline olduğu senaryolar ayrı entegrasyon kontrolleriyle doğrulanır.
- Şema envanteri testinde her tablo tam bir DDL/yazma sahibine aittir; Analysis DbContext/modeli collector yazma operasyonu içermez, collector assembly/DI/API/worker ağacında analitik ürün tipi kalmaz.
- HTTP örnekleri collector ve Analysis klasörlerine yeni host/prefix'e göre ayrılır; değişkenler genişletildiğinde JSON geçerlidir, salt okuma ve ücretli/worker yazma ön koşulları açıktır. Dokümanlarda collector'ın analitik ürün veya AI proxy sahibi olduğu eski anlatım kalmaz.
- Model ve sonuç değişmezliği sentetik fixture'larla doğrulanır; hiçbir otomatik kontrol canlı veya ücretli sağlayıcı çağrısı yapmaz.

Bu plan yalnız kabul sınırını tanımlar. Salt okunur source projection'larının sınıf ve ayar adları core uygulama diff'i kesinleşince belgelerde gerçek isimleriyle güncellenecektir.
