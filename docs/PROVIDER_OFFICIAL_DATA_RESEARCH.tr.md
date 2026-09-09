# Sağlayıcılardan resmî durum ve kota verisi alma araştırması

## Değerlendirme

Sağlayıcının bildirdiği hesap kullanımı ile uygulamanın ölçtüğü erişilebilirlik aynı veri değildir. Mevcut ProviderStatus API’si gerçek HTTP sonuçlarını, bazı sağlayıcı kota alanlarını ve collector’ın yerel SQL sayaçlarını birleştiriyor. Bu yaklaşım yararlıdır; ancak bütün yanıtı “sağlayıcının resmî durumu ve kalan kotası” diye sunmak doğrulanabilir değildir.

En somut geliştirme alanları ORCID’in özel durum endpoint’ini ve OpenAlex’in özel kota endpoint’ini kullanmaktır. SearchApi tarafında hesap sorgusunun doğru resmî kaynağı zaten kullanılmaktadır. Web of Science Starter ve YÖKSİS için aynı düzeyde hesabın kalan kotasını veren, bu kapsamda doğrulanmış bir sözleşme bulunmamaktadır. Bu iki sağlayıcı için eksik bilgiyi yerel sayaçla doldurmak yerine kaynağı ve belirsizliği göstermek gerekir.

Değerlendirme tarihi 9 Eylül 2026’dır. Kod karşılaştırması `Efeblk/researcher` deposunun `e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e` sürümüne dayanır. İncelenen status uygulaması, çalışma dalındaki sürümle aynıdır. Bu belge araştırma ve uygulama önerisidir; önerilen değişiklikler henüz ürün koduna uygulanmış değildir. [1]

> Uygulama notu (9 Eylül 2026): Bu araştırmadan sonra aynı çalışma dalında ORCID'in
> resmî status endpoint'i ve SQL ile beş dakikalık instance'lar arası koordinasyon,
> anahtarlı OpenAlex `/rate-limit` sorgusu, SearchApi alan doğrulaması ve ek V1 kaynak/
> zaman/kapsam alanları uygulanmıştır. Web of Science ve YÖKSİS için resmî hesap kotası,
> AI sağlayıcısı kullanım yönetimi ve kimlik doğrulanmış OpenAlex canlı şema kontrolü
> hâlâ açık doğrulamalardır. Güncel çalışma sözleşmesi `docs/PROVIDER_STATUS.md` dosyasındadır.

## Kavramlar ve kanıt düzeyi

| Bilgi | Gerçekte neyi gösterir? | Uygun sunum |
|---|---|---|
| Hesap/kota endpoint’i | Sağlayıcının ilgili hesap veya anahtar için bildirdiği kullanım | Sağlayıcı tarafından bildirilen hesap kotası |
| Belgelenmiş kota başlığı | İlgili isteğin yapıldığı andaki limit/kalan miktar | Sağlayıcı yanıtından alınan kota |
| Resmî servis durum endpoint’i | Sağlayıcının kendi bileşenleri hakkındaki değerlendirmesi | Sağlayıcının bildirdiği servis durumu |
| Resmî status sayfası | Yayımlanan olaylar ve kapsanan bileşenlerin genel durumu | Genel servis durumu; hesap erişimini doğrulamaz |
| Normal API’ye kontrol isteği | Bizim ortamımızdan belirli bir işlemin sonucu | Erişim kontrolü sonucu |
| Genel plan dokümanı | Bir plan için yayımlanmış üst sınırlar | Belgelenmiş plan limiti; kalan kullanım değil |
| SQL sayaçları | Collector’ın kaydettiği denemeler ve kendi sınırı | Yerel kullanım/sınır |
| Limit eksi kullanım | Girdi alanlarından türetilen değer | Hesaplanan kalan miktar |

Bir alanın gerçek sağlayıcı yanıtında bulunması ve alanın anlamının resmî sözleşmeyle belgelenmesi ayrı kanıtlardır. Belgelenmemiş bir başlık gözlemlenebilir; fakat biriminin, sıfırlanma döneminin ve hesap kapsamının bilindiği varsayılmamalıdır. “Bilgi yok”, “sorgu başarısız” ve “sağlayıcı bu bilgiyi sunmuyor” da farklı durumlardır.

## Sağlayıcı karşılaştırması

