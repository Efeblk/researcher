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

`feature/`, `fix/`, `chore/` veya `docs/` öneki kullanın. Commit ve PR tek bir amacı kapsamalı; davranış değişikliğini, çalıştırılan kontrolleri, şema/yapılandırma etkisini ve UI değiştiyse ekran görüntüsünü belirtmelidir. Uygulanmış migration'ı değiştirmeyin; daha büyük benzersiz numarayla yeni FluentMigrator dosyası ekleyin.

PR; Build, Dependency Review ve CodeQL kontrolleri geçmeden birleştirilmemelidir. SQL testleri Windows'ta LocalDB, diğer ortamlarda yalnız test için ayrılmış `ACADEMIC_TEST_SQLSERVER` kullanır; uygulama bağlantı ayarlarını veya gerçek sağlayıcı anahtarlarını okumaz. Sağlayıcı testlerinde temizlenmiş sentetik yanıtlar kullanın.

Yalnız `.md`, `.txt`, `.rst` ve yaygın ignore dosyaları değiştiğinde uygulama testleri gerekmez; bağlantıları ve `git diff --check` sonucunu doğrulayın.
