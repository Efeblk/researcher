# Tam servis ayrımı kabul planı

Bu planın hedefi iki bağımsız çalıştırılabilir servis ve tek SQL Server veritabanıdır. Collector yalnız toplama, sağlayıcı entegrasyonları, normalizasyon, kanonik çekirdek, yayın seçimi, bulk ve sağlayıcı durumunu yürütür. `ResearcherAnalysisService` araştırmacı/makale analizlerini, metrikleri, bilgi ve grafik ürünlerini, değerlendirmeleri, İK/fakülte akışlarını, bunların kalıcılığını ve worker'larını yürütür. Üçüncü servis veya ayna veritabanı yoktur.

## Veritabanı sahipliği

Collector aşağıdaki 37 veri tablosunun DDL ve yazma sahibidir:

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
| `202609170003` | `core.CanonicalWorkDoiAliases`, `core.CanonicalWorkDoiRelations` |
| `202609140002` | `analysis`, `hr`, `faculty` şemalarını oluşturur |

Analysis Service aşağıdaki 30 veri tablosunun DDL ve yazma sahibidir:

| Analysis migration'ı | Analysis Service nesneleri |
| --- | --- |
| `202609140001` | `analysis.GeminiUsageAttempts` |
| `202609140002` | `analysis`, `hr`, `faculty` şemalarını oluşturur |
| `202609140003` | `analysis.ResearcherAnalyses` |
| `202609140004` | `analysis.ArticleSummaries` |
| `202609140005` | `analysis.ArticleSourceSnapshots`, `analysis.ArticleSourcePages`, `analysis.ArticleSourceSpans`, `analysis.CanonicalArticleAnalysisRuns`, `analysis.CanonicalArticleClaims`, `analysis.CanonicalArticleClaimEvidence` |
| `202609140006` | `analysis.ArticleSummaryAutomationJobs` ve analysis-run otomasyon alanları |
| `202609140007` | `analysis.PublicationMetricSnapshots`, `analysis.PublicationMetricsRefreshStates` |
| `202609140008` | `analysis.PublicationMetricProviderSnapshots` |
| `202609140009` | `analysis.CanonicalArticleReviewRuns`, `analysis.CanonicalArticleReviewFindings`, `analysis.CanonicalArticleReviewEvidence` |
| `202609140010` | `analysis.ArticleEvaluationRuns`, `analysis.ArticleEvaluationCases`, `analysis.ArticleEvaluationWorkItems`, `analysis.ArticleEvaluationAttempts`, `analysis.ArticleEvaluationResults` |
| `202609140011` | `analysis.ReferencePopulationManifests`, `analysis.ReferencePopulationMembers` |
| `202609140012` | `hr.EvidenceDossiers`, `hr.DossierReviewActions` |
| `202609140013` | `faculty.AssistantContextVersions`, `faculty.AssistantRuns` |
| `202609140014` | `analysis.ArticleReviewWorkItems`, `analysis.ArticleReviewStageCheckpoints` ve review-run checkpoint alanları |
| `202609140015` | `analysis.ArticleEvaluationRuns` yetkilendirme alanları |
| `202609140016` | `analysis.CollectionChangeReceipts` |
| `202609140017` | `analysis.CanonicalArticleAnalysisRuns.SourceIdentityHash`; collector kimlikleri yeniden kullanıldığında eski ürünlerin güncel sayılmasını engeller |

`core.PublicationSummaries` bibliyografik normalizasyon çıktısıdır ve collector'da kalır; `analysis.ArticleSummaries` AI makale raporudur ve Analysis Service'e geçer. `core.CollectionChanges` normalize kaynak değişikliklerini kalıcı olarak bildirir; `analysis.CollectionChangeReceipts` Analysis Service'in idempotent tüketim durumunu saklar.

Migration kabulü:

