# Proje özeti ve yol haritası — 8 Eylül 2026

## Amaç ve durum

Bu .NET 10/Serenity uygulaması, akademisyen profillerini ve yayınlarını birden çok
sağlayıcıdan toplar, SQL Server'da tekilleştirir ve web sitesinde gösterilecek
yayınların seçilmesini sağlar. Mimari ve temel akışlar çalışan bir entegrasyon
prototipi düzeyindedir; production'a hazır değildir.

## Tamamlananlar

- [x] ORCID, Google Scholar/SearchApi, OpenAlex, Web of Science ve YÖKSİS
  entegrasyonları; sağlayıcıya özgü kayıtların SQL Server'da saklanması.
- [x] Ortak yayın modeli, DOI/başlık kurallarıyla tekilleştirme ve kullanıcının
  yayın gösterim seçimlerini kaydetme/yükleme.
- [x] SQL Server'da kalıcı toplu iş kuyruğu, yapılandırılabilir SQL kaynağı,
  yeniden deneme ve sağlayıcı bazlı hız/kota sınırları. İşçi ve SQL içe aktarma
  repodaki varsayılanlarda kapalıdır.
- [x] Sağlayıcı erişilebilirliği ile yerel SQL bütçelerini gösteren durum sorgusu.
  Yerel sayaç sağlayıcının gerçek kotası değildir; boş kota bilgisi “sınırsız”
  anlamına gelmez.
- [x] Yalnız araştırmacı ID'siyle kaydedilmiş veriden AI raporu üretme/saklama ve
  en son raporu getirme. Ortak sözleşme kütüphanesi ile Qwen/Ollama desteği vardır.
  Analiz otomatik veri toplamaz. `SnapshotAt`, geçmişe dönük sorguyu değil rapora
  giren mevcut kayıtların anını etiketler.
- [x] GitHub PR CI akışı ve Astra koordinasyonu/Sol uygulaması için ayrı worktree
  çalışma düzeni.

## Öncelikli yol haritası

- [ ] Diğer geliştirme bilgisayarında, taşınmış sözleşme dosyalarının eski yollarına
  referans veren `CS2001` derleme hatasını kök nedenine kadar
  çözmek. Yerel temiz derlemede yeniden üretilemedi.
- [ ] Qwen'in Türkçe anlatım kalitesini gerçek rapor örnekleriyle değerlendirip
  prompt/model ayarlarını iyileştirmek.
- [ ] Sağlayıcıları gerçek kurum hesaplarıyla doğrulamak; production SQL kaynak
  kolonlarını, bağlantıları ve gerçek kota/bütçe ayarlarını kesinleştirmek.
- [ ] Production öncesi açık izin servisini BYS oturum, yetki ve kayıt sahipliği
  denetimleriyle değiştirmek.
- [ ] Geçmişte saklanmış olabilecek hassas YÖKSİS verileri için kontrollü temizlik
  planı hazırlamak; mevcut maskeleme eski kayıtları otomatik temizlemez.
- [ ] İsteğe bağlı sonraki adım olarak kayıtlı AI raporlarını gösteren UI eklemek.

## Doğrulama ve ayrıntılar

Birleştirilen PR'ların CI kontrolleri geçti. Sentetik verili akışlar ve gerçek yerel
Qwen smoke testi daha önce başarılıydı; tüm sağlayıcılar gerçek hesaplarla uçtan uca
test edilmiş değildir ve bu doküman değişikliği yeni test eklemez.

Ayrıntılar: [kod tabanı rehberi](CODEBASE_GUIDE.md),
[toplu toplama](BULK_COLLECTION.md), [araştırmacı analizi](RESEARCHER_ANALYSIS.md) ve
[sağlayıcı durumu](PROVIDER_STATUS.md).
