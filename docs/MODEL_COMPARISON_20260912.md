# Model karşılaştırması — 12 Eylül 2026

> Durum: Kontrollü kalibrasyon ve eşlenmiş gerçek-makale pilot deneyi tamamlandı; pilotta iki model de final inceleme raporu üretemedi.

Kontrollü çalışma, `Europe/Istanbul` saat diliminde 11 Eylül gecesi başlayıp 12 Eylül 2026'da devam etti. Belge tarihi tamamlanma gününü esas alır.

## Soru ve kapsam

Bu karşılaştırma, altı yapılandırılmış modelin aynı küçük kontrollü kaynak-okuma görevindeki davranışını inceler. Bilimsel doğruluğu, bütün bulguların keşfini, makale kalitesini veya personel kararlarına uygunluğu ölçmez. 18 iddialı kalibrasyon paketi kasıtlı olarak sentetiktir ve gerçek bilimsel yayın evrenini temsil etmez.

Çalışmada aşağıdaki istenen model kimlikleri karşılaştırılır:

| Sağlayıcı | İstenen model | Sonuç durumu |
| --- | --- | --- |
| Gemini | `gemini-3.8-flash` | 6/6 vaka tamamlandı |
| Gemini | `gemini-3.5-flash-lite` | 6/6 vaka tamamlandı |
| Gemini | `gemini-3.1-pro-preview` | 6/6 vaka tamamlandı |
| Ollama | `qwen3.8:27b-q4_K_M` | 5/6 vaka tamamlandı; 1 timeout |
| Ollama | `qwen3.5:9b` | 5/6 vaka tamamlandı; 1 çıktı-sınırı tükenmesi |
| Ollama | `gemma4:12b-it-q4_K_M` | 6/6 vaka tamamlandı |

Bu deneyin hiçbir sonucu üretim varsayılan sağlayıcısını, modelini veya profilini değiştirmez.

## Donmuş kalibrasyon yöntemi

Çalıştırıcı, `controlled-source-reading-v1-section-correction-1` veri kümesi etiketini kullanır. Altı kaynak metni, 18 iddiası, beklenen kararları ve iddia bölümleri üretim kataloğu `controlled-source-reading-v2` ile aynıdır. Ayrı etiket, çalıştırıcının üretim sürümü güncellenmeden önce bağımsız olarak dondurulduğunu kaydeder.

Altı vakanın her birinde üç iddia vardır. Referans etiket kümesi dengelidir: altı `supported`, altı `unsupported` ve altı `uncertain`. Etiketler doğrulayıcı politikasını izler: doğrudan ve eksiksiz destek `supported`, çelişki `unsupported`, eksik veya belirsiz destek `uncertain` olur. Bu ayrım “azaltmadı” ile “artırdı” gibi vakalarda önemlidir: ikinci ifade kanıtlanmaz, ancak ilk ifadeyle çelişmez.

Her model aynı kontrollü kaynak span'lerini ve iddia metinlerini alır. İddia sırası deterministik olarak karıştırılır; özgün iddia kimlikleri çalışmaya özgü opak kimliklerle değiştirilir. Vaka kimlikleri, beklenen kararlar ve türetim notları sağlayıcı isteğinin dışında kalır. İstem, gömülü istem-enjeksiyonu cümlesi dahil kaynak metni ve iddiaları güvenilmeyen kanıt olarak ele alır.

| Ayar | Gemini | Yerel Ollama |
| --- | --- | --- |
| Çalıştırıcı giriş/context kabul bütçesi | 32.768 token | 32.768 token (`num_ctx`) |
| En yüksek çıktı | 4.096 token | 4.096 token |
| Temperature | 0 | 0 |
| Akıl yürütme modu | `high` | `think=true` |
| Çağrı timeout değeri | 180 saniye | 180 saniye |
| Otomatik tekrar | 0 | 0 |

32.768 token değeri Gemini tarafında test düzeneğinin giriş kabul bütçesidir; sağlayıcı isteği ayrıca bir context-window parametresi göndermez. Ollama isteği aynı sayıyı `num_ctx` olarak gönderir. Bu nedenle tablo özdeş çıkarım pencereleri olduğunu ileri sürmez. Sağlayıcıların akıl yürütme uygulamaları da özdeş değildir. Süre, her yapılandırılmış uçtan uca yolu temsil eder; yerel ham yükleme, istem değerlendirme ve üretim süreleri bulunabildiğinde ayrıca raporlanır.

