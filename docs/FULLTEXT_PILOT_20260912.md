# Gerçek tam metin AI pilotu — 12 Eylül 2026

Bu yetkilendirilmiş canlı pilot, tek bir açık makaleyi izole SQL Server veritabanında ve `Testing` ortamında çalıştırdı. Makinece okunabilir çıktı yerel `docs/fulltext-pilot-20260912-result.json` dosyasında korunur ve kaynak denetimine eklenmez. Üretim ayarları ve uygulama veritabanları değiştirilmedi; otomatik worker'lar ve ilgisiz sağlayıcılar kapalıydı.

Kaynak, *Adam: A Method for Stochastic Optimization* makalesinin sabitlenmiş `arXiv:1412.6980v1` sürümüydü (`22 Aralık 2014`). Uygulamanın kendi `pdfpig-layout-spans-v2` extractor'ı 555.695 baytlık PDF'den 9/9 metin taşıyan sayfa, 30.904 UTF-16 karakter, 31.637 UTF-8 bayt ve 64 span çıkardı. Review hesabı 70.085 bayttı ve değiştirilmemiş 100.000 bayt sınırına sığdı. Başka abstract seed edilmedi; kaynak gerçek `https://arxiv.org/pdf/1412.6980v1` URL'sinden alındı. Daha önce düşünülen 13 sayfalık futbol makalesi 149.810 bayt review girdisi ürettiği için ücretli çağrı yapılmadan elendi.

`SummarizeArticle` için gönderilen tek workflow isteği HTTP 200 ile 65,273 saniyede tamamlandı. Altı Gemini çağrısının tamamı başarılıydı. Özetleyici 13 aday iddiayı otomatik kontrol etti; 9 iddiayı kaydetti ve 4 `uncertain` iddiayı gerekçeleriyle eledi. Bu nedenle raporun claim kapsamı `IsPartial=true` olsa da kaynak kapsamı 9/9 sayfalık tam PDF'dir. Kaydedilmiş özet ve canonical evidence okumaları yeni model çağrısı üretmedi.

Başarılı özetten sonra gönderilen tek `ReviewCanonicalArticle` isteği HTTP 502 ile 123,628 saniyede sonlandı ve tekrar denenmedi. Sekiz review alt çağrısının ilk yedisi başarılı oldu; son çağrı Gemini'den HTTP 200 aldı ancak `MAX_TOKENS` ile bitti ve ledger'a `OutputLimit` yazıldı. Review rol sırası ve çağrı sırası bunun `teaching` doğrulama geçişi olduğunu gösterir; yakalanan üst seviye yanıt gövdesi yalnız genel 502 hatasını içerdiği için stage/role bilgisi çıkarımdır. Fail-closed davranışla hiçbir canonical review satırı kaydedilmedi; review read HTTP 404 döndü ve usage sayısını değiştirmedi.

SQL source hash'i `ef1663dd32671bce737af77d0cfd0777fd8ebd95bb86f5d0ff2b4aa457675fa3` yeniden hesaplanan hash ile eşleşti. Dokuz sayfanın metni ve 64 span'ın ID, sayfa, offset ve metni taze built-DLL extraction ile exact eşleşti. Kaydedilmiş 9 iddianın 16 evidence bağlantısı ile workflow ve read yanıtlarındaki tüm görülen citation'ların quote/page/offset değerleri de immutable span kataloğuyla exact eşleşti.

| Aşama | Çağrı | Prompt | Candidate | Thinking | Toplam token | Tahmini USD |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Özet + doğrulama | 6 | 25.990 | 2.729 | 19.022 | 47.741 | 0,101058750 |
| Review geçişleri | 8 | 62.707 | 3.669 | 38.503 | 104.879 | 0,205175250 |
| Toplam | 14 | 88.697 | 6.398 | 57.525 | 152.620 | 0,306234000 |

Cache token sayısı 0, maliyeti bilinmeyen attempt sayısı 0'dı. Tahmin, 31 Aralık 2026'ya kadar Gemini 3.8 Standard fiyatları olan input `$0.75/M`, cached input `$0.075/M` ve output+thinking `$3.75/M` üzerinden uygulama ledger'ından hesaplandı; fatura değildir.

Bu pilot tam metin edinme, extraction, özet, SQL kalıcılığı, exact evidence ve fail-closed review davranışını somut olarak sınadı. Review tamamlanmadığı için review kalitesi değerlendirilmedi ve bilimsel doğruluk kurulmadı. Koordinatör AI ajanının görsel kontrolü insan veya uzman değerlendirmesi değildir. PDF'deki grafiklerin görsel anlamı metin extractor'ı tarafından analiz edilmedi; formüllerde satır bölünmesi ve düzleştirilmiş üst/alt indis veya operatörler matematiksel biçim doğruluğu kanıtlamaz. Yüksek tanınırlıklı tek makale model kalitesi veya genelleme benchmark'ı değildir.

Sonuçlar veritabanı düşürülmeden önce kaydedildi. Pilotun sahip olduğu collector ve analysis process tree'leri durduruldu; doğrulanmış GUID veritabanları ve repository içindeki geçici runner klasörü kaldırıldı. 5001/5097/5197 dinleyicilerinin ve `AcademicFullTextPilot_` veritabanlarının kalmadığı doğrulandı. Temp kökündeki bu operatöre ait üç log klasörünün silinmesi otomatik politika tarafından reddedildi; bu klasörlerde toplam sekiz log dosyası kaldı. Koordinatörün kaynak ve görsel QA dosyalarını tuttuğu `academic-fulltext-source-t9aqbsak` klasörü de aynı nedenle kaldı. Engeller başka bir yöntemle aşılmadı.
