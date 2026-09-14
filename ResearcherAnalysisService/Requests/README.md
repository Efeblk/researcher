# Researcher Analysis Service HTTP örnekleri

Bu örnekler `ResearcherAnalysisService` uygulamasını doğrudan `http://localhost:5011` üzerinden çağırır. Collector kaydı veya `PersonelID` ile veri aramazlar: üretim istekleri yayın, sayfa, deterministik kaynak span'ı, değerlendirme parmak izi veya fakülte kanıtı gibi gerekli bağlamın tamamını gövdede taşır.

- [ResearcherAnalysis.http](ResearcherAnalysis.http): sağlık, sağlayıcı durumu ve tam araştırmacı snapshot'ı analizi.
- [ArticleAnalysis.http](ArticleAnalysis.http): makale özeti, tam uzman incelemesi ve ayrı quote/dispatch inceleme aşamaları.
- [EvaluationAndFaculty.http](EvaluationAndFaculty.http): profil tabanlı kalibrasyon ve tam kanıt kataloglu fakülte asistanı.

`@analysisKey` değerini `Service:ApiKey` ile aynı gerçek geliştirme sırrıyla değiştirin. Kimlik doğrulama `Authorization: Bearer` kullanmaz; korunan bütün uçlar `X-Analysis-Key` ister. Development'ta anahtar yapılandırılmamış yerel loopback isteklerine izin verilse de örnekler production ile aynı header sözleşmesini gösterir.

`[okuma]` istekleri model üretimi yapmaz. Üretim etiketleri kayıtlı varsayılanlara göre yerel Ollama ile hosted Gemini çağrılarını ayırır; sağlayıcı ayarlarını değiştirdiyseniz gerçek hedefi ayrıca denetleyin. Ücretli olarak işaretlenen istekleri yalnız amaçlı olarak çalıştırın. Bu dosyalar otomatik veya test sırasında çalıştırılmaz.

Adlandırılmış isteklerden yanıt değişkeni kullanan aşamalarda dosyayı yukarıdan aşağı çalıştırın. HTTP istemciniz `{{requestName.response.body.$...}}` ifadesini desteklemiyorsa önceki yanıttaki değeri aynı alana elle yapıştırın. Her dispatch için yeni bir `attemptId` üretin.
