# Toplu toplama

Toplu modül, araştırmacı satırlarını SQL Server kuyruğuna kaydeder; HTTP isteği batch kimliğiyle döner ve arka plan worker'ı mevcut çoklu sağlayıcı toplama akışını çalıştırır.

```text
JSON veya operatörün sabit SQL sorgusu → doğrulama → bulk kuyruğu
→ worker → ortak toplama servisi → sağlayıcı hız sınırı → kayıtlı sonuçlar → metrik hesaplama
```

## Girdi ve API

Her satır zorunlu, benzersiz `PersonelID` ile isteğe bağlı `ORCID`, `ResearcherID` (Web of Science), `ScholarID`, `ScopusID` ve `TcKimlikNo` alanlarını taşır. En az bir desteklenen kimlik gerekir. ScopusID 5–20 ASCII rakamdan oluşur; Scopus yazar bağlantısı da kanonik kimliğe çevrilebilir. `TcKimlikNo` sağlandığında YÖKSİS de ortak toplama akışında çalışır.

Worker, kayıt başarılı olduktan sonra aynı uygulama servisi üzerinden metrikleri hesaplar. Bu adım başarısız olursa iş başarılı sayılmaz ve mevcut retry politikası uygulanır. Tekil HTTP istemcileri ise `Collect` sonrasında `RecalculateMetrics`, ardından `GetResearcher` çağırır.

```json
{
  "BatchId": "a3700086-8ad7-4871-becf-0fbd2ce586e3",
  "Researchers": [{ "PersonelID": "employee-001", "ScopusID": "57200000001" }]
}
```

`/Services/AcademicPerformance/V1/Bulk/` altında üç POST işlemi vardır:

| İşlem | Davranış |
| --- | --- |
| `Submit` | En çok 10.000 JSON satırını doğrular ve kaydeder. |
| `ImportSql` | Operatörün yapılandırdığı salt okunur SQL sorgusunu çalıştırır. İstek SQL metni kabul etmez. |
| `Status` | Batch sayaçları ile sayfalı iş sonuçlarını döndürür; sayfa üst sınırı 500'dür. |

Normalizasyon yalnız tanınan ORCID/WoS/Scholar URL ve yazım biçimlerini kabul eder; tahmin, doldurma veya alanlar arası taşıma yapmaz. Özgün satır denetim için, kanonik kopya worker için saklanır. Geçersiz alanlar diğer geçerli sağlayıcıları engellemez. Kimliksiz, tekrarlı `PersonelID` içeren veya aynı sağlayıcı kimliğini farklı personele bağlayan satırlar `Rejected` olur. Aynı `BatchId` ve aynı sıralı içerik idempotenttir; farklı içerik reddedilir.

Manuel akış örnekleri [BulkCollection.http](../Requests/AcademicCollector/BulkCollection.http) ve [BulkSubmitPayload.sql](../Requests/AcademicCollector/BulkSubmitPayload.sql) dosyalarındadır.

## SQL içe aktarma ve worker

Birincil akışta dış sistem kendi verisini sorgulayıp `Submit` çağırır; uygulamanın kaynak veritabanı erişimine ihtiyacı yoktur. `ImportSql` kullanılacaksa `ConnectionStrings:BulkSource` yalnız gerekli `SELECT` yetkisine sahip hesap olmalı, sorgu `BulkSqlSource:Query` ile deployment sırasında sabitlenmelidir.

Worker kayıtlı varsayılanlarda etkin, SQL içe aktarma kapalıdır. Etkin ayarların kaynağı [`academicsettings.json`](../academicsettings.json) dosyasıdır. Worker veya hız ayarlarını değiştirdikten sonra hostu yeniden başlatın; `Status.WorkerEnabled` etkin değeri gösterir.

`ProviderRequestLimits:<Provider>` altında `Enabled`, `MinimumIntervalMilliseconds`, `DailyRequestLimit` ve isteğe bağlı `RateLimitCooldownSeconds` bulunur. SQL tabanlı sayaç, cooldown ve uygulama kilitleri aynı veritabanını kullanan instance'lar arasında paylaşılır. Bunlar sağlayıcının gerçek hesap kotasını ölçmez; başka uygulamaların kullanımını göremez. Her sayfa ve ayrıntı çağrısı bütçe tüketir, önbellekten karşılanan kayıt tüketmez. Scopus varsayılanı çağrılar arasında 400 ms bekler; her Author Retrieval ve Search sayfası ayrı istek sayılır. OpenAlex için yapılandırılmış API anahtarı 10.000 istek/gün yerel güvenlik tavanını etkinleştirir; anahtar yoksa etkili yerel tavan 1.000'dir. Bu sayaç OpenAlex'in kredi tabanlı gerçek günlük bütçesi veya hesap hakkı değildir; anahtarın sağladığı günlük bütçe artışı [OpenAlex kimlik doğrulama belgelerinde](https://help.openalex.org/api/authentication) açıklanır. YÖKSİS çağrıları varsayılan olarak beş saniye aralıklıdır; HTTP 429 yanıtı en az beş dakikalık ortak cooldown oluşturur ve daha uzun `Retry-After` değeri korunur. SearchApi desteklenir ancak varsayılan ayarda kapalıdır; gerçek plan ve kalan kota doğrulanmadan açılmamalıdır.

`Retry-After` ve geçici hatalar ortak cooldown oluşturur. Kategori veya kalıcılık hatası eşlik etmeyen, bütünüyle yerel kota/cooldown ertelemeleri retry hakkını tüketmeden işi kuyruğa döndürür; diğer geçici hatalar sınırlı üstel geri çekilme kullanır. 408/429 dışındaki 4xx yanıtları otomatik tekrar edilmez.

## Durum ve kurtarma

`Pending`, `Running` ve `RetryWaiting` geçici; `Succeeded`, `Partial`, `Failed` ve `Rejected` son durumdur. Zamanlar UTC'dir. İşler araştırmacı bazında sıralı çalışır; sağlayıcılar için ayrı paralel kuyruk yoktur.

SQL oturum kilidi tek worker sahipliğini korur. Çökmeden sonra başka worker terk edilmiş `Running` işi retry sınırı içinde sürdürür. Teslimat **en az bir kez** semantiğine sahiptir: sağlayıcı sonucu kaydedilip iş tamamlandı işaretlenmeden çökülürse dış çağrı tekrarlanabilir. Son `Failed` işler otomatik başlamaz. Kuyruk kayıtları denetim için tutulur; otomatik saklama/silme ve yönetim UI'ı yoktur.

Production'da bu operasyon uçlarına BYS yetkisi uygulanmalıdır. Uzun süren işlerde sağlayıcı adı, sıra, HTTP durumu ve cooldown loglanır; kimlikler, URL'ler, ham yanıtlar ve sırlar loglanmamalıdır.