- Collector yalnız `dbo.VersionInfo`, Analysis Service yalnız `dbo.ResearcherAnalysisVersionInfo` geçmişini kullanır. Salt analitik migration tipleri collector assembly'sinden çıkarılır; mevcut `dbo.VersionInfo` satırları korunur ve no-op/tombstone migration yığını eklenmez. Gemini ledger ve diğer analitik DDL'ler Analysis Service'e aittir.
- Collector ve Analysis migration zincirleri yalnızca kendi şemalarını doğrudan kurar. Geliştirme veritabanı şema değişikliklerinde sıfırlanıp yeniden oluşturulur; eski tablo benimseme veya veri taşıma yolu yoktur.
- Servisler arası foreign key oluşturulmaz. Analysis tablolarındaki `PersonelID`, `CanonicalWorkId`, `AcademicWorkId` gibi kaynak kimlikleri mantıksal referanstır; uygunluk Analysis Service'in salt okunur kaynak sorgularıyla doğrulanır. Analysis/hr/faculty içindeki kendi-servis foreign key ve unique/index kuralları korunur.
- Fresh veritabanında Analysis-first yalnız `analysis`, `hr`, `faculty` ve kendi history/receipt tablolarını; Collector-first yalnız collector şemaları, history ve değişiklik sinyalini oluşturur. Eşzamanlı başlangıç ortak SQL uygulama kilidiyle yarışmadan tamamlanır.
- Development collector temizliği yalnız collector tablolarını/history/sinyalini indirir; Analysis Service tablolarına ve `dbo.ResearcherAnalysisVersionInfo` geçmişine dokunmaz.

## Kod, worker ve HTTP sınırı

Collector'da yalnız `BulkCollectionWorker` kalır. `ArticleSummaryAutomationWorker`, `PublicationMetricsWorker`, `ArticleEvaluationWorker` ve `FacultyAssistantWorker` aynı varsayılan iş davranışlarıyla Analysis Service'e taşınır. Analysis Service, collector'ın kalıcı `CollectionChanges` kayıtlarını salt okunur olarak izler ve kendi receipt/kuyruk tablolarına idempotent planlama yazar; collector Analysis HTTP çağrısı veya AI sağlık proxy'si yapmaz.

Collector `http://localhost:5001` altında `Collect`, `GetResearcher`, `ListPublications`, `SavePublicationSelections`, `ListCanonicalPublications`, `Bulk/{Submit,Status,ImportSql}`, YÖKSİS, Semantic Scholar ve yalnız toplama sağlayıcılarının `ProviderStatus` işlemlerini tutar.

Analysis Service 22 ürün işlemini `http://localhost:5011/api/v1/...` altında konu bazlı yollarla sunar:

- Araştırmacı analizi: `/api/v1/researchers/analysis/generate`, `/api/v1/researchers/analysis`.
- Makale özeti/incelemesi: `/api/v1/articles/summary/generate`, `/api/v1/articles/summary`, `/api/v1/articles/analysis`, `/api/v1/articles/review/generate`.
- Metrik: `/api/v1/researchers/metrics`, `/api/v1/researchers/metrics/refresh`.
- Bilgi/grafik: `/api/v1/knowledge/search`, `/api/v1/knowledge/reference-population`, `/api/v1/knowledge/reference-population/import`, `/api/v1/knowledge/graph/export`.
- Değerlendirme: `/api/v1/evaluations/start`, `/api/v1/evaluations/status`.
- İK: `/api/v1/hr/dossiers/create`, `/api/v1/hr/dossiers`, `/api/v1/hr/dossiers/actions/append`, `/api/v1/hr/dossiers/actions`.
- Fakülte: `/api/v1/faculty/context/save`, `/api/v1/faculty/context`, `/api/v1/faculty/assistant/start`, `/api/v1/faculty/assistant/run`.

