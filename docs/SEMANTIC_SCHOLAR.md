# Semantic Scholar DOI zenginleştirmesi

Toplama tamamlandıktan sonra yalnızca araştırmacıya mevcut sağlayıcılar üzerinden bağlanmış ve DOI'si bulunan `AcademicWorks` kayıtları Semantic Scholar Graph API ile zenginleştirilir. İsimle yazar eşleştirmesi yapılmaz; atıf yapan makaleler araştırmacının kendi yayınları arasına eklenmez.

Veri DOI düzeyinde ortak önbellekte bir kez saklanır. Makale kimliği, başlık, özet, yazar kimlikleri, yıl, yayın yeri ve türü, alanlar, açık erişim PDF bilgisi, sayımlar, TLDR ve metin erişilebilirlik durumu korunur. Atıf ilişkileri hedef makale + atıf yapan Semantic Scholar makale kimliği ile, bağlamlar ise ilişkiye bağlı ayrı satırlar halinde saklanır. `contextsWithIntent` içindeki niyet yalnızca kendi bağlamıyla ilişkilendirilir. Destekleyici veya çelişen sonucuna dair türetim yapılmaz.

`SemanticScholar:ApiKey` isteğe bağlıdır ve `dotnet user-secrets` ile ayarlanmalıdır. Anonim kullanım için varsayılan hız saniyede en çok bir istektir. `CitationPageSize`, `MaximumCitationsPerPaper` ve `MaximumPapersPerRun` sınırları ayarlanabilir. `CitationsFetched`, `CitationTotal` ve `CitationsComplete` alanları sonucun tamamlanma veya kesilme durumunu açıklar. HTTP 404 negatif önbelleğe alınır; 429 ve aktarım hataları negatif sonuç sayılmaz.

Kaydedilmiş sonuçlar `POST /Services/AcademicPerformance/V1/ListSemanticScholarPapers` ile `PersonelID`, isteğe bağlı `AcademicWorkId`, `Skip` ve `Take` kullanılarak okunur.
