# Kod rehberi

Veritabanındaki 63 fiziksel tablonun servis, şema ve veri yaşam döngüsü ayrımı için
[veritabanı rehberine](DATABASE_GUIDE.md) bakın.

Uygulama iki bağımsız .NET 10 projesinden oluşur. Academic Collector dış sağlayıcılardan veri toplar, normalize eder ve kanonik çekirdeği yönetir. `ResearcherAnalysisService` bütün analitik ürünlerin HTTP, kalıcılık ve worker sahibidir. Servisler aynı SQL Server veritabanını kullanır, fakat kendi migration grubunu ve sürüm geçmişini başlangıçta ayrı uygular; herhangi biri önce veya ikisi eşzamanlı başlatılabilir.

## Klasörler ve sınırlar

```text
Program.cs                                      collector host, DI ve migration başlangıcı
Host/                                           yalnız collector host servisleri
Modules/AcademicPerformance/
  Service/Api/V1/{Contracts,Endpoints}/         toplama, okuma, kanonik, bulk ve provider HTTP yüzeyi
  Service/Application/                         collector kullanım senaryoları ve DTO eşleme
  Service/Researchers/                          model, toplama ve kalıcılık
  Service/Works/                                ortak yayın modeli, normalizasyon ve kanonikleştirme
  Service/Integrations/<Provider>/              sağlayıcı istemcileri ve ham modeller
  Service/Integrations/RateLimiting/            SQL tabanlı hız/kota koordinasyonu
  Service/Bulk/                                 kuyruk işleme ve yapılandırılabilir SQL importu
  Service/Data/Migrations/{Core,Providers}/     yalnız collector DDL'i; dbo.VersionInfo
  WebClient/                                    Serenity/Razor arayüzü ve UI adapter'ları
  Background/BulkCollectionWorker.cs            collector'ın tek kalıcı worker'ı
ResearcherAnalysisService/
  Program.cs                                    analysis host, DI ve migration başlangıcı
  Api/V1/                                       sağlayıcı tanısı ve profil keşfi controller'ları
  Products/Api/                                 ürün sözleşmeleri ve controller'lar
  Products/                                     analiz/özet/inceleme/metrik/bilgi/İK/fakülte akışları
  Products/Data/                                AnalysisDbContext ve yazılabilir product entity'leri
  SourceData/                                   aynı DB'deki collector tablolarının özel salt okunur modelleri
  Background/                                   summary, metrics, evaluation ve faculty worker'ları
  Data/Migrations/                              analysis/hr/faculty DDL'i; dbo.ResearcherAnalysisVersionInfo
  Requests/                                     Analysis HTTP örnekleri
ResearcherAnalysis.Contracts/                   paylaşılan analiz motoru sözleşmeleri
Requests/AcademicCollector/                     yalnız collector HTTP örnekleri
```

Collector bağımlılık yönü `WebClient veya Api/V1/Endpoints → Application → Researchers/Works/Integrations → Data` biçimindedir. Analysis kalıcı ürünleri `Products/Api → Products workflows → Products/Data + SourceData` yönünü izler. Sağlayıcı DTO'larını, EF entity'lerini, yetkilendirme actor'larını veya İK alanlarını dış/grafik sözleşmelerine taşımayın.

## Toplama, analiz ve tablo sahipliği

`AcademicPerformanceEndpoint`, isteği collector uygulama servisine iletir. `ResearcherCollectionHandler` kimlikleri doğrular, sağlayıcıları çağırır ve bir transaction içinde araştırmacıyı, normalize eserleri, bibliyografik özetleri ve kanonik ilişkileri eşitler. Aynı transaction `core.CollectionChanges` sinyalini yazar. YÖKSİS ayrı endpoint ve SOAP akışına sahiptir; bulk worker aynı toplama uygulama servisini kullanır.

| Sahip | Nesneler ve amaç |
| --- | --- |
| Collector | `core.Researchers`, normalize eserler, `core.PublicationSummaries`, yayın seçimi, kanonik kimlik/gözlemler/üyelikler ve `core.CollectionChanges` |
| Collector | ORCID, OpenAlex, Google Scholar, WOS, YÖKSİS, TR Dizin, Crossref ve Semantic Scholar ham/normalize sağlayıcı tabloları |
| Collector | `bulk.*` kuyrukları ve `integrations.*` hız/kota/durum kayıtları |
| Analysis Service | `analysis.*` araştırmacı/makale analizleri, immutable kanıt snapshot'ları, incelemeler, metrikler, değerlendirmeler, referans popülasyonları, kullanım ledger'ı ve `CollectionChangeReceipts` |
| Analysis Service | `hr.*` kanıt dosyaları ve review action'ları; `faculty.*` bağlam ve asistan run'ları |

Canonical work identity gives every valid DOI global priority. DOIless records merge across providers only within the same `PersonelID` when normalized exact titles, known years, and equal-cardinality compatible author lists agree. Author comparison permits order, punctuation, and full-name/initial variants, but missing, all-initial, `et al.`, placeholder, or truncated author text supplies no match evidence. Provider comma-delimited author output is treated as a list, so ambiguous `Surname, Given` strings may remain separate. A non-clique compatibility component or one containing multiple valid DOIs leaves every DOIless member source-scoped. After identity-rule changes, reset the disposable development database and recollect; no legacy backfill is maintained.

