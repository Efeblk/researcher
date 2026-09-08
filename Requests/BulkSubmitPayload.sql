-- 1) Bu sorguyu kaynak veritabanında, SSMS üzerinden çalıştırın.
-- 2) Sonuçtaki tek JSON hücresini BulkCollection.http içindeki Submit gövdesine yapıştırın.
-- 3) İlk denemeden sonra TOP (10) ifadesini kaldırarak 2.980 satırı gönderebilirsiniz.
-- Sorgu yalnızca okur; kaynak değerleri kırpmaz veya değiştirmez.

DECLARE @BatchId uniqueidentifier = NEWID();
DECLARE @RequestBody nvarchar(max);

SET @RequestBody = (
    SELECT
        CONVERT(nvarchar(36), @BatchId) AS BatchId,
        JSON_QUERY((
            SELECT TOP (10)
                CAST(p.PersonelID AS nvarchar(max)) AS PersonelID,
                p.ORCID,
                p.ResearcherID,
                p.ScopusID,
                p.ScholarID
            FROM dbo.PersonelTest AS p
            ORDER BY p.PersonelID
            FOR JSON PATH, INCLUDE_NULL_VALUES
        )) AS Researchers
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

SELECT @RequestBody AS RequestBody;
