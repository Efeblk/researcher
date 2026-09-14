# Researcher Analysis Service HTTP örnekleri

Bu klasördeki örneklerin tamamı bağımsız `ResearcherAnalysisService` uygulamasını `http://localhost:5011` üzerinden çağırır. İki farklı sözleşme yüzeyi vardır:

- Kalıcı ürün uçları `/api/v1/products/[action]` altında çalışır. Analysis Service aynı SQL veritabanındaki collector kaynaklarını özel salt okunur kaynak modelleriyle çözer, erişimi denetler ve çıktıları yalnız `analysis`, `hr` veya `faculty` tablolarına yazar.
- Stateless uçlar `/api/v1/*` altında gerekli araştırmacı snapshot'ını, makale sayfalarını/span'larını, değerlendirme parmak izini veya fakülte kanıt kataloğunu istek gövdesinde alır; `PersonelID` tek başına kaynak bağlamı sağlamaz.

## Kalıcı ürün örnekleri

- [ResearcherProducts.http](ResearcherProducts.http): kaynak kapsamı, kaydedilmiş araştırmacı analizi ve yeni analiz üretimi.
- [ArticleSummary.http](ArticleSummary.http): otomasyon durumu, kanonik kanıt, kaydedilmiş özet ve özet üretimi.
- [ArticleReview.http](ArticleReview.http): kaydedilmiş uzman incelemesi ve yeni staged inceleme.
- [PublicationMetrics.http](PublicationMetrics.http): kaydedilmiş metrik snapshot'ı ve yenileme kuyruğu.
- [AcademicKnowledge.http](AcademicKnowledge.http): kanıt araması, referans popülasyonu ve grafik dışa aktarımı.
- [ArticleEvaluation.http](ArticleEvaluation.http): kalıcı model değerlendirme kuyruğu ve sayfalı sonuç.
- [AcademicAiProducts.http](AcademicAiProducts.http): İK kanıt dosyası ve fakülte asistanı akışları.

Her kalıcı ürün isteği `@analysisKey` ile gerçek `X-Analysis-Key` servis erişim denetimini gösterir. `AcademicKnowledge.http`, `ArticleEvaluation.http` ve `AcademicAiProducts.http` işlemleri buna ek olarak güvenilir bir ürün erişim adaptörü gerektirir. Anahtar özne yetkisi yerine geçmez; adaptörün cookie/header biçimini deployment belirler. Varsayılan kapalı adaptörde anonim istek `401`, kimliği doğrulanmış fakat eşleme yapılandırılmamış istek `503`, yetki reddi ise kayıt varlığını açığa çıkarmayan `404` alır. Diğer kalıcı işlemler mevcut `PersonelID`/kaynak ilişkisi kontrollerini korur ancak ürün adaptörü kullanmaz. Collector kaynak şeması veya gerekli kaynak satırı henüz hazır değilse bağımlı ürün işlemi açık `503` dönebilir. Okuma olarak işaretlenen istekler model çağrısı yapmaz; üretim ve worker çağrılarının yorumlarında maliyet ve ön koşullar belirtilmiştir.

`clientRequestId`, aynı yetkili aktör ve `PersonelID` kapsamında aynı gövdeyle tekrarlandığında idempotent yeniden kullanım sağlar; farklı girdi için yeni UUID üretin. Fakülte bağlamı güncellemesinde `expectedVersion`, son okunan `Version` olmalıdır. `canonicalWorkId` ve `academicWorkId` değerlerini collector'ın [AcademicPerformance.http](../../Requests/AcademicCollector/AcademicPerformance.http) örneğindeki kanonik listeden alın.

## Stateless örnekler

- [ResearcherAnalysis.http](ResearcherAnalysis.http): sağlık, Analysis sağlayıcı durumu ve tam araştırmacı snapshot'ı analizi.
- [ArticleAnalysis.http](ArticleAnalysis.http): makale özeti, tam uzman incelemesi ve ayrı quote/dispatch aşamaları.
- [EvaluationAndFaculty.http](EvaluationAndFaculty.http): profil tabanlı kalibrasyon ve tam kanıt kataloglu fakülte asistanı.

`@analysisKey` değerini `Service:ApiKey` ile aynı geliştirme sırrıyla değiştirin. `/health` dışındaki Analysis API uçları `Authorization: Bearer` yerine `X-Analysis-Key` kullanır. Üretim etiketleri kayıtlı varsayılanlara göre yerel Ollama ile hosted Gemini çağrılarını ayırır; ayarları değiştirdiyseniz gerçek hedefi ayrıca denetleyin. Ücretli istekleri yalnız amaçlı çalıştırın; bu dosyalar test sırasında otomatik çalıştırılmaz.

Adlandırılmış yanıt değişkeni kullanan staged istekleri yukarıdan aşağı çalıştırın. HTTP istemciniz `{{requestName.response.body.$...}}` ifadesini desteklemiyorsa önceki yanıttaki değeri elle yapıştırın. Her dispatch için yeni bir `attemptId` üretin.
