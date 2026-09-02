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

Geliştirme veritabanında tüm migration zincirini sıfırdan doğrulamak için önce
sunucuyu durdurun, ardından `dotnet run -- --clean-database` ve `dotnet run`
komutlarını çalıştırın. Temizleme komutu bütün geliştirme verilerini siler.
