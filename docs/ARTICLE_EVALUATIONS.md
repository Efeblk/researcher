# Makale değerlendirmeleri

Makale değerlendirme dilimi, üretim makale incelemesinin sağlayıcı ve model ayarlarını değiştirmeden adlandırılmış profilleri aynı sabit görevlerde karşılaştırır. Bu iş akışı isteğe bağlıdır; yayın toplama, makale özeti otomasyonu veya normal uzman incelemesi tarafından kendiliğinden başlatılmaz.

İlk sürüm bir temsilî model kıyaslaması değildir. Küçük sentetik paket; profil bağlantısını, kaynak metni okuma davranışını, sonuç doğrulamasını, kalıcı iş kuyruğunu ve telemetriyi denetlemek için bir kalibrasyon düzeneğidir. Kullanıcının uzman etiketleyememesi bilimsel doğruluk için uydurma bir referans kümeye dönüştürülmez.

## Sabit profiller

Analiz servisi yalnızca aşağıdaki önceden tanımlanmış profil kimliklerini kabul eder:

| Profil kimliği | Sağlayıcı ve istenen model | Kullanılabilirlik | Sabitleme |
| --- | --- | --- | --- |
| `gemini-baseline` | Gemini, `gemini-3.8-flash` | `Gemini:ApiKey` varsa `configured` | Profil ayar sürümü ve parmak izi |
| `ollama-qwen-baseline` | Yerel Ollama, `qwen3.8:27b-q4_K_M` | `Ai:OllamaBaseUrl` güvenli bir loopback adresiyse `configured` | Model özeti `25b843619e944cd0ae6069f94ff4e5e26a16e109ccbc0a66a0f05979ed70098e` |
| `deepseek-candidate` | DeepSeek, `deepseek-flash` | `Evaluation:DeepSeek:ApiKey` yoksa `not_configured` | Profil ayar sürümü ve parmak izi |

Profil kimliği serbest metinle yeni bir model seçmez. Kolektör kuyruğa almadan önce analiz servisindeki `GET /api/v1/evaluations/profiles` iç uç noktasından izin listesini ve kullanılabilirliği okur. Seçilen profilin sağlayıcı, istenen model, model revizyonu, güvenli yürütme ayarları, `SettingsVersion` ve SHA-256 `SettingsFingerprint` değeri SQL'e anlık görüntü olarak yazılır. Parmak izi; profil meta verisinden ve sıralı, yeniden kurulabilir ayarlardan üretilir. İş çalıştırılırken güncel parmak izi tekrar kontrol edilir; değişiklik `ProfileDrift` ile kapalı biçimde başarısız olur.

Bu profiller değerlendirmeye özeldir. `Ai:Provider`, `Ai:Model`, `Ai:ArticleProvider`, `Ai:ArticleModel` veya normal makale doğrulayıcısı seçimini değiştirmezler.

## Kontrollü kalibrasyon paketi

Her çalışma, `controlled-source-reading-v2` veri kümesindeki altı sentetik vakayı otomatik olarak içerir. Dört vaka İngilizce, iki vaka Türkçedir. Her vakada üç iddia bulunur; toplam 18 iddia, beklenen sınıflar arasında tam dengelidir: altı `supported`, altı `unsupported`, altı `uncertain`. Kontrollü iddialar varsayılan olarak `findings` bölümündedir; örneklem büyüklüğü ve kurum kaynağı iddialarını içeren yönlendirme-enjeksiyonu vakası istemin bölüm denetimiyle tutarlı biçimde `data` bölümündedir.

Vakalarda sayı ve birim değişimleri, olumsuzluk, nüfus koşulu ve kaynak içine gömülmüş yönlendirme gibi sınırlı okuma sorunları ölçülür. Bu kapsam gerçek makale çeşitliliğini, alan bilgisini, yöntem kalitesini veya eksiksiz bulgu keşfini temsil etmez.

