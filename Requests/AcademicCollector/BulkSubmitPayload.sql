-- 1) Bu sorguyu kaynak veritabanında, SSMS üzerinden çalıştırın.
-- 2) Sonuçtaki BatchId ve Researchers içeren tek JSON hücresini BulkCollection.http içindeki
--    Submit gövdesinin tamamı olarak yapıştırın; aynı BatchId ile Status isteğini çalıştırın.
-- 3) İlk denemeden sonra TOP (10) sayısını ihtiyaca göre artırın; tek batch en çok 10.000 satır alır.
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
