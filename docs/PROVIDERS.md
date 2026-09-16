# Sağlayıcılar

Bu belge collector'ın akademik veri entegrasyonlarının sabit kapsamını özetler. Fiyat, kota ve paketler değişebilir; production ayarından önce bağlantılı resmî kaynak ve gerçek hesap ekranı doğrulanmalıdır. Collector varsayılanları için [`academicsettings.json`](../academicsettings.json) esas alınır. Analysis Service'in AI sağlayıcıları ve model ayarları [kendi README dosyasında](../ResearcherAnalysisService/README.md) açıklanır.

| Sağlayıcı | Projedeki kapsam | Temel sınır |
| --- | --- | --- |
| ORCID | Açık profil, faaliyet ve eser | Atıf, h-index ve i10-index sağlamaz. |
| OpenAlex | ORCID eşleşmeli profil, yayın ve kendi metrikleri | Scholar metriği değildir; aday eşleşme kusursuz olmayabilir. |
| Scopus | Scopus Author ID ile Author Retrieval profili/metrikleri ve `AU-ID(...)` Search yayınları | API anahtarı gerekir; `InstToken` kurumsal erişim gerektiğinde eklenir. Search ve Author Retrieval kotaları ayrıdır. |
| Google Scholar / SearchApi | Profil, yayın, atıf, h/i10 | Google'ın resmî API'si değildir; entegrasyon varsayılan ayarda kapalıdır. |
| Web of Science Starter | ResearcherID ile WOS/WOK yayınları ve varsa atıf | h-index yalnız tüm gerekli atıflar geldiyse yerelde hesaplanır; i10 yoktur. |
| YÖKSİS | 21 kategori ve desteklenen eser ayrıntıları | Kurumsal kimlik, T.C. kimlik no ve ayrıca BYS yetkisi gerekir. |
| TR Dizin | Tam ORCID eşleşmeli yazar ve yayın | Yayımlanmış kota doğrulanmamıştır. |
| Crossref | DOI metaverisi/atıf ve pozitif-negatif önbellek | Araştırmacı girdi alanı değildir; polite pool için iletişim e-postası önerilir. |
| Semantic Scholar | DOI metaverisi, atıf bağlamları, TLDR ve açık PDF adayı | Atıf yapan eserler araştırmacının yayın listesine eklenmez. |

`PersonelID`, `TcKimlikNo`, `ORCID`, Web of Science `ResearcherID`, Google Scholar ID ve Scopus ID yalnız kullanıcı veya içe aktarma satırında açıkça verilen değerlerden kaydedilir. Sağlayıcı yanıtlarında görülen kimlikler profil ve ham kaynak metadatasında korunabilir, ancak araştırmacının kanonik kimlik sütunlarını doldurmaz, değiştirmez veya temizlemez. Yalnız `TcKimlikNo` verilen bir toplama isteği sadece YÖKSİS'i çalıştırır; diğer sağlayıcılar için kimlikler ayrıca verilmelidir.

ORCID toplaması OpenAlex ve TR Dizin'i de tetikler; Scopus yalnız doğrulanmış `ScopusID` ile çalışır. DOI bulunan ortak eserler normal `Collect` ve bulk akışında Crossref ile zenginleştirilir. Semantic Scholar bu akışlarda otomatik çağrılmaz; kayıtlı DOI'ler için yalnız `CollectSemanticScholar` işlemiyle açıkça çalıştırılır. Daha önce kaydedilmiş Semantic Scholar verileri ve eser kaynakları normal toplama sırasında korunur. Sağlayıcı metrikleri kaynak adıyla ayrı tutulur. `Collect` sağlayıcı ve eser verisini kaydeder; `RecalculateMetrics` dış HTTP çağrısı yapmadan kayıtlı tam profillerden metrik alanlarını günceller. OpenAlex, Google Scholar ve Scopus değerleri sağlayıcının raporladığı biçimde korunur; WOS h-index ve toplam atıf yalnız kayıtlı WOS eserlerinden hesaplanır ve eksik bir atıf değeri sonucu `null` yapar. ResearchGate/Academia.edu scraping'i kapsam dışıdır.