Çalışma oluşturulurken iddia sırası `RunId`, vaka kimliği ve özgün iddia kimliğinden deterministik olarak karıştırılır. Modele yalnızca çalışma için üretilen opak kimlikler, iddia metinleri ve değişmez kaynak span kimlikleri gönderilir. Beklenen yanıtlar ve mekanik türetim açıklamaları kolektörde kalır; analiz servisine veya modele gönderilmez.

12 Eylül 2026 tarihli altı modellik kontrollü çalışma ile eşlenmiş gerçek-makale pilotunun yöntem, sonuç ve yorum sınırları [model karşılaştırma raporunda](MODEL_COMPARISON_20260912.md) tutulur.

Terminal bir çalışmada kontrollü kaynak-okuma doğruluğunun paydası planlanan bütün kalibrasyon iddialarıdır. Başarısız işlerin iddiaları paydada kalır ve karışıklık matrisinde gerçek sınıfa karşı `failed` olarak görünür. Eksik veya geçersiz model yanıtları ayrıca sayılır. Çalışma sürerken doğruluk oranı verilmez; tanımlı bir paydası veya gözlemi olmayan oranlar sahte sıfır yerine `null` kalır.

## Gerçek makale vakaları ve kör çapraz kontrol

İstek en fazla üç gerçek vaka ekleyebilir. Her vaka için güncel `PersonelID`–`CanonicalWorkId` ilişkisi ve istenen dilde başarılı, değişmez bir kanonik analiz ile kaynak anlık görüntüsü bulunmalıdır. Kolektör yeni URL açmaz, sağlayıcıdan veri toplamaz ve metni yeniden çıkarmaz. Sayfaları, span'leri, kaynak türünü, çıkarım sürümünü ve kapsam bilgisini mevcut SQL kaydından kopyalar; hash ve katalog tutarlılığını kuyruğa almadan önce denetler.

İlişki hem iş alınırken hem sonuç kaydedilirken tekrar kontrol edilir. Gerçek vakalı bir çalışmanın okunması da aynı sahip `PersonelID` değerini ve bütün yayınlarla güncel ilişkiyi gerektirir. Böylece geçmişte kuyruğa alınmış bir iş, ilişki kaldırıldıktan sonra sonucu açığa çıkaramaz.

`EnableBlindCrossCheck=true` seçildiğinde listedeki ilk profil birincil inceleme kaynağıdır. Her profil aynı kaynak üzerinde bağımsız bir uzman incelemesi üretir; ikinci ve varsa üçüncü profil ayrıca ilk profilin bulgularını kontrol eder. Çapraz kontrol isteği özgün bulgu kimliklerini opak kimliklerle değiştirir ve ilk profil kimliğini taşımaz. Birincil inceleme başarısızsa ona bağlı çapraz kontroller `Skipped` olur.

`CrossModelVerdictAgreement`, yalnızca diğer profilin birincil bulguyu kaynak karşısında `supported` saydığı çapraz-kontrol kararlarının bütün çapraz-kontrol kararlarına oranıdır. Bu, bağımsız uzman doğrulaması değildir: modeller benzer eğitim verileri, istem yapısı ve hatalar paylaşabilir. Sonuç bilimsel doğruluk, yöntem kalitesi veya bütün önemli bulguların bulunduğu anlamına gelmez.

## Kalıcı yürütme ve sınırlar

`StartArticleEvaluation` işi SQL'e yazar ve HTTP 202 döndürür. Bir çalışma en fazla üç profil, üç gerçek vaka, 36 iş öğesi ve ağırlıklı 114 sağlayıcı çağrısı içerir. Bütçe hesabında kalibrasyon işi 1, gerçek inceleme 8, çapraz kontrol 4 olası model çağrısı sayılır. İş öğesi sayısı çağrı bütçesi değildir.

