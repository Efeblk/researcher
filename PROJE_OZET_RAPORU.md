# Proje özet raporu — 8 Eylül 2026

Bu .NET 10/Serenity entegrasyon prototipi, akademisyen profilleri ve yayınlarını
toplar, tekilleştirir ve okul sitesindeki yayın seçimlerini yönetir. Production'a
hazır değildir.

## Tamamlananlar

- [x] ORCID, Google Scholar/SearchApi, OpenAlex, Web of Science ve YÖKSİS verileri
  sağlayıcıya özgü ham kayıtlar halinde SQL Server'da saklanıyor.
- [x] Yayınlar ortak modele çevriliyor, DOI ve başlık kurallarıyla tekilleştiriliyor;
  özetler ve kullanıcının gösterim seçimleri kaydedilip yeniden yükleniyor.
- [x] V1 API, toplama, getirme, listeleme ve seçimleri sunuyor.
- [x] Web arayüzü profil karşılaştırmasını, yayınları ve seçimleri gösteriyor.
- [x] Toplu toplama, kalıcı SQL kuyruğu ve yapılandırılabilir SQL kaynağı üzerinden
  arka planda çalışabiliyor. Sağlayıcı bazlı merkezi hız, kota, bekleme ve yeniden
  deneme yönetimi var; worker ve SQL içe aktarma varsayılan olarak kapalı.
- [x] Provider Status, erişilebilirlik ile yerel SQL bütçesini ayrı gösteriyor.
  Yerel sayaç sağlayıcının gerçek kotası değildir.
- [x] Araştırmacı ID'siyle kaydedilmiş veriden Qwen/Ollama raporu üretme, saklama ve
  son raporu getirme hazır. Analiz yeniden veri toplamaz; snapshot zamanı rapora
  giren mevcut kayıtları etiketler, geçmişe dönük veri sorgulamaz.

## Öncelikli yol haritası

- [ ] Diğer geliştirme bilgisayarında görülen, taşınmış sözleşme dosyalarına eski
  referanslardan kaynaklanan `CS2001` hatasını çözmek; yerel temiz derlemede oluşmadı.
- [ ] Qwen'in Türkçe rapor kalitesini gerçek örneklerle iyileştirmek.
- [ ] Gerçek sağlayıcı hesaplarını, production kimlik bilgilerini ve bütçeleri;
  kurumsal SQL kolon eşlemeleriyle birlikte doğrulamak.
- [ ] Production öncesi açık izin servisini BYS oturum, yetki ve kayıt sahipliği
  denetimleriyle değiştirmek.
- [ ] İsteğe bağlı olarak kayıtlı AI raporlarını gösteren web ekranını eklemek.

PR CI kontrolleri, sentetik akışlar ve Qwen smoke testi geçti.
Gerçek sağlayıcı hesaplarıyla uçtan uca test henüz yapılmadı.

Ayrıntılar: [API planları ve limit kaynakları](docs/API_OZET_RAPORU.md),
[toplu toplama](docs/BULK_COLLECTION.md), [araştırmacı analizi](docs/RESEARCHER_ANALYSIS.md)
ve [sağlayıcı durumu](docs/PROVIDER_STATUS.md).