| Sağlayıcı | Resmî hesap/kota kaynağı | Resmî durum kaynağı | Mevcut koddaki durum | Karar |
|---|---|---|---|---|
| SearchApi | `/api/v1/me` | Status sayfası; ayrıca hesap analitiği | Hesap endpoint’i kullanılıyor | Kaynağı koru, dönem ve türetme bilgisini geliştir |
| OpenAlex | `/rate-limit`, belgelenmiş yanıt başlıkları | Status sayfası | `/works` isteğinin başlıkları okunuyor | Anahtar varsa özel kota sorgusu ekle |
| ORCID | Genel limit politikası var; canlı kalan kota endpoint’i doğrulanmadı | `/v3.0/pubStatus`, `/v3.0/apiStatus`, status sayfası | Arama sorgusuyla kontrol ediliyor | Resmî durum kontrolüne geç; kotayı ayrı tut |
| Web of Science Starter | İncelenen OpenAPI’de özel hesap endpoint’i yok | Starter’a özel durum endpoint’i doğrulanmadı | Belge sorgusu ve genel başlık ayrıştırıcısı | Erişim kontrolü olarak sun; kota sözleşmesini sağlayıcıyla doğrula |
| YÖKSİS | Kamuya açık kota sözleşmesi doğrulanmadı | Kamuya açık özel health sözleşmesi doğrulanmadı | WSDL erişimi kontrol ediliyor | Yalnızca WSDL erişimi olarak sun |
| AnalysisService | Collector’dan bağımsız; seçilen AI sağlayıcısına bağlı | Kendi `/health` endpoint’i | Sunucunun çalıştığı kontrol ediliyor | Sunucu, model erişimi ve kullanım bilgisini ayır |

Bu tablodaki olumlu bulgular ilgili sağlayıcı bölümlerindeki birincil kaynaklarla desteklenir. “Doğrulanmadı” ifadesi, hizmetin kesinlikle bulunmadığı anlamına gelmez; kamuya açık veya erişilebilen sözleşmelerin sınırını belirtir.

## SearchApi

### Doğrulanmış olanaklar

Resmî Account API, `GET https://www.searchapi.io/api/v1/me` adresindedir. Kimlik doğrulama `Authorization: Bearer` veya `api_key` parametresiyle yapılabilir. Aylık kullanım, aylık hak, kalan kredi, saatlik arama sayısı ve saatlik limit döner. Aktif abonelik varsa dönem başlangıç/bitiş alanları da belgelenmiştir. Bu, SearchApi hesabının verisidir; Google Scholar’ın doğrudan sunduğu hesap kotası değildir. [2]

| Kaynak alan | Önerilen anlam |
|---|---|
| `account.monthly_allowance` | Sağlayıcının bildirdiği aylık hak |
| `account.current_month_usage` | Sağlayıcının bildirdiği aylık kullanım |
| `account.remaining_credits` | Sağlayıcının bildirdiği kalan kredi |
| `api_usage.hourly_rate_limit` | Saatlik arama sınırı |
| `api_usage.searches_this_hour` | İçinde bulunulan saatteki arama sayısı |
| `subscription.period_start`, `period_end` | Varsa abonelik dönemi |

Mevcut kod ilk beş alanı okur. Saatlik `Remaining`, sağlayıcının doğrudan döndürdüğü bir alan olmayıp limitten kullanımı çıkararak hesaplanır. Bu değere alan düzeyinde `DerivedFromProviderValues` gibi bir işaret eklenmesi önerilir. Abonelik bitişini, açık sözleşme olmadan bütün kota pencerelerinin kesin sıfırlanma anı kabul etmemek gerekir. [1][2]

`GET /api/v1/search_analytics` hesabın tarihsel arama performansını ve motor bazında sonuçlarını sağlar. Belgeye göre Scale veya üstü plan gerekir; endpoint ücretsizdir ve son bir yılın verisini kapsar. Bu veri, anlık health yerine geçmiş başarı/hata/gecikme değerlendirmesi için uygundur. Account API’nin çağrı maliyeti hakkında aynı ücretsiz garantisi çıkarılmamalıdır. [3]

Resmî status sayfasında API ve web sitesi bileşenleri ile olay geçmişi bulunur. Sayfanın erişilebilir olması, desteklenen herkese açık bir JSON status sözleşmesinin varlığını kanıtlamaz; belgelenmiş bir besleme doğrulanmadan iç sayfa isteklerine bağımlı bir entegrasyon kurulmamalıdır. [4]

### Uygulama değerlendirmesi

Hesap sorgusunu arama kredisi tüketen normal toplama işlerinden ayrı bir işlem türü olarak izlemek daha açıklayıcıdır. Bu ayrım yerel sayaç içindir; sağlayıcının faturayı nasıl hesapladığını değiştirmez. Kota sorgusu başarısız olursa son başarılı değer zamanıyla birlikte tutulabilir, fakat yeni ve geçerli bilgi gibi işaretlenmemelidir.

Mevcut ayrıştırıcı `account: {}` için bütün sayıları null olan bir kota öğesi ekleyebilir. Listenin boş olmaması tek başına anlamlı kota verisi bulunduğunu kanıtlamaz. HTTP erişimi başarılı kalabilirken kota sonucu `Unavailable` veya `Unknown` olmalıdır; bunun için en az bir kullanılabilir sayısal alan ve beklenen alan türleri kontrol edilmelidir. [1]

## OpenAlex

### Doğrulanmış olanaklar

Güncel Authentication belgesi hem Bearer hem query parametresi ile anahtar kullanımını, günlük bütçe başlıklarını ve `GET https://api.openalex.org/rate-limit?api_key=YOUR_KEY` sorgusunu açıkça gösterir. `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Credits-Used` ve `X-RateLimit-Reset` alanları belgelenmiştir; sonuncusu UTC gece yarısına kalan saniyedir. [5]

