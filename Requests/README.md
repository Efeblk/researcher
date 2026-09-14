# HTTP istek örnekleri

Bu klasördeki her `.http` dosyası Visual Studio veya REST Client ile bağımsız çalışır. Dosya başındaki örnek değişkenleri kendi eşleşen test kayıtlarınızla değiştirin; gerçek API anahtarı, T.C. kimlik numarası veya başka bir sır kaydetmeyin. Yanıt gövdeleri burada kopyalanmaz; güncel sözleşmeler `Modules/AcademicPerformance/Service/Api/V1/Contracts` altındadır.

## Normal test yolu

1. [`AcademicPerformance.http`](AcademicPerformance.http) ile uygulama sağlığını, [`ProviderStatus.http`](ProviderStatus.http) ile sağlayıcı durumunu denetleyin.
2. Bir yol seçin: tek araştırmacı için `AcademicPerformance.http` içindeki `Collect`, toplu iş için [`BulkCollection.http`](BulkCollection.http). Aynı kişiyi iki yoldan da göndermek gerekmez.
3. `AcademicPerformance.http` ile kaydedilmiş araştırmacıyı, yayın özetlerini ve kanonik yayınları okuyun.
4. [`PublicationMetrics.http`](PublicationMetrics.http) ile metrik durumunu, [`ArticleSummary.http`](ArticleSummary.http) ile otomatik özet kuyruğunu ve kaydedilmiş sonucu okuyun. Elle üretim çağrıları ayrıca işaretlenmiştir.
5. İK ve fakülte ürünü gerekiyorsa [`AcademicAiProducts.http`](AcademicAiProducts.http) dosyasına geçin. [`ArticleReview.http`](ArticleReview.http) isteğe bağlı destek akışıdır. Eski uyumluluk analizi [`ResearcherAnalysis.http`](ResearcherAnalysis.http), model değerlendirmesi [`ArticleEvaluation.http`](ArticleEvaluation.http), ileri bilgi/referans/grafik örnekleri [`AcademicKnowledge.http`](AcademicKnowledge.http) içinde ayrı tutulur. Yayın seçimi, Semantic Scholar ve YÖKSİS örnekleri `AcademicPerformance.http` sonunda bulunur.

| Kimlik | Nereden alınır | Nerede kullanılır |
| --- | --- | --- |
| `publicationId` | `ListPublications.Entities[].Id` | Yalnız `SavePublicationSelections.PublicationIds` |
| `canonicalWorkId` | `ListCanonicalPublications.Entities[].Id` | Özet otomasyonu, kanonik kanıt/inceleme, gerçek değerlendirme vakası, bilgi/grafik, İK ve fakülte ürünleri |
| `academicWorkId` | Aynı kanonik kaydın `Observations[].AcademicWorkId` alanı | Eski elle özet üretimi/okuması ve Semantic Scholar yayın/atıf filtreleri |

Tekil toplama çağrısı yanıt verince o kişinin toplama işi bitmiştir. Toplu akışta `Bulk/Status` yanıtındaki `Counts` araştırmacı toplama işlerini sayar; makale özetlerinin tamamlandığını göstermez. `IsComplete`, başarısız/kısmi/reddedilmiş son durumları da tamamlanmış sayar; bu nedenle `Counts` ve `Jobs` birlikte incelenmelidir. Örneğin 10 kişi, 10 makale veya 10 özet anlamına gelmez; her kişinin yayın sayısı farklıdır.

`ArticleSummaryAutomation:Enabled=true` ve `WorkerEnabled=false` olduğunda toplama, görünür özet kuyruk kayıtlarını oluşturmaya devam eder; yeni kaynak indirme ve AI çalışması başlamaz. Ayar, başlamış bir denemeyi iptal etmez. `WorkerEnabled=true` yapıldığında bekleyen işler işlenir. Collector (`http://localhost:5001`) ve analiz servisi (`http://localhost:5011`) ayrı hostlardır. Collector'daki `ConnectionStrings:AcademicDatabase` ile analiz servisindeki `ConnectionStrings:UsageDatabase` aynı SQL Server veritabanını göstermelidir. İstemciler yalnız collector V1 endpoint'lerini çağırır, analiz servisine doğrudan istek göndermez.

`ArticleEvaluation.http`, `AcademicKnowledge.http` ve `AcademicAiProducts.http` güvenilir bir ürün erişim adaptörü ister. Adaptörün BYS olması zorunlu değildir; kimlik bilgisinin biçimini kurulu adaptör belirler. Varsayılan kurulumda anonim istek `401`, kimliği doğrulanmış istek ise adaptör yapılandırılmadığı için `503` alır. Yetki reddi, kayıt varlığını açığa çıkarmamak için `404` döner.

`clientRequestId`, aynı yetkili aktör + `PersonelID` kapsamında aynı istek gövdesiyle tekrarlandığında idempotent yeniden kullanım sağlar; farklı girdi için yeni UUID üretin. Fakülte bağlamı güncellemesinde `expectedVersion`, son okunan `Version` olmalıdır. `forceRegeneration=true` ve açık yenileme çağrıları yeni sürüm/çalışma üretebilir; önce ilgili dosyadaki yorumları okuyun.
