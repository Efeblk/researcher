# Kod rehberi

Uygulama iki bağımsız çalıştırılabilir projeden oluşur: Serenity tabanlı collector ve `ResearcherAnalysisService`. Ortak AI sözleşmeleri `ResearcherAnalysis.Contracts/` altındadır. İki servis aynı SQL Server veritabanını kullanır, fakat kendi migration grubunu ve sürüm geçmişini başlangıçta ayrı uygular; herhangi biri önce veya ikisi eşzamanlı başlatılabilir.

## Klasörler ve sınırlar

```text
Program.cs                                  collector host, DI ve migration başlangıcı
Host/                                       yalnız bu hosta ait servisler
Modules/AcademicPerformance/
  Service/Api/V1/{Contracts,Endpoints}/     dış client sözleşmesi ve HTTP uçları
  Service/Application/                     kullanım senaryoları ve DTO eşleme
  Service/Researchers/                      model, toplama ve kalıcılık
  Service/Works/                            ortak yayın modeli ve tekilleştirme
  Service/Integrations/<Provider>/          sağlayıcı istemcileri ve ham modeller
  Service/Integrations/RateLimiting/        SQL tabanlı hız/kota koordinasyonu
  Service/{Bulk,ArticleSummaries,Analysis}/ toplu işler ve analiz iş akışları
  Service/Data/Migrations/{Core,Providers}/ FluentMigrator değişiklikleri
  WebClient/                                Serenity/Razor arayüzü ve UI adapter'ları
  Background/                               kalıcı toplu kuyruk worker'ı
AcademicCollectorDemo.Tests/                Unit, Integration, Infrastructure
ResearcherAnalysisService/
  Program.cs                                analysis host, DI ve migration başlangıcı
  Api/V1/                                   doğrudan, tam bağlamlı AI HTTP yüzeyi
  Data/Migrations/                          Analysis Service kullanım defteri migration'ları
  Requests/                                 doğrudan analysis HTTP örnekleri
Requests/AcademicCollector/                 collector HTTP örnekleri
```

Bağımlılık yönü `WebClient veya Api/V1/Endpoints → Application → Researchers/Works/Integrations → Data` şeklindedir. Dış client'lar yalnız V1 sözleşmelerini kullanmalı; EF entity'leri, sağlayıcı DTO'ları ve WebClient endpoint'leri dış sözleşmeye çıkarılmamalıdır. Sağlayıcıya özgü tipleri entegrasyon klasöründe tutun.

## Toplama ve veri katmanları

`AcademicPerformanceEndpoint`, isteği uygulama servisine iletir. `ResearcherCollectionHandler` kimlikleri doğrular, araştırmacıyı bulur, sağlayıcıları çağırır ve tek transaction içinde araştırmacıyı kaydedip ortak eserleri ve yayın özetlerini eşitler. YÖKSİS ayrı endpoint ve SOAP akışına sahiptir.

| Katman | Amaç |
| --- | --- |
| Sağlayıcı profil/eser tabloları | Ham alanları, yanıtları ve kaynağı korur; esas sağlayıcı kaydıdır. |
| `core.AcademicWorks` / `AcademicWorkSources` | Sağlayıcı eserlerini ortak biçime ve kaynak ilişkisine taşır. |
| `core.PublicationSummaries` | DOI; yoksa normalize başlık-yıl ile tekilleştirilmiş listeyi sunar. |
| `core.PublicationDisplayApprovals` | Akademisyenin okul sitesinde gösterim seçimini saklar. |
| Collector'ın `analysis.*` tabloları | Araştırmacı analizleri, makale özetleri, kanıtlar, değerlendirmeler ve kalıcı ürünleri saklar. |
| Analysis Service `analysis.GeminiUsageAttempts` | Ücretli Gemini denemelerinin durum ve maliyet defterini saklar. |
| `bulk.*` / `integrations.*` | Kuyruk ile sağlayıcı hız, kota ve durum koordinasyonunu saklar. |

Sağlayıcı metrikleri kolay raporlama için `core.Researchers` üzerinde nullable kolonlara da yansıtılır; sağlayıcı tabloları esas kaynaktır ve farklı sağlayıcıların metrikleri birleştirilmez. `PersonelID`, araştırmacının kurum anahtarıdır.

## Kanonik veri ve ürün yüzeyleri

Toplama, yayın özetlerini güncellemeden önce sağlayıcı eserlerini `core.CanonicalWorks`, araştırmacı üyelikleri ve kaynak gözlemleriyle uzlaştırır. Kanonik transaction bağımlı özet ve metrik işlerini planlar. `analysis.*` ayrıca değişmez makale kanıtlarını, uzman incelemelerini, değerlendirme denemelerini, metrik snapshot'larını, referans popülasyonu manifestlerini, İK dosyalarını ve fakülte asistanı çalıştırmalarını saklar. SQL Server esas kaynaktır; grafik dışa aktarımı ikinci bir production veritabanı değil yeniden üretilebilir bir kanıt paketidir.