Burada `Credits-Used` yalnızca ilgili isteğin maliyetidir; günlük toplam kullanım diye eşlenmemelidir. Hesap kotası sorgusu için anahtar kullanılmalı, anahtarsız sıradan istekten alınan bütçe kullanıcının kişisel hesabına mal edilmemelidir. Resmî status sayfası ayrıca genel servis durumunun izlenmesi için kullanılabilir. [5][7]

### Şema ve güncellik sınırı

OurResearch’ün eski dokümantasyon deposunda `/rate-limit` yanıtı için `rate_limit.credits_limit`, `credits_used`, `credits_remaining`, `resets_at` ve `resets_in_seconds` örneği bulunur. Aynı dosyada eski anahtar/plan anlatımı da vardır. Güncel Help Center anahtarların daha geniş kullanımını anlatırken eski dosya premium bağlamına ağırlık verir. Bu nedenle eski örnek, güncel hesapla sözleşme doğrulaması yapılmadan kesin üretim şeması kabul edilmemelidir; örnekteki limitler koda sabitlenmemelidir. Eski örnekte üst seviyede `api_key` de bulunur: bütün gövdeyi istemciye aktarmak yerine yalnızca gerekli sayısal alanlar seçilmelidir. [6]

9 Eylül 2026’da bu rapor kapsamında yapılan anahtarsız `/rate-limit` isteği HTTP 401 döndürmüştür. Bu gözlem bir hesabın güncel kota şemasını veya bakiyesini doğrulamaz; yalnızca anahtarsız isteğin kabul edilmediğini gösterir. Kimlik bilgisiyle sorgu yapılmamıştır.

### Uygulama değerlendirmesi

Mevcut `ProviderStatusService`, `/works?per_page=1&select=id` isteğini gönderip başlıklardan limit/kalan bilgisi toplar. Bu resmî yanıt verisidir, ancak özel hesap endpoint’inin yerine normal liste işlemi yürütülmektedir. Özel sorgu eklendiğinde normal yanıtlar üzerinden alınan son kota başlıkları da destekleyici kaynak olarak korunabilir. [1]

Güvenilir uygulama için önce yetkili bir hesapta küçük bir doğrulama yapılmalı; dönen alan adları, türleri ve birimleri incelenmelidir. Onaylı sayısal alanlardan temizlenmiş bir test örneği hazırlanmalı, anahtar ve hesap tanımlayıcıları dahil edilmemelidir. Birim doğrulanamıyorsa “istek” veya para birimi uydurmak yerine birim bilinmiyor olarak bırakılmalıdır. Kota endpoint’inin ücretlendirilmediği de ayrıca doğrulanmadan varsayılmamalıdır.

## ORCID

### Resmî health endpoint’i

ORCID üretim için Public API’de `https://pub.orcid.org/v3.0/pubStatus`, Member API’de `https://api.orcid.org/v3.0/apiStatus` adreslerini belgeliyor. Web uygulaması durumu ayrı `webStatus.json` adresindedir. Sağlayıcı durum kontrollerinin beş dakikada birden sık yapılmamasını ister; genel görünüm için `status.orcid.org` adresini de gösterir. [8]

9 Eylül 2026’da Public API durum endpoint’ine yapılan tek anahtarsız canlı sorgu HTTP 200 ve aşağıdaki gövdeyi döndürdü. Bu örnek sentetik değildir; yalnızca o andaki gözlemdir. [9]

```json
{
  "tomcatUp": true,
  "dbConnectionOk": true,
  "readOnlyDbConnectionOk": true,
  "overallOk": true
}
```

Önerilen adaptör HTTP koduna ek olarak `overallOk` değerini kontrol etmelidir. `overallOk: false`, alanın eksikliği ve yanlış tür birbirinden ayrılmalıdır. Endpoint değişikliği sırasında mevcut arama yanıtındaki `num-found` doğrulaması da değiştirilmelidir; yalnızca URL değiştirmek yeterli değildir. Başarılı genel durum sorgusu bir client token’ının geçerli olduğunu doğrulamaz. [1][8][9]

### Kota sınırı

ORCID’in yayımladığı politika anonim istekleri IP, kayıtlı Public API kullanımını client ID düzeyinde ayırır. Belgede anonim kullanım için 25 bin okuma/gün, kayıtlı public kullanım için 100 bin okuma/gün; Member API için kullanım kotası olmaması belirtilir. Saniyelik hız sınırları bundan ayrıdır. Bunlar canlı kalan bakiye değildir. Belge ayrıca burst aşımında HTTP 503 olabileceğini açıklar; her 503’ü kesin genel servis kesintisi gibi yorumlamak doğru olmaz. [10]