Tek değerlendirme worker'ı SQL uygulama kilidiyle sıradaki işi alır. Her işin en fazla bir denemesi vardır. İstek kaydı model çağrısından önce yazılır ve yürütme belirteci sonuç kaydını çitler. Süreç, sonucu kaydetmeden önce kesilirse çağrı ücretli olmuş olabilir; iş ve açık deneme `Interrupted` yapılır, maliyet `Unknown` kalır ve otomatik tekrar denenmez. Diğer yürütme hataları da sonucu paydadan çıkarmadan kalıcı başarısızlık olarak saklanır.

`GetArticleEvaluation` salt-okunur ve sayfalıdır. Üst yanıt; çalışma durumu, veri/evaluator/politika sürümleri, sabitlenmiş profiller, toplam ve tamamlanan iş sayıları ile toplu ölçümleri verir. `ProfileAggregates` her profil için aynı tam ölçüm bloğunu; karışıklık matrisi, planlanan/bekleyen/başarısız/eksik/geçersiz iddia sayıları ve kontrollü doğruluk dahil ayrı hesaplar. Ayrıca profil başına toplam süre, nullable giriş/çıkış token toplamları ve `Known`, `Partial` veya `Unknown` `TokenStatus` döner. Her iş öğesi; vaka ve aşama, profil, durum, deneme sayısı, sonuç kodu, gerçekte dönen model kimliği, güvenli ölçümler, telemetri ve nullable maliyet tahminini içerir. Telemetri sağlayıcı çağrısı başına aşama/rol, istenen ve dönen model, başlangıç/bitiş zamanı, süre, giriş/çıkış/cache/thinking token sayıları, fiyat sürümü, nullable USD tahmini ve hata kodunu korur.

Gerçek inceleme ölçümlerindeki `ExactEvidenceLinkRate`, dönen kanıtların kayıtlı kaynak kimliği, sayfa, başlangıç/bitiş ofseti ve alıntıyla birebir katalog eşleşme oranıdır. Bu yalnızca mekanik bağ bütünlüğüdür. `ReviewCandidateFindings`, `ReviewSupportedFindings`, `ReviewUnsupportedFindings`, `ReviewUncertainFindings` ve `ReviewOmittedFindings` üretim ve doğrulama kapsamını ayrı gösterir. `ScientificAccuracy` ve `RealArticleOmissionRecall` uzman referansı bulunmadığı için `null`/`NotValidated` kalır. Sistem otomatik bir model kazananı, yayın kalite puanı veya İK kararı üretmez.

## Yapılandırma

Kolektörün kuyruk ve kaynak sınırları `academicsettings.json` içindeki `ArticleEvaluation` bölümündedir:

```json
"ArticleEvaluation": {
  "WorkerEnabled": true,
  "PollSeconds": 5,
  "RequestTimeoutSeconds": 330,
  "MaximumSourceBytes": 100000,
  "EvaluatorVersion": "article-evaluator-v1",
  "PolicyVersion": "article-evaluation-policy-v1"
}
```

Gerçek vaka kaynağı kuyruğa alınırken etkin sınır, bu kolektör değerinin ve seçilen profillerin sabitlenmiş `maximumInputBytes` değerlerinin en küçüğüdür; bu nedenle analiz servisinde kaçınılmaz biçimde reddedilecek büyük bir kaynak çalışması oluşturulmaz.

Kolektörün 330 saniyelik istek sınırı, analiz servisinin yapılandırılmış 300 saniyelik toplam sınırından sonra yapılandırılmış timeout telemetrisini döndürebilmesi için 30 saniye pay bırakır. Analiz servisinin bağımsız değerlendirme sınırları `ResearcherAnalysisService/appsettings.json` içinde `Evaluation` altında yapılandırılır:

```json
"Evaluation": {
  "TimeoutSeconds": 300,
  "MaximumInputBytes": 100000,
  "ContextTokens": 131072,
  "MaxOutputTokens": 8192,
  "VerifierMaxOutputTokens": 8192,
  "DeepSeek": {
    "ApiKey": null,
    "PricingVersion": null,
    "InputUsdPerMillionTokens": null,
    "CacheHitUsdPerMillionTokens": null,
    "OutputUsdPerMillionTokens": null
  }
}
```