Yerel modeller AMD Ryzen 7 7800X3D, 31,19 GiB RAM ve 12 GB VRAM'li NVIDIA GeForce RTX 4070 SUPER üzerinde Ollama `0.34.0` ile çalıştı. Qwen 27B'nin ilk tamamlanan çağrısı 111,282 saniyeydi: 18,849 saniye model yükleme, 1,836 saniye istem değerlendirme ve 90,025 saniye üretim. Yerel hız karşılaştırmaları bu donanım ve yerleşim koşullarına bağlıdır.

## Puanlama ve belirsizlik

Rapor, dönen iddialardaki sınıflandırmayı operasyonel kapsamdan ayırır. Dönen iddia doğruluğu yalnızca geçerli olarak dönen etiketleri kullanır ve mevcut sonuçların ne sıklıkla doğru sınıflandırıldığını gösterir. Planlanan iddia kapsamı, 18 iddianın kaçının geçerli etiket aldığını gösterir. Tamamlanan vaka sayısı ve medyan gecikme yalnız tamamlanan isteklerden hesaplanır.

Başarısızlık-dahil kalibrasyon doğruluğu, ikincil bir operasyonel ölçüm olarak planlanan 18 iddianın tümünü paydada tutar. Başarısız vaka, eksik karar, yinelenen kimlik, geçersiz etiket veya geçersiz yapılandırılmış yanıt paydadan sessizce çıkamaz. Karışıklık matrisi `supported`, `unsupported`, `uncertain` ile başarısız veya eksik sonuçları ayrı tutar. Timeout operasyonel bir başarısızlıktır ve eksik etiket doğurur; bilimsel olarak yanlış bir sınıflandırma diye anlatılmaz. Benzer biçimde 4.096 token sınırında tamamlanmayan üretim, bu sabit yüksek-akıl-yürütme/çıktı bütçesi altındaki operasyonel tükenmedir; tek başına modelin genel yeteneği veya bilimsel doğruluğu hakkında hüküm değildir.

Tek bir yanıt görünen oranı 5,56 yüzde puan değiştirir. Aşağıdaki Wilson %95 aralıkları belirsizliğin büyüklüğünü göstermek için verilen kaba örneklerdir:

| Doğru | Oran | Wilson %95 aralığı |
| ---: | ---: | ---: |
| 18/18 | %100,00 | %82,41–100,00 |
| 17/18 | %94,44 | %74,24–99,01 |
| 16/18 | %88,89 | %67,20–96,90 |

İddialar bağımsız 18 örnek değildir; üçerli olarak altı vaka içinde kümelenmiştir. Bu aralıklar biçimsel anlamlılık kanıtı veya modeller arası hipotez testi değildir. Küçük puan farkları kesinleşmiş bir model sıralaması olarak sunulmaz.

## Kontrollü kalibrasyon sonuçları

| Model | Doğru / dönen | Planlanan kapsam | Tamamlanan vaka / 6 | Başarısızlık-dahil / 18 | Tamamlanan istek medyanı | Başlıca karışıklık veya hata |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `gemini-3.8-flash` | 16/18 (%88,89) | 18/18 (%100) | 6/6 | 16/18 (%88,89) | 4,0095 sn | 2 `uncertain→unsupported` |
| `gemini-3.5-flash-lite` | 14/18 (%77,78) | 18/18 (%100) | 6/6 | 14/18 (%77,78) | 3,336 sn | 4 `uncertain→unsupported` |
| `gemini-3.1-pro-preview` | 15/18 (%83,33) | 18/18 (%100) | 6/6 | 15/18 (%83,33) | 6,8985 sn | 3 `uncertain→unsupported` |
| `qwen3.8:27b-q4_K_M` | 13/15 (%86,67) | 15/18 (%83,33) | 5/6 | 13/18 (%72,22) | 131,914 sn | 2 `uncertain→unsupported`; `en-condition` timeout |
| `qwen3.5:9b` | 12/15 (%80,00) | 15/18 (%83,33) | 5/6 | 12/18 (%66,67) | 42,470 sn | 3 `uncertain→unsupported`; `en-negation` 4.096-token sınırı |
| `gemma4:12b-it-q4_K_M` | 15/18 (%83,33) | 18/18 (%100) | 6/6 | 15/18 (%83,33) | 13,7055 sn | 3 `uncertain→unsupported` |

