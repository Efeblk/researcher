# Academic Collector HTTP örnekleri

Bu klasördeki her `.http` dosyası Visual Studio veya REST Client ile bağımsız çalışır ve collector'ın `http://localhost:5001` yüzeyini kullanır. Örnek değişkenleri kendi test kayıtlarınızla değiştirin; gerçek API anahtarı, T.C. kimlik numarası veya başka bir sırrı repoya kaydetmeyin. Güncel collector sözleşmeleri `Modules/AcademicPerformance/Service/Api/V1/Contracts` altındadır.

## Normal test yolu

1. [AcademicPerformance.http](AcademicPerformance.http) ile uygulama sağlığını, [ProviderStatus.http](ProviderStatus.http) ile yalnız toplama sağlayıcılarının durumunu denetleyin.
2. Tek araştırmacı için `AcademicPerformance.http` içindeki `Collect` işlemini veya toplu iş için [BulkCollection.http](BulkCollection.http) dosyasını kullanın.
3. `AcademicPerformance.http` ile kaydedilmiş araştırmacıyı, bibliyografik yayın özetlerini ve kanonik yayınları okuyun. Yayın seçimi, Semantic Scholar ve YÖKSİS örnekleri de bu dosyadadır.

| Kimlik | Nereden alınır | Nerede kullanılır |
| --- | --- | --- |
| `publicationId` | `ListPublications.Entities[].Id` | Yalnız `SavePublicationSelections.PublicationIds` |
| `canonicalWorkId` | `ListCanonicalPublications.Entities[].Id` | Analysis Service'in özet, inceleme, değerlendirme, bilgi, İK ve fakülte ürünleri |
| `academicWorkId` | Aynı kanonik kaydın `Observations[].AcademicWorkId` alanı | Semantic Scholar yayın/atıf filtreleri ve Analysis Service'in gözlem tabanlı özet ürünleri |

Tekil toplama çağrısı yanıt verince o kişinin toplama işi bitmiştir. Toplu akışta `Bulk/Status` yanıtındaki `Counts` araştırmacı toplama işlerini sayar; Analysis Service işlerinin tamamlandığını göstermez. `IsComplete`, başarısız, kısmi veya reddedilmiş son durumları da tamamlanmış sayabilir; `Counts` ve `Jobs` alanlarını birlikte inceleyin.

Collector AI üretimi, analitik ürün kaydı veya analitik worker çalıştırmaz. Toplama ve normalizasyon aynı SQL veritabanına `core.CollectionChanges` sinyali yazar; Analysis Service kendi receipt kaydıyla bu sinyali bağımsız işler. Kalıcı analiz ürünlerinin 5011 örnekleri [Researcher Analysis Service Requests](../../ResearcherAnalysisService/Requests/README.md) altındadır. Collector kapalıyken mevcut normalize veri üzerindeki Analysis ürünleri; Analysis Service kapalıyken collector toplama, bulk, kanonik okuma ve sağlayıcı durumu çalışmaya devam eder.
