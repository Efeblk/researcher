# Analysis Service HTTP örnekleri

Servisi `dotnet run --project ResearcherAnalysisService` ile başlatın.

Günlük sekiz işlem [Researchers.http](Researchers.http) ve [Articles.http](Articles.http) içindedir. Birleşik araştırmacı okumasında `Analysis=null`, araştırmacının bulunduğunu fakat rapor üretilmediğini belirtir. Birleşik kanonik makale okumasında `Evidence` ve `Review` ayrı ayrı null olabilir; güncel ilişki yoksa yanıt `404` olur. [Service.http](Service.http) iki tanı/metadata çağrısını gösterir.

On dört uzman ürün işlemi `Specialist` altında grupludur; profil keşfi bunlara ek metadata işlemidir. Böylece API yüzeyi 8 günlük + 14 uzman ürün + profil keşfi + sağlayıcı tanısı olmak üzere 24 işlemdir; `/health` bu sayıya dahil değildir.

- [Knowledge.http](Specialist/Knowledge.http): kanıt arama, referans popülasyonu ve grafik dışa aktarımı.
- [Evaluations.http](Specialist/Evaluations.http): profil keşfi, kalıcı değerlendirme kuyruğu ve sonuçları.
- [Hr.http](Specialist/Hr.http): İK kanıt dosyaları ve inceleme işlemleri.
- [Faculty.http](Specialist/Faculty.http): fakülte bağlamı ve asistan kuyruğu.

Bu değişiklik kırıcıdır: `/products` öneki ve yinelenen stateless üretim uçları kaldırılmıştır; gövdeli okumalar `POST` olarak kalır. Uzman işlemler güvenilir ürün erişim adaptörü de ister. Okumalar model çağrısı yapmaz; üretim ve worker işlemleri ücretli sağlayıcı kullanabilir ve örnekler otomatik çalıştırılmaz.
