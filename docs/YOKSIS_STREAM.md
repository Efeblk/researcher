# YÖKSİS canlı toplama akışı

`POST /Services/AcademicPerformance/V1/Yoksis/CollectStream` satırlarla ayrılmış
JSON (`application/x-ndjson`) döndürür. Kategori ve ayrıntı olayları gerçek işlem
ilerlemesini bildirir. Heartbeat yalnızca sunucu bağlantısının açık olduğunu
gösterir ve son gerçek ilerleme zamanını değiştirmez. Son satır `result` veya
`error` olur.

Kayıtlar toplama aşaması bittikten sonra tek veritabanı işlemi içinde yazılır.
İstemci bağlantıyı kapattığında aktif HTTP ve kayıt işlemleri iptal edilir.
`Yoksis:RequestTimeoutSeconds` her sağlayıcı HTTP çağrısını varsayılan 100
saniyeyle sınırlar; bütün toplamanın toplam süre sınırı değildir.