Desteklenen collector işlemleri `/Services/AcademicPerformance/V1/[action]` yolunu kullanır. Toplama işlemlerine ek olarak `ListCanonicalPublications`, `RebuildCanonicalPublications`, `GetResearcherPublicationMetrics`, `RefreshResearcherPublicationMetrics`, `SearchAcademicEvidence`, `GetReferencePopulation`, `ImportReferencePopulation` ve `ExportAcademicEvidenceGraph` ile [analiz hattındaki](ANALYSIS_PIPELINE.md) makale ve fakülte işlemleri bulunur. Ürün uçları özne verisini yüklemeden önce `IAcademicProductAccessService.AuthorizeAsync` çağırmalı ve yetkilendirilen özne kimliğini servise aktarmalıdır. Varsayılan uygulama kapalı kalır; consuming host güvenilir kimlik/kapsam uygulaması sağlamalıdır.

Analysis Service `/api/v1/` altında bağımsız bir service-to-service yüzey sunar. Bu uçlar `PersonelID` üzerinden SQL'den kaynak içerik çözmez; gerekli araştırmacı snapshot'ı, sayfalar, deterministik span'lar veya kanıt kataloğu istekte taşınır. Erişim `X-Analysis-Key` ile korunur. Collector kalıcı ürünleri orkestre ederken bu yüzeyi çağırabilir, ancak iki servis ayrı ayrı run ve publish edilebilir. Sağlayıcı DTO'larını, EF entity'lerini, yetkilendirme actor'larını ve İK alanlarını genel grafik/dışa aktarım sözleşmelerinden uzak tutun.

Kanonik kimlik için `Service/Works/{Models,Processing,Persistence}`, kanıt akışları için `Service/ArticleSummaries` ve `Service/ArticleReviews`, değerlendirme için `Service/Evaluations`, snapshot'lar için `Service/Metrics`, arama/dışa aktarım için `Service/Knowledge` ve `Service/GraphProjection`, İK kanıtı için `Service/HrDossiers`, asistan çalıştırmaları için `Service/FacultyAssistant`, yetkilendirme için `Service/ProductAccess` klasörlerinden başlayın. Ayrıntılı sözleşme ve sınırlar [kanonik veri](CANONICAL_ACADEMIC_DATA.md), [AI ürünleri](ACADEMIC_AI_PRODUCTS.md), [yayın metrikleri](PUBLICATION_METRICS.md) ve [veri/bilgi katmanı](DATA_KNOWLEDGE_LAYER.md) belgelerindedir.

## Değişiklik noktaları

| İhtiyaç | Başlangıç dosyası/klasörü |
| --- | --- |
| Kimlik ayrıştırma | `Service/Researchers/Collection/ResearcherIdentifierParser.cs` |
| Sağlayıcı HTTP/parsing | `Service/Integrations/<Provider>/` |
| YÖKSİS operasyonları | `Service/Integrations/Yoksis/Collection/YoksisOperationCatalog.cs` |
| Yayın sınıflandırma/tekilleştirme | `Service/Works/Processing/` |
| Dış API alanı | `Service/Api/V1/Contracts/` ve `AcademicPerformanceDtoMapper.cs` |
| Web formu/paneller/grid | `WebClient/Pages/AcademicPerformance/` ve `WebClient/Publications/` |
| Collector veritabanı | `Service/Data/Migrations/Core/` veya `Providers/` altında yeni migration; ayrıca EF modeli |
| Analysis kullanım defteri | `ResearcherAnalysisService/Data/Migrations/` altında yeni migration |

Migration sırasını her servisin kendi geçmişindeki benzersiz `[Migration(...)]` numarası belirler. Collector `dbo.VersionInfo`, Analysis Service `dbo.ResearcherAnalysisVersionInfo` kullanır. `Up()` ve bağımlılık sırasını gözeten `Down()` yazın, tablo adlarını şemayla niteleyin, tablo sahipliğini servisler arasında karıştırmayın ve uygulanmış migration'ları değiştirmeyin. Eski `PersonelID` öncesi veritabanları için ayrıca geçiş planı gerekir.

Doğrulama komutları ve PR akışı [katkı rehberindedir](../CONTRIBUTING.md). Entegrasyon testleri izole SQL Server veritabanı ve sentetik sağlayıcı yanıtları kullanır. Üretilmiş `wwwroot/esm/` dosyaları yerine TypeScript kaynaklarını düzenleyin. Toplu akış için [toplu toplama](BULK_COLLECTION.md), AI akışları için [analiz hattı](ANALYSIS_PIPELINE.md), dış servis davranışı için [sağlayıcılar](PROVIDERS.md) belgesine bakın.