`core.PublicationSummaries` bibliyografik normalizasyon çıktısıdır; AI özeti `analysis.ArticleSummaries` tablosudur. Her tablo tam bir DDL/yazma sahibine sahiptir. Servisler arası foreign key yoktur. Analysis tablolarındaki `PersonelID`, `CanonicalWorkId` ve `AcademicWorkId` mantıksal kaynak kimlikleridir; Analysis güncel ilişki ve uygunluğu aynı veritabanındaki salt okunur kaynak modelleriyle denetler. `AnalysisDbContext.SaveChanges` bu modellere yazmayı reddeder. Gerekli kanıtı kendi tablolarında immutable snapshot olarak tutar; collector tablolarını veya veritabanını aynalamaz.

Collector migration'ları `dbo.VersionInfo`, Analysis migration'ları `dbo.ResearcherAnalysisVersionInfo` kullanır. Fresh Analysis-first yalnız Analysis nesnelerini, fresh Collector-first yalnız collector nesnelerini kurar; şema değişikliklerinde geliştirme veritabanı sıfırlanıp yeniden oluşturulur. Tam envanter ve başlangıç sırası kabul matrisi [servis ayrımı planındadır](SERVICE_SEPARATION_PLAN.md).

## HTTP ve worker yüzeyleri

Collector `http://localhost:5001/Services/AcademicPerformance/V1/[action]` altında şu sorumlulukları tutar:

- `Collect`, `RecalculateMetrics`, `GetResearcher`, `ListPublications`, `SavePublicationSelections`;
- `ListCanonicalPublications`;
- `Bulk/{Submit,Status,ImportSql}`;
- YÖKSİS, Semantic Scholar ve toplama sağlayıcısı `ProviderStatus` işlemleri.

Analysis Service kalıcı ürünleri `http://localhost:5011/api/v1/...` altında sunar. Bunlar araştırmacı analizi, makale özeti/kanıtı/incelemesi, metrik, kanıt araması, referans popülasyonu, grafik dışa aktarımı, model değerlendirmesi, İK kanıt dosyası ve fakülte asistanıdır. Bilgi/grafik, değerlendirme, İK ve fakülte ürünlerinde `IAcademicProductAccessService` veri okunmadan önce özneyi yetkilendirir. Varsayılan uygulama kapalıdır; deployment güvenilir kimlik ve kapsam adaptörü sağlamalıdır. Araştırmacı, makale ve metrik uyumluluk işlemleri mevcut kaynak ilişkisi kontrollerini korur. Kaynak şeması veya satırı henüz hazır değilse bağımlı ürün açık `503` verir.

Yinelenen tam bağlamlı HTTP üretim uçları kaldırılmıştır; ürün iş akışları analiz motorlarını süreç içinde çağırır. Collector Analysis HTTP çağrısı veya AI sağlık proxy'si yapmaz.

Collector'da yalnız `BulkCollectionWorker` bulunur. Analysis Service'teki `ArticleSummaryAutomationWorker`, `PublicationMetricsWorker`, `ArticleEvaluationWorker` ve `FacultyAssistantWorker`, collector'ın `core.CollectionChanges` sinyalini kendi `analysis.CollectionChangeReceipts` kaydıyla idempotent tüketir. Servislerden biri offline iken diğeri kendi alanında çalışmaya devam eder.

## Değişiklik noktaları

| İhtiyaç | Başlangıç dosyası/klasörü |
| --- | --- |
| Kimlik ayrıştırma ve dış toplama | `Modules/AcademicPerformance/Service/Researchers/Collection/` |
| Sağlayıcı HTTP/parsing | `Modules/AcademicPerformance/Service/Integrations/<Provider>/` |
| Yayın sınıflandırma/kanonikleştirme | `Modules/AcademicPerformance/Service/Works/` |
| Collector dış API alanı | `Modules/AcademicPerformance/Service/Api/V1/` |
| Collector veritabanı | `Modules/AcademicPerformance/Service/Data/Migrations/{Core,Providers}/` |
| Kalıcı analiz API/iş akışı | `ResearcherAnalysisService/Products/Api/` ve ilgili `Products/` klasörü |
| Salt okunur collector kaynak eşlemesi | `ResearcherAnalysisService/SourceData/` |
| Analysis veritabanı | `ResearcherAnalysisService/Data/Migrations/` ve `Products/Data/` |
| Analiz motoru/model adaptörü ve tanı | `ResearcherAnalysisService/Analysis/`, `Integrations/`, `Api/V1/` |
| Web formu/paneller/grid | `Modules/AcademicPerformance/WebClient/` |

Doğrulama komutları ve PR akışı [katkı rehberindedir](../CONTRIBUTING.md). Entegrasyon testleri izole SQL Server veritabanı ve sentetik sağlayıcı/model yanıtları kullanır; canlı veya ücretli çağrı yapmaz. Toplu akış için [toplu toplama](BULK_COLLECTION.md), analiz akışları için [analiz hattı](ANALYSIS_PIPELINE.md), dış servis davranışı için [sağlayıcılar](PROVIDERS.md) belgesine bakın.