Bu kapsamda IP veya client için güncel kalan okuma sayısını veren ayrı bir resmî endpoint doğrulanmamıştır. Aynı client ID başka uygulamalarda veya aynı dış IP başka makinelerde kullanılıyorsa collector SQL sayacı bütün ORCID tüketimini bilemez. Durum kaynağı iyileştirilebilir; “resmî kalan kota” aynı gerekçeyle otomatik olarak sağlanamaz.

### Uygulama değerlendirmesi

ORCID için en az 300 saniyelik ayrı yenileme aralığı önerilir. Collector’ın genel API yanıtı daha sık okunabilse de ORCID’e giden gerçek sorgu bu aralığı aşmamalıdır. Çoklu instance kullanılıyorsa yalnızca süreç içi önbellek yeterli olmayabilir; paylaşılmış son kontrol zamanı veya tek kontrol sorumlusu gerekir. Bu, sağlayıcının önerisini bütün deployment düzeyinde uygulamaya yönelik bir tasarım kararıdır.

## Web of Science Starter

### Sözleşmenin gösterdiği sınır

Clarivate portalının doğrudan bağladığı Starter OpenAPI belgesindeki yollar `/documents`, `/documents/{uid}`, `/journals` ve `/journals/{id}` ile sınırlıdır. İncelenen sözleşmede `/account`, `/usage`, `/quota`, `/rate-limit` veya Starter’a özel `/health` yolu yoktur. Kota başlıklarının mevcut genel ayrıştırıcıdaki adlarla döneceğini garanti eden bir tanım da bulunmamıştır. Bu sonuç erişilebilen Starter sözleşmesiyle sınırlıdır; ayrı özel yönetim servislerini dışlamaz. [11]

Portal, API key ve abonelik/plan ayrımını açıklar. Yayımlanan plan sınırı hesabın bugün kalan hakkı değildir. Örneğin ücretsiz deneme planı için belirtilen 50 istek/gün, o anahtarın o gün hiç kullanılmadığı anlamına gelmez. [12]

Clarivate’ın SUSHI Status API’si gerçekten resmî ve kimlik doğrulamasızdır; fakat SUSHI raporlama servisine aittir. Starter’ın belge sorgularının çalıştığına kanıt olarak kullanılamaz. Aynı sağlayıcı altında olması ürün kapsamlarını eşitlemez. [13]

### Uygulama değerlendirmesi

Mevcut kod gerçek bir belge sorgusu yapıyor ve genel `X-RateLimit-*` başlıklarını varsa topluyor. Bunun doğru adı hesap kotasını doğrulayan sorgu değil, Starter erişim kontrolüdür. Başlıklardan değer alınırsa kaynak başlık adı korunmalı; dönem ve birim doğrulanana kadar genel varsayımlar uygulanmamalıdır. [1]

Her dashboard yenilemesinde belge sorgulamak düşük limitli planlarda pahalı olabilir. Örneğin varsayımsal olarak her dakika bir kontrol 24 saatte 1.440 istek eder; bu sayı tek instance için bile küçük günlük limitleri aşar. Bu bir sağlayıcı ücret bilgisi değil, kontrol sıklığı hesabıdır. Normal toplama yanıtlarından pasif gözlem ve seyrek kullanıcı tetiklemeli kontrol daha uygun bir tasarımdır.

Clarivate’tan istenecek doğrulama nettir: Starter hesabı için resmî kullanım endpoint’i var mı; kalan günlük hak hangi başlıkla döner; sayaç hangi kapsamda tutulur; reset hangi zaman dilimindedir; başarısız istek ve limit=0 sorgusu sayılır mı? Cevap gelmeden Expanded veya başka Clarivate ürünlerinin alanları Starter’a taşınmamalıdır.

## YÖKSİS

Mevcut entegrasyon `OzgecmisV2` SOAP servisini kullanır; status kontrolü aynı adrese `?wsdl` ekler. Kod deposundaki operasyon kataloğu akademik özgeçmiş işlemlerini listeler; içinde bir hesap kotası veya health işlemi bulunmaz. Katalog uygulamanın kullandığı alt kümedir, YÖKSİS’in tüm hizmetlerinin listesi değildir. [14]

Kamuya açık kaynaklarda bu servis için kalan kota/abonelik kullanımı sözleşmesi doğrulanamamıştır. Resmî WSDL adresinin içeriği de bu değerlendirmede okunabilir biçimde elde edilememiştir. Bundan “YÖKSİS kesinlikle böyle bir hizmet sunmuyor” sonucu çıkarılamaz; kuruma sağlanan entegrasyon dokümanı veya servis yetkilisinin cevabı gereklidir. [15]

WSDL’in başarılı gelmesi yalnızca servis tanımına erişimi gösterir. Bir SOAP operasyonunun çalışması, kurumsal kullanıcının ilgili işlem yetkisi ve bir personelin verisini okuyabilmesi farklı kontrollerdir. Sağlık testi amacıyla gerçek kişinin T.C. kimlik numarasını kullanmak yerine, kurumun resmen sunduğu kişisel veri gerektirmeyen bir test/ping işlemi varsa o tercih edilmelidir.

