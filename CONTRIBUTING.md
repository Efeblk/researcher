# Katkı rehberi

Değişiklikleri kısa ömürlü bir dalda yapın ve `main` dalına pull request açın. Kodun yerini seçmeden önce [kod rehberini](docs/CODEBASE_GUIDE.md) okuyun.

```powershell
git switch main
git pull --ff-only
git switch -c feature/kisa-aciklama

# değişikliği yaptıktan sonra
dotnet build AcademicCollectorDemo.sln
dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj
dotnet test ResearcherAnalysisService.Tests/ResearcherAnalysisService.Tests.csproj
npm run typecheck
npm test
git diff --check
git status
```

`feature/`, `fix/`, `chore/` veya `docs/` öneki kullanın. Commit ve PR tek bir amacı kapsamalı; davranış değişikliğini, çalıştırılan kontrolleri, şema/yapılandırma etkisini ve UI değiştiyse ekran görüntüsünü belirtmelidir. Yeni migration'ı tablonun sahibine ekleyin: collector `core`, provider, `bulk` ve `integrations` nesneleri için `Modules/AcademicPerformance/Service/Data/Migrations/{Core,Providers}`; Analysis Service `analysis`, `hr` ve `faculty` nesneleri için `ResearcherAnalysisService/Data/Migrations` kullanır. İki servis aynı SQL veritabanında `dbo.VersionInfo` ve `dbo.ResearcherAnalysisVersionInfo` geçmişlerini ayrı tutar. Servisler arası foreign key eklemeyin; Analysis'in collector kaynak erişimini salt okunur model olarak koruyun.

PR; Build, Dependency Review ve CodeQL kontrolleri geçmeden birleştirilmemelidir. SQL testleri Windows'ta LocalDB, diğer ortamlarda yalnız test için ayrılmış `ACADEMIC_TEST_SQLSERVER` kullanır; uygulama bağlantı ayarlarını veya gerçek sağlayıcı anahtarlarını okumaz. Sağlayıcı testlerinde temizlenmiş sentetik yanıtlar kullanın.

Yalnız `.md`, `.txt`, `.rst` ve yaygın ignore dosyaları değiştiğinde uygulama testleri gerekmez; bağlantıları ve `git diff --check` sonucunu doğrulayın.