Timeout sonuçları raporda normalize edilmiş `timed_out` koduyla gösterilir; sağlayıcı adaptörünün ham `cancelled` durumu temizlenmiş denetim eserinde korunur.

Geçerli olarak dönen bütün hatalı etiketler `uncertain→unsupported` yönündeydi. Altı model de bu küçük pakette yanlış `supported` üretmedi. Bu, modellerin yalnızca bu örneklerde belirsiz kanıtı çelişki diye sınıflandırmaya eğilim gösterdiğini söyler; daha geniş bir güvenlik veya doğruluk iddiası değildir.

`gemini-3.8-flash`, tam operasyonel kapsamla en yüksek ham doğru sayısını (16/18) ve 4,0095 saniyelik medyanı birlikte verdiği için daha geniş bulut değerlendirmesi için güçlü başlangıç adayıdır. `gemma4:12b-it-q4_K_M`, 15/18 ve 6/6 tamamlanmayla yerel adaylar içinde en dengeli sonucu verdi; tamamlanan-istek medyanı `qwen3.8:27b-q4_K_M` medyanından bu donanım ve ayarlarda yaklaşık 9,6 kat kısaydı. Örneklem ve aralıklar kesin bir kalite kazananı ilan etmeye yetmez.

## Kullanım ve maliyet

| Model | Giriş tokenı | Çıkış tokenı | Altı vaka maliyeti |
| --- | ---: | ---: | ---: |
| `gemini-3.8-flash` | 3.389 | 5.384 | $0,02273175 tahmin |
| `gemini-3.5-flash-lite` | 3.389 | 6.575 | $0,01745420 çevrimdışı tahmin |
| `gemini-3.1-pro-preview` | 3.389 | 3.393 | $0,04749400 çevrimdışı tahmin |
| `qwen3.8:27b-q4_K_M` | Toplam `Unknown`; tamamlanan çağrılar 2.832 | Toplam `Unknown`; tamamlanan çağrılar 2.714 | `Unknown` |
| `qwen3.5:9b` | 3.407 | 18.221 | `Unknown` |
| `gemma4:12b-it-q4_K_M` | 3.479 | 4.079 | `Unknown` |