Kısa vadeli doğru çıktı `WsdlReachability` ile sınırlı kalır; resmî kota `Unknown` olur. Yerel `DailyRequestLimit`, hız ayarı ve SQL sayacı collector politikasını göstermeye devam edebilir. Kuruma yöneltilecek bilgi talebi birim, kota dönemi, hesap/IP kapsamı, reset zamanı, sorgu maliyeti ve hata kodlarını birlikte kapsamalıdır.

## AnalysisService ve AI sağlayıcısı

AnalysisService’in `/health` endpoint’i kendi kodumuzda `Service = ResearcherAnalysisService`, `Status = Running` döndürür. Model yükleme veya çıkarım yapmaz. Dolayısıyla bu yanıtı OpenAI/Ollama erişimi, modelin çalışması veya kalan AI bütçesi olarak sunmak doğru değildir. [16]

### Yerel Ollama

Ollama resmî API’sindeki `/api/tags` mevcut modelleri, `/api/ps` bellekte çalışan modelleri listeler. Bunlar model envanteri ve yüklenme durumunu gözlemek için uygundur. `/api/ps` listesinin boş olması, diskteki modelin sonraki istekte yüklenemeyeceği anlamına gelmez. Gerçek çıkarım başarısı ayrı bir testtir; bu iki endpoint tek başına onu kanıtlamaz. [17][18]

Yerel Ollama için uzak abonelik kredisi modeli dayatılmamalıdır. Süre, bellek, kuyruk kapasitesi ve model bulunabilirliği ayrı işletim ölçütleridir. Ollama Cloud gibi ayrı bir hizmet kullanılması halinde onun hesap sözleşmesi ayrıca ele alınmalıdır; bu rapordaki yerel Ollama çıkarımları otomatik olarak buluta uygulanmaz.

### OpenAI

OpenAI’nin resmî Usage ve Costs API’leri kuruluş kullanımını ve harcamayı sorgulama olanağı verir. Proje bazlı rate-limit listeleme endpoint’i de model başına yapılandırılmış sınırları döndürür. Bunlar sırasıyla geçmiş kullanım/harcama ve yapılandırılmış kapasite bilgisidir; doğrudan “şu an kaç rapor daha üretebilirim” sayısı değildir. [19][20]

Normal API yanıtlarında istek/token limitleri, kalan miktarlar ve reset bilgileri için ayrı başlıklar belgelenmiştir. Proje token başlıkları da uygulanabildiğinde bulunabilir. Mevcut genel kota ayrıştırıcısı bu farklı alan isimlerini kapsamaz; bir OpenAI adaptörü gerekir. [21]

Yönetim sorguları için gerekli admin yetkisi sıradan model çağrısı anahtarıyla eş tutulmamalıdır. Kullanım izleme, AnalysisService içinde ayrı yetkilendirilmiş bir yönetim bileşeni olarak tasarlanabilir; collector yalnızca temizlenmiş özeti almalıdır. Kalan token kapasitesi, parasal bakiye ve geçmiş maliyet ayrı alanlarda gösterilmelidir. [22]

## Mevcut uygulamadaki düzeltme öncelikleri

| Öncelik | Somut bulgu | Önerilen değişiklik | Doğrulama |
|---|---|---|---|
| P0 | `Status` birden fazla anlamı topluyor | Erişim, sağlayıcı health’i ve kota sorgusuna ayrı sonuçlar ekle | Health başarılı/kota başarısız karma durum testi |
| P0 | ORCID özel health yerine arama kullanıyor | `pubStatus` adaptörü, boolean alan doğrulaması, ayrı yenileme aralığı | `overallOk` true/false/eksik/yanlış tür |
| P0 | OpenAlex özel hesap endpoint’i kullanılmıyor | Anahtar varsa `/rate-limit`; önce güncel şema doğrulaması | Temizlenmiş gerçek şema fixture’ı ve 401 testi |
| P0 | SearchApi boş hesap nesnesi sayısal kanıt olmadan kota öğesi üretebiliyor | Sayısal alan kullanılabilirliğini kota durumu olarak değerlendir | `account: {}`, null, string sayı, kısmi alanlar |
| P1 | Ortak 60 saniye cache bütün kaynakları yönetiyor | Kaynak başına gözlem zamanı/TTL ve refresh koordinasyonu | Eşzamanlı çağrılar sağlayıcı sorgusunu çoğaltmamalı |
| P1 | `ProviderQuotaDto` reset, kapsam, tazelik ve türetme bilgisi taşımıyor | Ek, nullable provenance alanları veya yeni kota gözlem modeli | Eski client uyumluluğu ve bilinmeyen alanlar |
| P1 | WOS/YÖKSİS kota başlıklarının anlamı sözleşmeyle doğrulanmış değil | Resmî eşleme doğrulanana kadar garantili kota iddiası kullanma | Başlıksız yanıtta `Unknown` |
| P1 | AnalysisService JSON’da `status` bulunması yeterli görülüyor | Kendi health sözleşmesinin değerini doğrula; AI alt kontrollerini ayır | Yanlış status değerinde sağlıklı sonucu çıkmamalı |