Yinelenen stateless generation uçları kaldırılmıştır; analiz motorları kalıcı ürün iş akışlarınca süreç içinde çağrılır. `/health` dışındaki Analysis API uçları `X-Analysis-Key` denetimini kullanır. Bilgi/grafik, değerlendirme, İK ve fakülte ürünleri buna ek olarak güvenilir kimlik/özne kapsamı denetimini uygular; servis anahtarı özne yetkisi yerine geçmez ve korunan ürünlerde `PersonelID` tek başına yetki değildir. Araştırmacı, makale ve metrik uyumluluk işlemleri mevcut kaynak ilişkisi kontrollerini korur. Analysis sağlayıcı durumu Analysis Service'te kalır; collector sağlayıcı durumu AI proxy çağrısı yapmaz.

## Çalışma ve veri akışı kabulü

1. Collector dış sağlayıcıları çağırır, provider/core tablolarını tek normalizasyon transaction'ında günceller ve aynı sahiplik sınırında kalıcı değişiklik sinyali yazar.
2. Analysis Service aynı veritabanındaki collector tablolarını özel salt okunur kaynak modelleriyle okur. Analiz kanıtı için gereken değişmez snapshot temsillerini kendi tablolarında saklar; collector tablolarını kopyalamaz, collector modellerini yazmaz ve bir ayna veritabanı üretmez.
3. Analysis worker'ları değişiklik sinyalini kendi receipt kaydıyla idempotent işler; özet, metrik ve diğer ürün durumlarını yalnız `analysis`, `hr` ve `faculty` tablolarına yazar.
4. Collector kapalıyken Analysis Service mevcut normalize kaynaklardan ürün okuma/işleme yapabilir. Collector şeması henüz yoksa Analysis Service başlar; sağlık, profil keşfi ve sağlayıcı tanısı çalışır, worker'lar kaynak hazır olana kadar hata üretmeden bekler.
5. Analysis Service kapalıyken collector toplama, normalize etme, kanonik listeleme, bulk ve sağlayıcı durumu işlemlerine devam eder; bir Analysis endpoint'ine veya sağlık proxy'sine bağımlı değildir.
6. Her iki servis aynı SQL veritabanı hedefini farklı, en-az-yetkili hesaplarla kullanabilir. Startup migration açıkken her hesap kendi şema/history nesneleri için gereken DDL yetkisini de alır; migration dışarıda uygulanıyorsa collector kendi tablolarında yazma, Analysis kendi tablolarında yazma ve collector kaynaklarında yalnız okuma yetkisiyle çalışır.

## Değişmez davranış ve doğrulama

- Taşıma, dış sağlayıcı toplama eşlemesini, kanonikleştirme kurallarını, yayın seçimini, prompt/sözleşme sürümlerini, model adlarını, thinking/timeout/token/maliyet varsayılanlarını veya AI fail-closed kullanım defteri davranışını değiştirmez.
- Korunan ürünlerin idempotency anahtarları, pagination ve erişim hata semantiği korunur. Birleşik okumalar açık nullable alanlarla kaydedilmemiş durumu bildirir; collector kaynak şeması veya gerekli kaynak satırı henüz hazır değilse Analysis Service açık `503` verir. `/health` dışındaki uçlarda `X-Analysis-Key` davranışı korunur.
- Fresh Collector-only, fresh Analysis-only, iki başlangıç sırası, eşzamanlı migration ve servislerden birinin offline olduğu senaryolar ayrı entegrasyon kontrolleriyle doğrulanır.
- Şema envanteri testinde her tablo tam bir DDL/yazma sahibine aittir; Analysis DbContext/modeli collector yazma operasyonu içermez, collector assembly/DI/API/worker ağacında analitik ürün tipi kalmaz.
- HTTP örnekleri collector ve Analysis klasörlerine yeni host/prefix'e göre ayrılır; değişkenler genişletildiğinde JSON geçerlidir, salt okuma ve ücretli/worker yazma ön koşulları açıktır. Dokümanlarda collector'ın analitik ürün veya AI proxy sahibi olduğu eski anlatım kalmaz.
- Model ve sonuç değişmezliği sentetik fixture'larla doğrulanır; hiçbir otomatik kontrol canlı veya ücretli sağlayıcı çağrısı yapmaz.