Gemini değerleri sağlayıcı kullanım telemetrisinden gelir. Tutarlar [Gemini API fiyatlandırması](https://ai.google.dev/gemini-api/docs/pricing) temel alınarak yerelde tutulan `docs/benchmarks/20260912/gemini-rate-snapshot.json` oran anlık görüntüsüyle hesaplandı. `gemini-3.8-flash` tutarı üretim fiyat anlık görüntüsünün tahminidir; diğer iki Gemini tutarı aynı çalıştırmanın kullanımına uygulanan çevrimdışı tahminlerdir ve fatura değildir. Yerel Ollama çalıştırmalarının bu deneyde sağlayıcı faturası yoktur; parasal maliyetleri sıfır değil `Unknown` değeridir. Qwen 27B timeout çağrısı kullanım telemetrisi döndürmediği için 2.832/2.714 değerleri kesin olarak yalnızca beş tamamlanmış çağrının ara toplamıdır; altı çağrının toplamı bilinmez. Token sayıları ile yerel süre telemetrisi operasyonel ölçümlerdir ve yanıt kalitesini kanıtlamaz.

## Eşlenmiş gerçek-makale pilotu

Eşlenmiş pilot, `gemini-3.8-flash` ve `qwen3.8:27b-q4_K_M` modellerine aynı değişmez, temizlenmiş ve yalnızca abstract içeren 1.933 baytlık kaynağı verdi. Kaynak beş doğrulanmış span içeriyordu ve kapsamı açıkça kısmiydi. Bağımsız uzman etiketli bulgular bulunmadığı için `ScientificAccuracy` ve eksik-bulgu recall değeri `null` kaldı.

| Model | Üretim aşaması | Doğrulama aşaması | Uçtan uca süre | Bilinen kullanım ve maliyet | Son rapor |
| --- | --- | --- | ---: | --- | --- |
| `gemini-3.8-flash` | Method üretimi tamamlandı, 6,407 sn | 11,656 sn sonra `invalid_provider_response` | 19,138 sn | 1.831 giriş; thinking dahil 5.871 çıkış; 5.242 thinking; $0,02338950 tahmin | Üretilmedi |
| `qwen3.8:27b-q4_K_M` | Method üretimi tamamlandı, 87,643 sn | 90,342 sn sonra timeout | 180,018 sn | Üretimde 772 giriş/267 çıkış; doğrulama ve toplam kullanım `Unknown`; maliyet `Unknown` | Üretilmedi |

Bir rolün tamamlanması hem üretim hem doğrulama geçişini gerektirir. İki model de method doğrulamasında durduğu için tamamlanmış rol, final bulgu raporu veya puanlanabilir kanıt kümesi üretmedi. Kayıtta sıfır kanıt referansı bulunması %0 ya da %100 kanıt doğruluğu anlamına gelmez; değerlendirilecek rapor bulunmadığını gösterir.

Gemini üretimi başarıyla dönmüş, ardından doğrulayıcı geçersiz yanıt vermiştir. Kesin doğrulayıcı hata nedeni bilinmemektedir. Çıktının 4.096 token rezervine yaklaşması, tek başına çıktı sınırının tükendiğini kanıtlamaz. Qwen üretimi başarıyla dönmüş, doğrulama çağrısı kullanım telemetrisi vermeden timeout olmuştur. İki aday için de tekrar yapılmamıştır.

Donmuş çalıştırıcıdaki telemetri kaydedicisi, daha sonraki doğrulayıcı zaten başarısız olmuşken geriye doğru önceki `completed` denemeyi bulup Gemini üretimini de hatalı biçimde `failed` işaretledi. Aşama-dönüş kanıtıyla üretilen denetim kaydı ilk denemeyi `completed` olarak düzeltir. Üretim kodundaki düzeltme yalnızca telemetriyi en son başlatılan denemeye bağlar; model çıktısını veya model kalitesini iyileştirmez.

Bir sonraki kabul kapısı, model yönlendirme kararı vermeden önce kontrollü bütçelerle gerçek-inceleme akışını tamamlamak ve doğrulama başarısızlıklarının kesin nedenini yapılandırılmış biçimde korumaktır. Modeller arası uyum ancak tamamlanmış raporlar olduğunda insan incelemesi için anlaşmazlığı görünür kılabilir; gerçeği kanıtlamaz.

Kontrollü çalışmanın yerel `calibration-summary.json` özeti 36 planlı çağrının 36'sının denendiğini, 36 benzersiz model-vaka çifti bulunduğunu, her vaka için tek sağlayıcı-payload hash'i kullanıldığını ve dönen model uyuşmazlığı olmadığını kaydeder. Aynı yerel benchmark dizinindeki vaka manifesti, vaka sonuçları, Ollama süreleri ve eşlenmiş-pilot özeti denetim için korunur. Bu makinece okunabilir kanıt dosyaları kimlik bilgisi, user-secret içeriği, ham hata stack'i veya makineye özgü yerel veritabanı yolu içermez; ham sağlayıcı ve maliyet dökümleriyle birlikte kaynak denetimine eklenmez.

Kod düzeltmeleri `ResearcherAnalysisService.Tests` içinde 190/190 ve `AcademicCollectorDemo.Tests` içinde 362/362 testten geçti. Yerel `docs/benchmarks/20260912/artifact-index.json`, çalışma dosyalarının SHA-256 değerlerini ve kaynak eser hash'lerini kaydeder.

## Yorum sınırları

Kontrollü sonuçlar ve olası öneriler yalnızca bu sabit kaynak-okuma paketi ile 4.096 çıktı tokenlı yüksek-akıl-yürütme yapılandırması hakkında çıkarımı destekler. Gerçek-makale pilotu, tek bir kayıtlı kaynaktaki çıktı biçimini ve anlaşmazlığı gösterebilir. İkisi de genel bilimsel kalite iddiasını, küçük bir modelin genel olarak yetersiz olduğu sonucunu, evrensel bir kazananı, İK kararını veya otomatik üretim-varsayılanı değişikliğini desteklemez.