Bu bulgular statik kod değerlendirmesine dayanır; yeni hata senaryoları bu araştırma kapsamında test koduyla çalıştırılmış değildir. Üretim değişikliği öncesinde tabloya karşılık gelen testler eklenmelidir. [1][16]

## Önerilen veri sözleşmesi

V1 tüketicilerini korumak için mevcut alanları aniden yeniden anlamlandırmak yerine ek alanlarla ilerlemek daha uygundur. Tam yeniden tasarım gerekirse yeni API sürümü tercih edilebilir. Aşağıdaki gruplar sağlayıcılar arasında aynı kavramları taşır; bütün sağlayıcılarda bütün alanlar dolu olmak zorunda değildir.

| Grup/alan | Önerilen içerik |
|---|---|
| `Reachability` | Bizim ortamımızdan yapılan isteğin sonucu, HTTP kodu ve ölçülen süre |
| `ReportedHealth` | Sağlayıcının kendi status endpoint’inin değerlendirmesi; varsa alt bileşenler |
| `AccountQuota` | Hesaba/anahtara ait resmî kullanım gözlemi |
| `LocalBudget` | Collector politikası ve yerel deneme sayaçları |
| `SourceKind` | `AccountApi`, `ResponseHeaders`, `StatusApi`, `LocalSql`, `DocumentedPolicy` |
| `SourceFields` | Değerin alındığı JSON yolu veya başlık adı |
| `ValueKind` | Doğrudan bildirilen, hesaplanan veya yapılandırılan |
| `Scope` | Hesap, API key, proje, IP, client ID, deployment; bilinmiyorsa null |
| `Window`, `Unit` | Gün/ay/dakika ve istek/kredi/token; doğrulanmış anlam |
| `ObservedAt`, `ExpiresAt` | Kaynağın son gözlem zamanı ve yerel geçerlilik sınırı |
| `ResetsAt` | Varsa sağlayıcının bildirdiği veya belgelenmiş reset bilgisinden türetilen zaman |
| `DataStatus` | `Available`, `Unknown`, `Unavailable`, `Stale`, `NotConfigured`, gerekçesiyle |

Bu modelde `Limit=null` otomatik olarak sınırsız anlamına gelmez. Sınırsız kullanım iddiası açık kanıtla ayrı işaretlenmelidir. `Remaining=0` ise bilinen ve tükenmiş bir değerdir; bilinmeyen durumla aynı gösterilmemelidir. `Used` alanının isteğe özel maliyet mi dönem toplamı mı olduğu adaptör düzeyinde doğrulanmalıdır.

Saatlik limitten kullanım çıkarılıyorsa çıktı, girdi kaynaklarını ve türetildiğini korumalıdır. Sağlayıcı doğrudan kalan miktar döndürüyorsa kendi değerimizle sessizce üzerine yazılmamalıdır. Güncel sorgu başarısız olduğunda eski veri korunabilir; ancak `Stale` etiketi ve asıl gözlem zamanı görünür olmalıdır.

Kullanıcı ekranında dört kısa satır yeterlidir: “API’ye erişim”, “Sağlayıcının bildirdiği durum”, “Hesap kotası” ve “Bu uygulamanın sınırı”. Örneğin API’ye erişim başarılıyken yerel limit dolu olabilir. Bu birleşimi tek kırmızı “provider kapalı” mesajına dönüştürmemek gerekir.

## Sorgulama ve maliyet tasarımı

Önerilen mimari, her ekran açılışında bütün sağlayıcılara yeni istek göndermez. Normal iş çağrılarında belgelenmiş kota başlıkları pasif olarak kaydedilir. Hesap/status endpoint’leri kendilerine ait aralıklarla yenilenir. Ön yüz en son gözlemi okur ve yaşını gösterir; elle kontrol seçeneği de ilgili sağlayıcı aralığına uyar.

Yerel toplama bütçesi dolduğunda resmî kota endpoint’inin de otomatik olarak engellenmesi, kullanıcının hesabını teşhis etmesini zorlaştırabilir. Bunun için kontrol isteklerini ayrı sınıflandırmak düşünülebilir. Ancak bu ayrım sağlayıcının hız sınırını veya genel bekleme talebini aşmak için kullanılmamalıdır. Aynı hesap/host için geçerli dış limitler korunur.

ORCID’in belgelenmiş beş dakikalık sınırı dışında bu rapor bütün sağlayıcılar için sabit bir resmî polling süresi belirlememektedir. SearchApi/OpenAlex hesap sorguları için başlangıçta birkaç dakikalık işletim aralığı seçilmesi bir ürün önerisidir; sağlayıcı garantisi değildir. WOS belge kontrolü gibi veri sorguları daha seyrek veya kullanıcı isteğine bağlı olmalıdır.

