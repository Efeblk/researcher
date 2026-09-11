# Semantic Scholar DOI zenginleştirmesi

Semantic Scholar entegrasyonu, personele ait mevcut `AcademicWorks` satırlarında bulunan DOI'leri Graph API ile zenginleştirir. Yazar adıyla eser aramaz ve atıf yapan makaleleri araştırmacının kendi eserleri arasına eklemez. Bu nedenle yalnızca YÖKSİS'ten gelmiş olsa bile `AcademicWorks` içinde DOI'si bulunan bir eser, açık toplama ucu çağrılarak zenginleştirilebilir.

Makale verisi DOI düzeyinde ortak önbellekte tutulur ve aynı DOI başka bir personelin eserinde bulunduğunda yeni HTTP isteği yapılmadan yeniden kullanılır. S2 makale kimliği, başlık, özet, yazarlar, yayın alanları, açık erişim PDF bilgisi, sayaçlar, TLDR ve metin erişilebilirliği ham sağlayıcı kaydıyla birlikte saklanır. Açık erişim PDF bağlantısı aynı DOI'ye sahip mevcut eserlerin `AcademicWorkSources` satırlarına eklenir. S2 atıf sayaçları sağlayıcı kaydında kalır; eserin kendi sağlayıcısına ait `CitedByCount` alanının anlamını değiştirmez.

Semantic Scholar'ın `tldr` alanı sağlayıcının sunduğu kısa açıklamadır. Uygulamanın yerel analiz servisiyle ürettiği ve ayrıca sakladığı AI raporu değildir.

## API uçları

Üç uç da `POST` kullanır ve kayıt sahipliğini `PersonelID` üzerinden sınırlar:

| Uç | Amaç |
| --- | --- |
| `/Services/AcademicPerformance/V1/CollectSemanticScholar` | Personelin mevcut DOI'lerini önbellekten veya Semantic Scholar'dan işler. Yanıtta `HasPendingWork=true` ise aynı istek yeniden gönderilerek kalan iş sürdürülür. |
| `/Services/AcademicPerformance/V1/ListSemanticScholarPapers` | `PersonelID`, isteğe bağlı `AcademicWorkId`, `Skip` ve `Take` ile kaydedilmiş makale metaverisini listeler. |
| `/Services/AcademicPerformance/V1/ListSemanticScholarCitations` | Personele ait bir `AcademicWorkId` için kaydedilmiş atıf ilişkilerini, bağlamları ve niyetleri sayfalı döndürür. |

`HasPendingWork`, bu çalıştırmanın güvenli sınır içinde tamamlanamadığını bildirir. Özellikle istek kotası veya sayfa ortası 429 yanıtından sonra açık toplama ucu tekrar çağrılmalıdır. `CitationsComplete=false` tek başına bekleyen iş anlamına gelmez: `CitationNextOffset`, ayarlanmış `MaximumCitationsPerPaper` üst sınırına ulaştıysa sonuç bilinçli olarak kırpılmıştır ve sonraki çağrı aynı sınırla daha fazla atıf çekmez. Tekilleştirilen veya kimliği eksik kayıtlar nedeniyle saklanan ilişki sayısı taranan kayıt sayısından düşük olabilir.

## Sınırlar ve önbellek

Varsayılanlar `academicsettings.json` içinde ayarlanabilir:

| Ayar | Varsayılan | Anlamı |
| --- | ---: | --- |
| `ProviderRequestLimits:SemanticScholar:MinimumIntervalMilliseconds` | 1000 | Uygulama genelinde Semantic Scholar HTTP istekleri arasındaki asgari süre. |
| `SemanticScholar:MaximumPapersPerRun` | 10 | Tek çalıştırmada işlenecek en fazla DOI. |
| `SemanticScholar:MaximumCitationsPerPaper` | 500 | Makale başına taranacak atıf listesi için ofset sınırı; saklanan benzersiz ilişki sayısından farklı olabilir. |
| `SemanticScholar:CitationPageSize` | 100 | Atıf sayfası başına istenen kayıt sayısı. |
| `SemanticScholar:CacheMaxAgeHours` | 720 | Olumlu ve 404 negatif sonuçların yeniden kullanılacağı süre. |

`SemanticScholar:ApiKey` isteğe bağlıdır ve kullanılacaksa `dotnet user-secrets` veya güvenli production yapılandırmasıyla verilmelidir. Anonim kullanım desteklenir. Provider Status denetimi de yapılandırılmışsa aynı anahtarı `x-api-key` başlığında kullanır; anahtarı yanıta veya log mesajına yazmaz.

Toplu toplamada yerel hız sınırlayıcının ertelemesi, işi başarısız saymak yerine sonraki kuyruk denemesine bırakır. Atıf sayfası ortasında 429 veya geçici aktarım hatası oluşursa sonraki çağrı kaydedilmiş ofsetten sürer. Her sayfa bir işlem içinde eklenir veya güncellenir; eski nesilde kalmış ilişkiler yalnızca son sayfa başarıyla tamamlanınca silinir. Ara sonuçlar eski ve yeni kayıtları birlikte içerebilir. `CitationsRefreshing` yenilemenin sürdüğünü, `CitationsFetched` mevcut yenileme neslinde alınan sayıyı, atıf listeleme yanıtındaki `StoredCitationCount` ise o anda saklanan toplam ilişki sayısını gösterir.

`contextsWithIntent` içindeki niyet yalnızca kendi bağlamıyla ilişkilendirilir. Entegrasyon bu veriden destekleyici veya çelişen atıf sonucu türetmez.
