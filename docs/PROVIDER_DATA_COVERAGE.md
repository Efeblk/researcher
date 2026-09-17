# Sağlayıcı veri kapsamı

Bu belge collector'ın hangi verileri istediğini, sağlayıcı kanıtı olarak neyi sakladığını ve hangi alanları yapılandırılmış biçimde sunduğunu gösterir. Her sağlayıcının sunduğu bütün alanların toplandığı anlamına gelmez. Yayın türlerinin ortak sınıflandırması [PUBLICATION_TYPES.md](PUBLICATION_TYPES.md) belgesindedir.

| Sağlayıcı | Toplanan ve saklanan veri | Yapılandırılmış çıktı | Mevcut sınır |
| --- | --- | --- | --- |
| ORCID | Açık `/record`, tercih edilen her eserin tam kaydı ve employment, education, qualification, invited position, distinction, membership, service, funding, peer review ve research resource özetlerindeki bütün açık put-code ayrıntıları. Özgün kayıt ve ayrıntı JSON'u saklanır. | Temel kimlik, biyografi, anahtar kelimeler, güncel görev, faaliyet sayıları ve eser alanları. İsteğe bağlı yanıtta faaliyet ayrıntıları, diğer adlar ve açık e-posta bloğu. | Yalnız Public API'nin veya yapılandırılan Member API belirtecinin görebildiği veri gelir. Sınırlı görünürlükteki kayıtlar Member API yetkisi ister. Ayrıntı çağrılarından biri başarısız olursa ya da put-code eşleşmezse son tam snapshot korunur. |
| OpenAlex | ORCID ile eşleşen yazar yanıtı, bütün eser sayfaları ve her eserin tam yanıtı. | Profil metrikleri ve temel eser alanları; ayrıca eser konuları ve atıf etki bağlamı. İsteğe bağlı yanıtta dış kimlikler, alternatif/ham adlar, kurum geçmişi, son kurumlar, konular ve konu payı. | Yazar profilleri algoritmik eşleştirmeye dayanır. Eser yanıtındaki bütün zengin alanlar birinci sınıf sütunlara taşınmaz; özgün yanıtta korunur. |
| Scopus | Author Retrieval `ENHANCED`, cursor ile alınan Scopus Search `COMPLETE` sayfaları ve temizlenmiş yazar/sayfa/eser JSON'u. | Profil metrikleri ve temel eser alanları. İsteğe bağlı yanıtta sağlayıcının ORCID'i, ad varyantları, güncel/geçmiş kurumlar, konu alanları ve ortak yazar sayısı. | API anahtarı gerekir; dönen görünüm abonelik ve `InstToken` yetkisine bağlıdır. Her eser için Abstract Retrieval çağrılmaz; bu API'deki özet, anahtar kelime, fon ve kaynakça ayrıntıları toplanmaz. |
| Google Scholar / SearchApi | Ücretli SearchApi Scholar Author sayfaları, profil metrikleri, makale satırları ve ham yanıtlar. | Profil metrikleri ve temel makale alanları. İsteğe bağlı yanıtta ilgi alanları, ortak yazarlar, açık erişim özeti ve küçük resim. | Google Scholar'ın resmî API'si yoktur. Atıf ayrıntısı, mandate ayrıntısı ve ek ücretli çağrı zincirleri çalıştırılmaz. |
| Web of Science Starter | ResearcherID ile süzülen WOS/WOK belge sayfaları ve belge yanıtları. | Eşleşen yazardan bulunan görünen ad ile temel eser/atıf alanları. | Starter belge araması tam araştırmacı profili sağlamaz. Daha zengin metrik, düzenlenmiş kurum geçmişi ve hakemlik sunan ayrı lisanslı [Web of Science Researcher API](https://developer.clarivate.com/apis/wos-researcher) entegre değildir. |
| YÖKSİS | Uygulamada tanımlı 21 CV kategorisi, desteklenen yayın ayrıntıları, temizlenmiş kayıtlar ve kalıcı toplama snapshot'ları. Projeler bu tanımlı kategoriler arasındadır. | Kayıtlar kategori bazında saklanır; yayın niteliğindeki kayıtlar ortak eser modeline aktarılır. | Kurumsal kullanıcı, T.C. kimlik numarası ve ilgili uygulama erişim yetkisi gerekir. “21 kategori” WSDL'deki olası bütün operasyonlar anlamına gelmez. |
| TR Dizin | ORCID doğrulamalı yazar, tek `authorPublicationsById` yayın dizisi ve yayın başına ayrıntı; `PROJECT` türü ve çözümlenen tam ad facet'iyle bulunan proje sayfaları ve ayrıntıları. | Yazar metrikleri ve yayınlar ile kimliği doğrulanan proje faaliyetleri ayrı tutulur; projeler yayın metriklerine katılmaz. | Her proje adayı çözümlenen `authorId` veya ORCID ile eşleşmelidir. Eşleşmeyen aday reddedilir ve kapsam `Partial` raporlanır; bu durum tek başına otomatik yeniden deneme başlatmaz. Bozuk ayrıntı toplamayı başarısız kılar ve son iyi snapshot'ı korur. Bulunamayan yazar genel hata yerine `NotFound` olur. Proje sayfaları varsayılan olarak en çok 100 sayfayla korunur. Bütün TÜBİTAK devam eden projeleri ve bütün ad varyantları için eksiksizlik iddiası yoktur; BAPSİS kapsam dışıdır. |
| Crossref | DOI için tam yanıt JSON'u; mevcut fon, lisans, kaynakça ve ilişki metadatası; olumlu/olumsuz önbellek kaydı. | Temel bibliyografya, özet, atıf sayısı, kaynak bağlantıları ve seçilen tam metin adayları. | Araştırmacı profil kaynağı değildir; DOI zenginleştirmesidir. Ürün kullanımı tanımlanmayan zengin alanlar ham kanıtta kalır. |
| Semantic Scholar | Yalnız açık istekle, sınırlı sayıdaki DOI için makale metadatası, TLDR, açık PDF adayı ve sınırlandırılmış atıf yapan eser/bağlam sayfaları. | Makale metadatası ve atıf bağlamları Semantic Scholar uçlarından sunulur ve kayıtlı eserle ilişkilendirilir. | Normal veya bulk toplamada otomatik çalışmaz. Varsayılan sınır çalıştırma başına 10 makale ve makale başına 500 atıftır. Tam kaynakça ve yazar profili toplanmaz; atıf yapan eserler araştırmacının yayın listesine eklenmez. |

## Kayıtlı araştırmacı ayrıntılarını okuma

`GetResearcher` varsayılan olarak küçük bir yanıt döndürür. Kayıtlı araştırmacı alanları gerektiğinde `IncludeProviderDetails` açıkça gönderilir:

```http
POST /Services/AcademicPerformance/V1/GetResearcher
Content-Type: application/json

{
  "PersonelID": "P-1001",
  "IncludeProviderDetails": true
}
```

İsteğe bağlı `Researcher.ProviderDetails`, kayıtlı sağlayıcı snapshot'larından seçilen ORCID, OpenAlex, Scopus ve Google Scholar araştırmacı alanlarını içerir. Eser sayfası yanıtlarını ve kimlik doğrulama bilgilerini içermez; dış sağlayıcı çağrısı yapmaz. Bu bloklarda görülen sağlayıcı kimlikleri araştırmacının kanonik kimlik sütunlarını doldurmaz veya değiştirmez.

## Resmî başvurular

- [ORCID kayıt okuma öğreticisi](https://info.orcid.org/documentation/api-tutorials/api-tutorial-read-data-on-a-record/)
- [OpenAlex yazar verisi](https://help.openalex.org/data/authors/) ve [eser alanları](https://help.openalex.org/data/works/attributes/)
- [Elsevier Scopus API kılavuzu](https://dev.elsevier.com/guides/Scopus%20API%20Guide_V1_20230907.pdf)
- [SearchApi Google Scholar Author API](https://www.searchapi.io/docs/google-scholar-author)
- [Web of Science Starter API](https://developer.clarivate.com/apis/wos-starter)
- [YÖKSİS servis tanımı](https://servisler.yok.gov.tr/ws/OzgecmisV2?wsdl)
- [TR Dizin API](https://development.trdizin.gov.tr/)
- [Crossref REST API](https://www.crossref.org/documentation/retrieve-metadata/rest-api/)
- [Semantic Scholar Academic Graph API](https://www.semanticscholar.org/product/api)
