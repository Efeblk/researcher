-- Yeni Bulk API sürümünün TcKimlikNo ve otomatik YÖKSİS desteğini gerektirir. API çağrısı yapmaz; tam toplu istek JSON zarfı üretir.
SET NOCOUNT ON;
DECLARE @Top int=10, @BatchId uniqueidentifier=NEWID(), @RequestBody nvarchar(max);
IF @Top<1 OR @Top>10000 THROW 51020,'@Top 1 ile 10000 arasında olmalıdır.',1;
IF OBJECT_ID('tempdb..#Ready') IS NOT NULL DROP TABLE #Ready;
;WITH q AS(SELECT s.* FROM import.AkademikListe s WHERE s.TcKimlikNo IS NOT NULL
 AND NOT EXISTS(SELECT 1 FROM import.AkademikListe d WHERE d.TcKimlikNo=s.TcKimlikNo AND d.PersonelID<>s.PersonelID)
 AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.TcKimlikNo=s.TcKimlikNo AND r.PersonelID<>s.PersonelID)
 AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.PersonelID=s.PersonelID AND NULLIF(r.TcKimlikNo,N'') IS NOT NULL AND r.TcKimlikNo<>s.TcKimlikNo)), safe AS(
 SELECT q.PersonelID,
  CASE WHEN q.ORCID IS NOT NULL AND NOT EXISTS(SELECT 1 FROM import.AkademikListe d WHERE d.ORCID=q.ORCID AND d.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.ORCID=q.ORCID AND r.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.PersonelID=q.PersonelID AND NULLIF(r.ORCID,N'') IS NOT NULL AND r.ORCID<>q.ORCID) THEN q.ORCID END ORCID,
  CASE WHEN q.ScholarID IS NOT NULL AND NOT EXISTS(SELECT 1 FROM import.AkademikListe d WHERE d.ScholarID=q.ScholarID AND d.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.ScholarID=q.ScholarID AND r.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.PersonelID=q.PersonelID AND NULLIF(r.ScholarID,N'') IS NOT NULL AND r.ScholarID<>q.ScholarID) THEN q.ScholarID END ScholarID,
  CASE WHEN q.ResearcherID IS NOT NULL AND NOT EXISTS(SELECT 1 FROM import.AkademikListe d WHERE d.ResearcherID=q.ResearcherID AND d.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.ResearcherID=q.ResearcherID AND r.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.PersonelID=q.PersonelID AND NULLIF(r.ResearcherID,N'') IS NOT NULL AND r.ResearcherID<>q.ResearcherID) THEN q.ResearcherID END ResearcherID,
  CASE WHEN q.ScopusID IS NOT NULL AND NOT EXISTS(SELECT 1 FROM import.AkademikListe d WHERE d.ScopusID=q.ScopusID AND d.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.ScopusID=q.ScopusID AND r.PersonelID<>q.PersonelID) AND NOT EXISTS(SELECT 1 FROM core.Researchers r WHERE r.PersonelID=q.PersonelID AND NULLIF(r.ScopusID,N'') IS NOT NULL AND r.ScopusID<>q.ScopusID) THEN q.ScopusID END ScopusID FROM q)
SELECT q.TcKimlikNo, safe.* INTO #Ready FROM safe INNER JOIN q ON q.PersonelID = safe.PersonelID;
SET @RequestBody=(SELECT CONVERT(nvarchar(36),@BatchId) BatchId,JSON_QUERY((SELECT TOP(@Top) PersonelID,TcKimlikNo,ORCID,ResearcherID,ScopusID,ScholarID FROM #Ready ORDER BY PersonelID FOR JSON PATH,INCLUDE_NULL_VALUES)) Researchers FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
SELECT @RequestBody AS RequestBody;
SELECT (SELECT COUNT(*) FROM #Ready) AS ToplamHazir,(SELECT COUNT(*) FROM import.AkademikListe)-(SELECT COUNT(*) FROM #Ready) AS ToplamAtlanan,@Top AS IstenenUstSinir;