`CollectSemanticScholar`, tamamı başarısız sağlayıcı çağrısında `503`, önceki DOI'ler kaydedildiyse `200` ve `HasPendingWork=true` döndürür. `ErrorCode`, `ProviderHttpStatusCode`, `RetryAt` ve `Retryable` alanları güvenli yeniden deneme tanısı sağlar; yerel erteleme ve taşıma hatalarında sağlayıcı HTTP durumu `null` kalır.

`make collect` yalnız `Collect` çağrısını yapar. Tekil komut satırı akışında ardından `Requests/AcademicCollector/AcademicPerformance.http` içindeki `RecalculateMetrics` ve `GetResearcher` adımlarını çalıştırın.

`RecalculateMetrics` normal JSON yanıtını korur. Aynı istekte `Accept: application/x-ndjson`
gönderildiğinde kilit bekleme, yükleme, sağlayıcı hesaplama, kaydetme ve commit aşamaları ile
bağlantı heartbeat olayları satırlarla ayrılmış JSON olarak iletilir. Heartbeat bağlantının açık
olduğunu gösterir; `LastProgressElapsedSeconds` son gerçek aşama değişiminden geçen süredir.

Scopus için `Scopus:ApiKey` zorunludur; kurumsal abonelik gerekiyorsa `Scopus:InstToken` da user-secrets veya güvenli deployment yapılandırmasından verilir. İstekler yalnız yapılandırılmış `Scopus:ApiBaseUrl` altında oluşturulur ve sırlar `X-ELS-APIKey` / `X-ELS-Insttoken` başlıklarında taşınır. Search sonuçları 25 kayıtlık COMPLETE sayfaları ve cursor ile, `Scopus:MaximumPages` sınırına kadar alınır. Sayfa eksik veya hatalıysa önceki tam profil ve yayınlar değiştirilmez.

## Durum ve kota anlamı

`GET /Services/AcademicPerformance/V1/ProviderStatus` en son sağlık/kota gözlemlerini özetler. Dört kavram ayrı değerlendirilmelidir:

- erişilebilirlik: endpoint'e bağlantı ve yanıt;
- hesap yetkisi: anahtarın ilgili servise erişimi;
- sağlayıcının bildirdiği kota: yalnız belgelenmiş yanıt alanı/başlığı;
- yerel bütçe: bu uygulamanın SQL sayaçları ve ayarı.

Yerel sayaç sağlayıcı hesabının kalan kotası değildir. Eksik değer sıfır veya sınırsız sayılmaz. Önbelleğe alınmış gözlemin zamanı ve stale durumu korunur. Sağlık kontrolünün başarılı olması veri toplama yetkisini veya tüm sağlayıcıların sağlıklı olduğunu kanıtlamaz. Gerçek hesaplarla uçtan uca test edilmedikçe fixture sonuçları production kanıtı olarak sunulmamalıdır.

## Güvenlik ve işletim

Anahtar, parola, T.C. kimlik numarası, ham kişisel yanıt ve istek URL'leri loglara veya repoya yazılmamalıdır. Daha önce kaydedilmiş hassas YÖKSİS yanıtları yeni kod tarafından otomatik temizlenmez. Kaynak eser ID'si olmayan artımlı YÖKSİS kayıtlarında içerik değişikliği eski sürümü koruyabilir; bu kayıtlar DOI/başlık tekilleştirmesine rağmen gerektiğinde ayrıca incelenmelidir.

YÖKSİS Basic Authentication yalnız Base64 kodlar; HTTPS korunmalı ve endpoint dış ağa BYS kontrolü olmadan açılmamalıdır. Uzak servis şartları, saklama hakları, kota ve kurumsal lisanslar deployment sahibi tarafından teyit edilmelidir.

Resmî başvurular: [ORCID API](https://info.orcid.org/documentation/integration-guide/orcid-api-guide/) · [OpenAlex API](https://docs.openalex.org/) · [Scopus APIs](https://dev.elsevier.com/) · [SearchApi Scholar Author](https://www.searchapi.io/docs/google-scholar-author) · [WoS Starter](https://developer.clarivate.com/apis/wos-starter) · [YÖKSİS WSDL](https://servisler.yok.gov.tr/ws/OzgecmisV2?wsdl) · [TR Dizin](https://development.trdizin.gov.tr/) · [Crossref REST API](https://www.crossref.org/documentation/retrieve-metadata/rest-api/) · [Semantic Scholar API](https://www.semanticscholar.org/product/api)