Gemini anahtarı `Gemini:ApiKey`, yerel Ollama adresi `Ai:OllamaBaseUrl` üzerinden okunur. DeepSeek maliyeti yalnızca fiyat sürümü ile üç oran birlikte ve negatif olmayan değerlerle tanımlanırsa hesaplanır. Anahtarları kaynak denetimine yazmayın; `dotnet user-secrets` veya güvenli üretim yapılandırması kullanın.

## API akışı

Kolektörün desteklenen V1 uç noktaları şunlardır:

- `POST /Services/AcademicPerformance/V1/StartArticleEvaluation`: sabit profilleri ön denetler, kalibrasyon ve isteğe bağlı gerçek vakaları kalıcı kuyruğa ekler, HTTP 202 döndürür.
- `POST /Services/AcademicPerformance/V1/GetArticleEvaluation`: `RunId`, zorunlu sahip `PersonelID`, `Skip` ve `Take` ile kalıcı ilerleme/sonuç sayfasını okur.

`StartArticleEvaluationRequest`, `ProfileIds`, `PersonelId`, `RealCases` ve `EnableBlindCrossCheck` alanlarını taşır. HTTP 202 gövdesindeki `StartArticleEvaluationResponse`; `RunId`, ilk durum, vaka/iş/olası çağrı sayıları ile veri kümesi ve evaluator sürümünü döndürür. Sayfalı okuma `GetArticleEvaluationRequest` alır ve `ArticleEvaluationResponse` döndürür. Analiz servisindeki `GET /api/v1/evaluations/profiles` ve `POST /api/v1/evaluations/execute` kolektörün `X-Analysis-Key` ile kullandığı iç sözleşmelerdir; son kullanıcı akışı değildir.

Bu dilimde iptal uç noktası yoktur. Collector'daki kalibrasyon dahil bütün başlatma ve okuma istekleri açık bir `PersonelID` ve güvenilir ürün erişim adaptörü üzerinden yetki denetimi gerektirir. Collector'ın kalıcı kuyruk örnekleri [ArticleEvaluation.http](../Requests/AcademicCollector/ArticleEvaluation.http), Analysis Service'in tam kaynak ve güncel profil parmak izi isteyen doğrudan örneği [EvaluationAndFaculty.http](../ResearcherAnalysisService/Requests/EvaluationAndFaculty.http) dosyasındadır.

## Yerel entegrasyon smoke sonucu

11 Eylül 2026'da donmuş analiz servisi derlemesine doğrudan gönderilen tek bir İngilizce, üç iddialı sentetik istek `ollama-qwen-baseline` profilinin SHA-256 ayar parmak izi ön denetimini geçti. Bu çalıştırmada profil timeout değeri 180 saniye, context sınırı 32.768 token, üretim ve doğrulayıcı çıkış sınırları 4.096 token idi. Gerçekte dönen model `qwen3.8:27b-q4_K_M` oldu. Beklenen yanıtlar modele gönderilmedi.

Model üç iddianın ikisini doğru sınıflandırdı: kaynakla aynı 12 puanlık iddiayı `supported`, kaynak 12 derken 21 puan diyen iddiayı `unsupported` verdi. Kaynakta B grubu sonucu bulunmadığı için `uncertain` beklenen üçüncü iddiayı ise hatalı biçimde `unsupported` saydı. Duvar süresi 154,429 saniye, sağlayıcı süresi 153.809 ms, kullanım 542 giriş ve 353 çıkış tokenıydı; maliyet `null` ve durum `Unknown` kaldı. `ScientificAccuracy` yine `null` idi.

Bu yalnızca tek bir entegrasyon smoke çalıştırmasıdır; altı vakalı kalıcı SQL çalışması, kapsamlı doğruluk sonucu veya model sıralaması değildir. Tam değerlendirme ölçümleri ancak belirli `RunId`, profil parmak izleri, veri/evaluator/politika sürümleri ve tamamlanmış SQL kayıtlarıyla birlikte raporlanmalıdır.
