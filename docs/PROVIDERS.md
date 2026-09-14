# Sağlayıcılar

Bu belge entegrasyonların sabit kapsamını özetler. Fiyat, kota ve paketler değişebilir; production ayarından önce bağlantılı resmî kaynak ve gerçek hesap ekranı doğrulanmalıdır. Uygulama varsayılanları için [`academicsettings.json`](../academicsettings.json) esas alınır.

| Sağlayıcı | Projedeki kapsam | Temel sınır |
| --- | --- | --- |
| ORCID | Açık profil, faaliyet ve eser | Atıf, h-index ve i10-index sağlamaz. |
| OpenAlex | ORCID eşleşmeli profil, yayın ve kendi metrikleri | Scholar metriği değildir; aday eşleşme kusursuz olmayabilir. |
| Google Scholar / SearchApi | Profil, yayın, atıf, h/i10 | Google'ın resmî API'si değildir; entegrasyon varsayılan ayarda kapalıdır. |
| Web of Science Starter | ResearcherID ile WOS/WOK yayınları ve varsa atıf | h-index yalnız tüm gerekli atıflar geldiyse yerelde hesaplanır; i10 yoktur. |
| YÖKSİS | 21 kategori ve desteklenen eser ayrıntıları | Kurumsal kimlik, T.C. kimlik no ve ayrıca BYS yetkisi gerekir. |
| TR Dizin | Tam ORCID eşleşmeli yazar ve yayın | Yayımlanmış kota doğrulanmamıştır. |
| Crossref | DOI metaverisi/atıf ve pozitif-negatif önbellek | Araştırmacı girdi alanı değildir; polite pool için iletişim e-postası önerilir. |
| Semantic Scholar | DOI metaverisi, atıf bağlamları, TLDR ve açık PDF adayı | Atıf yapan eserler araştırmacının yayın listesine eklenmez. |
| Unpaywall | Makale analizi için DOI ile açık erişim kaynak adayı | Yalnız geçerli `Unpaywall:Email` yapılandırıldığında çağrılır. |

ORCID toplaması OpenAlex ve TR Dizin'i de tetikler; DOI bulunan ortak eserler Crossref ve Semantic Scholar ile zenginleştirilebilir. Sağlayıcı metrikleri kaynak adıyla ayrı tutulur. ResearchGate/Academia.edu scraping'i ve Scopus toplaması kapsam dışıdır.

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

Resmî başvurular: [ORCID API](https://info.orcid.org/documentation/integration-guide/orcid-api-guide/) · [OpenAlex API](https://docs.openalex.org/) · [SearchApi Scholar Author](https://www.searchapi.io/docs/google-scholar-author) · [WoS Starter](https://developer.clarivate.com/apis/wos-starter) · [YÖKSİS WSDL](https://servisler.yok.gov.tr/ws/OzgecmisV2?wsdl) · [TR Dizin](https://development.trdizin.gov.tr/) · [Crossref REST API](https://www.crossref.org/documentation/retrieve-metadata/rest-api/) · [Semantic Scholar API](https://www.semanticscholar.org/product/api) · [Unpaywall](https://unpaywall.org/products/api)
