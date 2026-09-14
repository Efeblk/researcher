# Toplu toplama

Toplu modül, araştırmacı satırlarını SQL Server kuyruğuna kaydeder; HTTP isteği batch kimliğiyle döner ve arka plan worker'ı mevcut çoklu sağlayıcı toplama akışını çalıştırır.

```text
JSON veya operatörün sabit SQL sorgusu → doğrulama → bulk kuyruğu
→ worker → ortak toplama servisi → sağlayıcı hız sınırı → kayıtlı sonuçlar
```

## Girdi ve API

Her satır zorunlu, benzersiz `PersonelID` ile isteğe bağlı `ORCID`, `ResearcherID` (Web of Science), `ScholarID` ve `ScopusID` alanlarını taşır. En az bir desteklenen kimlik gerekir. Scopus henüz toplanmaz; değer denetim amacıyla saklanır ve uyarı üretir. YÖKSİS toplu akışta yoktur.

```json
{
  "BatchId": "a3700086-8ad7-4871-becf-0fbd2ce586e3",
  "Researchers": [{ "PersonelID": "employee-001", "ORCID": "0000-0001-8560-7482" }]
}
```

`/Services/AcademicPerformance/V1/Bulk/` altında üç POST işlemi vardır:

| İşlem | Davranış |
| --- | --- |
| `Submit` | En çok 10.000 JSON satırını doğrular ve kaydeder. |
| `ImportSql` | Operatörün yapılandırdığı salt okunur SQL sorgusunu çalıştırır. İstek SQL metni kabul etmez. |
| `Status` | Batch sayaçları ile sayfalı iş sonuçlarını döndürür; sayfa üst sınırı 500'dür. |

Normalizasyon yalnız tanınan ORCID/WoS/Scholar URL ve yazım biçimlerini kabul eder; tahmin, doldurma veya alanlar arası taşıma yapmaz. Özgün satır denetim için, kanonik kopya worker için saklanır. Geçersiz alanlar diğer geçerli sağlayıcıları engellemez. Kimliksiz, tekrarlı `PersonelID` içeren veya aynı sağlayıcı kimliğini farklı personele bağlayan satırlar `Rejected` olur. Aynı `BatchId` ve aynı sıralı içerik idempotenttir; farklı içerik reddedilir.

Manuel akış örnekleri [`Requests/BulkCollection.http`](../Requests/BulkCollection.http) ve [`Requests/BulkSubmitPayload.sql`](../Requests/BulkSubmitPayload.sql) dosyalarındadır.

## SQL içe aktarma ve worker

Birincil akışta dış sistem kendi verisini sorgulayıp `Submit` çağırır; uygulamanın kaynak veritabanı erişimine ihtiyacı yoktur. `ImportSql` kullanılacaksa `ConnectionStrings:BulkSource` yalnız gerekli `SELECT` yetkisine sahip hesap olmalı, sorgu `BulkSqlSource:Query` ile deployment sırasında sabitlenmelidir.

The worker is enabled in checked-in defaults; SQL import is disabled. Effective settings are authoritative in [`academicsettings.json`](../academicsettings.json). Restart the host after changing worker or rate settings; `Status.WorkerEnabled` reports the active value.

`ProviderRequestLimits:<Provider>` altında `Enabled`, `MinimumIntervalMilliseconds` ve `DailyRequestLimit` bulunur. SQL tabanlı sayaç, cooldown ve uygulama kilitleri aynı veritabanını kullanan instance'lar arasında paylaşılır. Bunlar sağlayıcının gerçek hesap kotasını ölçmez; başka uygulamaların kullanımını göremez. Her sayfa ve ayrıntı çağrısı bütçe tüketir, önbellekten karşılanan kayıt tüketmez. SearchApi desteklenir ancak varsayılan ayarda kapalıdır; gerçek plan ve kalan kota doğrulanmadan açılmamalıdır.

`Retry-After` ve geçici hatalar ortak cooldown oluşturur. Yerel kota/cooldown ertelemeleri retry hakkını tüketmeden işi kuyruğa döndürür; diğer geçici hatalar sınırlı üstel geri çekilme kullanır. 408/429 dışındaki 4xx yanıtları otomatik tekrar edilmez.

## Durum ve kurtarma

`Pending`, `Running` ve `RetryWaiting` geçici; `Succeeded`, `Partial`, `Failed` ve `Rejected` son durumdur. Zamanlar UTC'dir. İşler araştırmacı bazında sıralı çalışır; sağlayıcılar için ayrı paralel kuyruk yoktur.

SQL oturum kilidi tek worker sahipliğini korur. Çökmeden sonra başka worker terk edilmiş `Running` işi retry sınırı içinde sürdürür. Teslimat **en az bir kez** semantiğine sahiptir: sağlayıcı sonucu kaydedilip iş tamamlandı işaretlenmeden çökülürse dış çağrı tekrarlanabilir. Son `Failed` işler otomatik başlamaz. Kuyruk kayıtları denetim için tutulur; otomatik saklama/silme ve yönetim UI'ı yoktur.

Production'da bu operasyon uçlarına BYS yetkisi uygulanmalıdır. Uzun süren işlerde sağlayıcı adı, sıra, HTTP durumu ve cooldown loglanır; kimlikler, URL'ler, ham yanıtlar ve sırlar loglanmamalıdır.