Collector’ın üst seviye HTTP 200 yanıtı bütün sağlayıcıların sağlıklı olduğunu ifade etmemelidir. Tek bir kaynağın zaman aşımı diğerlerini gizlememeli; her alt sonucun zaman damgası tutulmalıdır. Özellikle farklı TTL’ler getirildiğinde üst seviyedeki tek `CheckedAt` bütün verilerin aynı anda toplandığı izlenimini vermemelidir.

## Uygulama sırası ve kabul ölçütleri

İlk aşama veri anlamlarını düzeltmek ve ORCID adaptörünü eklemektir. İkinci aşama OpenAlex’in yetkili hesapla şemasını doğrulamak, özel kota adaptörünü ve SearchApi’nin alan bazlı veri kaynağını tamamlamaktır. Üçüncü aşama WOS/YÖKSİS için kurum/sağlayıcı sözleşmesini netleştirmek ve gerekiyorsa AI kullanım yönetimini ayrı bileşen olarak eklemektir.

Üretime hazır kabul edilecek bir değişiklik aşağıdaki koşulları sağlamalıdır:

1. Resmî kalan kota alanı yalnızca yetkili hesabın endpoint’inden veya anlamı belgelenmiş başlıktan doldurulur.
2. Yerel sayaç, sağlayıcı kotasının yerine geçmez; aynı anahtarın başka uygulamadaki tüketimi hakkında iddiada bulunulmaz.
3. Sağlayıcı health’i ile token/hesap yetkisi ayrı değerlendirilir.
4. Eksik, bozuk veya değişmiş şema sıfır/sınırsız/sağlıklı gibi yanıltıcı değerlere dönüştürülmez.
5. Saat, dönem, birim ve kaynak alanı korunur; UTC dönüşümleri ve reset sınırları test edilir.
6. Önbellek süresi, eşzamanlı kullanıcı sayısı ve instance sayısı kontrol isteklerini beklenmedik biçimde çoğaltmaz.
7. Test fixture’ları sayısal örneklerle sınırlıdır; sağlayıcı anahtarları ve kişisel kayıtlar yanıt/log/repoya girmez.
8. Gerçek hesap doğrulamasıyla sentetik testler raporlanırken açıkça ayrılır.

## Açık kalan doğrulamalar

SearchApi hesabının gerçek planı ve endpoint erişimi, OpenAlex’in mevcut anahtarla döndürdüğü şema, WOS Starter’ın anahtar bazlı kota başlıkları ve YÖKSİS kurum dokümanı bu raporda canlı kimlik bilgileriyle doğrulanmamıştır. ORCID Public status için tek bir anahtarsız pozitif gözlem vardır; OpenAlex kota için anahtarsız 401 gözlemi vardır. Bu gözlemler bütün sağlayıcıların üretimde uçtan uca test edildiği anlamına gelmez.

WOS/YÖKSİS için en değerli sonraki kanıt, yetkili hesaba ait temizlenmiş yanıt başlıkları ve sağlayıcının resmî alan açıklamasıdır. OpenAlex için güncel, temizlenmiş hesap yanıtı yeterli bir başlangıçtır. Resmî status sayfalarının makinece okunabilir beslemeleri ise ayrı sözleşmeyle doğrulanmadan üretim bağımlılığı yapılmamalıdır.

## Kaynaklar

Web kaynaklarının erişim tarihi 9 Eylül 2026’dır. Sayfanın erişim tarihi yayımlanma tarihi değildir. Tarihi veya sürümü belirsiz belgelerde güncellik sınırı ilgili bölümde belirtilmiştir.

