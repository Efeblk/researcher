# Migration Düzeni

FluentMigrator migration'ları uygulama başlangıcında assembly içinden bulunur.
Klasör adı yürütme sırasını etkilemez; sıra sınıflardaki benzersiz
`[Migration(...)]` numarasıyla belirlenir.

## Klasörler

- `Core/`: Akademisyen, ortak yayın, gösterim onayı ve diğer paylaşılan şema
  değişiklikleri.
- `Providers/`: Google Scholar veya OpenAlex gibi belirli bir dış sağlayıcıya
  ait tablo ve kolon değişiklikleri.

Mevcut sıra:

| Sürüm | Dosya | Amaç |
| --- | --- | --- |
| `202608250001` | `Core/202608250001_InitialAcademicSchema.cs` | Temel akademik şema |
| `202608270001` | `Providers/202608270001_AddGoogleScholar.cs` | Scholar profil ve eser tabloları |
| `202608280001` | `Providers/202608280001_AddOpenAlexComparison.cs` | Ayrı OpenAlex karşılaştırma tabloları |
| `202609110001` | `Core/202609110001_AddGeminiUsageAttempts.cs` | Kalıcı Gemini kullanım denemeleri tablosu |
| `202609110002` | `Core/202609110002_GroupTablesBySchema.cs` | 28 uygulama tablosunu sorumluluk şemalarına taşıma |

Son migration uygulama tablolarını `core`, sağlayıcıya özel şemalar, `analysis`,
`bulk` ve `integrations` altında gruplar. `dbo` yalnızca FluentMigrator sürüm kaydı
gibi migration altyapısına ayrılmıştır. Taşıma `ALTER SCHEMA TRANSFER` kullandığı için
mevcut satırları, anahtarları ve indeksleri yeniden oluşturmaz.

## Yeni Migration Ekleme

Dosyayı `yyyyMMddNNNN_AciklayiciEylem.cs` biçiminde adlandırın ve aynı sayıyı
`[Migration(yyyyMMddNNNN, "...")]` özniteliğinde kullanın. Ortak şema değişikliği
`Core/`, yalnız bir sağlayıcıyı ilgilendiren değişiklik `Providers/` altında
olmalıdır.

Uygulanmış migration dosyalarını değiştirmeyin veya birleştirmeyin. Şema düzeltmesi
için daima daha büyük sürüm numaralı yeni bir migration ekleyin. `Up()` ileri
değişikliği, `Down()` ise yabancı anahtar bağımlılıklarını gözeterek güvenli geri
alma sırasını içermelidir. Aynı değişikliği `AcademicDbContext` modeline de
yansıtın.

Yeni tablo, indeks, anahtar ve yabancı anahtar migration işlemlerinde hedef tabloya
uygun `.InSchema("...")` çağrısını ekleyin. `Execute.Sql` içindeki tablo adlarını ve
yabancı anahtar hedeflerini de şema adıyla açıkça niteleyin; uygulama tabloları için
varsayılan `dbo` çözümlemesine güvenmeyin.

Geliştirme veritabanında tüm migration zincirini sıfırdan doğrulamak için önce
sunucuyu durdurun, ardından `dotnet run -- --clean-database` ve `dotnet run`
komutlarını çalıştırın. Temizleme komutu bütün geliştirme verilerini siler.