1. Efeblk/researcher. [ProviderStatusService.cs, e9519b3](https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/Modules/AcademicPerformance/Service/Integrations/Status/ProviderStatusService.cs) ve [ProviderQuotaDto.cs](https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/Modules/AcademicPerformance/Service/Api/V1/Contracts/ProviderQuotaDto.cs). Sabit commit üzerinden mevcut uygulama kanıtı.
2. SearchApi. [Account API Documentation](https://www.searchapi.io/docs/account-api). Hesap alanları, kimlik doğrulama ve abonelik dönemi; yayın tarihi belirtilmiyor.
3. SearchApi. [Search Analytics API](https://www.searchapi.io/docs/search-analytics-api). Plan erişimi, tarihsel performans ve maliyet açıklaması; yayın tarihi belirtilmiyor.
4. SearchApi. [Resmî status sayfası](https://status.searchapi.io/) ve [olay geçmişi](https://status.searchapi.io/incidents). Dinamik durum kaynakları.
5. OpenAlex. [Authentication](https://help.openalex.org/api/authentication/). Sayfada son güncelleme 19 Ağustos 2026; anahtar, kota başlıkları ve `/rate-limit`.
6. OurResearch. [OpenAlex eski dokümantasyon deposu: Rate limits and authentication](https://github.com/ourresearch/openalex-docs/blob/main/how-to-use-the-api/rate-limits-and-authentication.md). Yanıt şeması örneği; eski plan/anahtar anlatımı nedeniyle sınırlı kanıt olarak kullanıldı.
7. OpenAlex. [Resmî status sayfası](https://status.openalex.org/). Dinamik durum kaynağı.
8. ORCID, Paula Demain. [How do I check the server status?](https://info.orcid.org/ufaqs/how-do-i-check-the-server-status/), 21 Kasım 2022. Resmî endpoint’ler ve beş dakikalık aralık.
9. ORCID. [Public API status endpoint’i](https://pub.orcid.org/v3.0/pubStatus). 9 Eylül 2026 tek anahtarsız HTTP gözlemi; uzun dönem sağlık garantisi değildir.
10. ORCID, Rob Blackburn. [What are the API usage quotas and limits?](https://info.orcid.org/ufaqs/what-are-the-api-limits/), sayfada 14 Kasım 2022 tarihli başlık. Erişim tarihinde yayımlanan politika tablosu; canlı hesap sayacı değildir.
11. Clarivate. [Web of Science Starter OpenAPI JSON](https://developer.clarivate.com/apis/wos-starter/swagger). Erişimde OpenAPI 3.0.0, API bilgi sürümü 1.0.0; endpoint ve başlık sözleşmesi incelemesi.
12. Clarivate. [Web of Science Starter API](https://developer.clarivate.com/apis/wos-starter). API key, planlar ve erişim; yayın tarihi belirtilmiyor.
13. Clarivate. [Web of Science SUSHI Status API](https://developer.clarivate.com/apis/sushi-status-api). Ürün kapsamı ve kimlik doğrulamasız durum servisi.
14. Efeblk/researcher. [YoksisOperationCatalog.cs, e9519b3](https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/Modules/AcademicPerformance/Service/Integrations/Yoksis/Collection/YoksisOperationCatalog.cs). Uygulamada kullanılan SOAP operasyonları; tüm YÖKSİS sözleşmesini temsil etmez.
15. YÖK. [OzgecmisV2 WSDL adresi](https://servisler.yok.gov.tr/ws/OzgecmisV2?wsdl). İçerik bu değerlendirmede okunamadı; kota/health sözleşmesine olumlu veya kesin olumsuz kanıt sayılmadı.
16. Efeblk/researcher. [ResearcherAnalysisService/Program.cs, e9519b3](https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/ResearcherAnalysisService/Program.cs). Yerel health yanıtının kapsamı.
17. Ollama. [List models](https://docs.ollama.com/api/tags). `/api/tags` sözleşmesi; yayın tarihi belirtilmiyor.
18. Ollama. [List running models](https://docs.ollama.com/api/ps). `/api/ps` sözleşmesi; yayın tarihi belirtilmiyor.
19. OpenAI. [Usage API Reference](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage). Kuruluş kullanım ve maliyet kaynakları; sürekli güncellenen başvuru.
20. OpenAI. [List project rate limits](https://developers.openai.com/api/reference/ruby/resources/admin/subresources/organization/subresources/projects/subresources/rate_limits/methods/list_rate_limits). Proje/model limitleri ve admin istemci örneği.
21. OpenAI. [API Overview — Rate limiting information](https://developers.openai.com/api/reference/overview). İstek/token/proje başlıkları; sürekli güncellenen başvuru.
22. OpenAI. [Admin API Keys](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/admin_api_keys). Yönetim API anahtarları; sürekli güncellenen başvuru.

[1]: https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/Modules/AcademicPerformance/Service/Integrations/Status/ProviderStatusService.cs
[2]: https://www.searchapi.io/docs/account-api
[3]: https://www.searchapi.io/docs/search-analytics-api
[4]: https://status.searchapi.io/
[5]: https://help.openalex.org/api/authentication/
[6]: https://github.com/ourresearch/openalex-docs/blob/main/how-to-use-the-api/rate-limits-and-authentication.md
[7]: https://status.openalex.org/
[8]: https://info.orcid.org/ufaqs/how-do-i-check-the-server-status/
[9]: https://pub.orcid.org/v3.0/pubStatus
[10]: https://info.orcid.org/ufaqs/what-are-the-api-limits/
[11]: https://developer.clarivate.com/apis/wos-starter/swagger
[12]: https://developer.clarivate.com/apis/wos-starter
[13]: https://developer.clarivate.com/apis/sushi-status-api
[14]: https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/Modules/AcademicPerformance/Service/Integrations/Yoksis/Collection/YoksisOperationCatalog.cs
[15]: https://servisler.yok.gov.tr/ws/OzgecmisV2?wsdl
[16]: https://github.com/Efeblk/researcher/blob/e9519b3d2e1a2da8f716bb6cad8c6a31fa87fe1e/ResearcherAnalysisService/Program.cs
[17]: https://docs.ollama.com/api/tags
[18]: https://docs.ollama.com/api/ps
[19]: https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage
[20]: https://developers.openai.com/api/reference/ruby/resources/admin/subresources/organization/subresources/projects/subresources/rate_limits/methods/list_rate_limits
[21]: https://developers.openai.com/api/reference/overview
[22]: https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/admin_api_keys
