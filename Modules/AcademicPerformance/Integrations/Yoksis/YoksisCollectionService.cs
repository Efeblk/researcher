namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisCollectionService
{
    private static readonly List<YoksisOperationDefinition> Operations =
    [
        new(
            "Dersler",
            "getirDersListesi",
            "getirDersListesiRequest"),
        new(
            "Üniversite dışı deneyimler",
            "getirUnvDisiDeneyimListesi",
            "getirUnvDisiDeneyimListesiRequest"),
        new(
            "Tez danışmanlıkları",
            "getirTezDanismanListesi",
            "getirTezDanismanListesiRequest"),
        new(
            "Hakemlikler",
            "getHakemlikBilgisiV1",
            "getHakemlikBilgisiV1Request"),
        new(
            "Bildiriler",
            "getBildiriBilgisiV1",
            "getBildiriBilgisiV1Request",
            "Bildiri ayrıntıları",
            "getBildiriBilgisiDetayV1",
            "getBildiriBilgisiDetayV1Request",
            "YAYIN_ID"),
        new(
            "Tasarımlar",
            "getTasarimBilgisiV1",
            "getTasarimBilgisiV1Request"),
        new(
            "Personel ve araştırmacı kimlikleri",
            "getPersonelLinkV1",
            "getPersonelLinkV1Request"),
        new(
            "Ödüller",
            "getOdulListesiV1",
            "getOdulListesiV1Request"),
        new(
            "Araştırma ve sertifikalar",
            "getArastirmaSertifkaBilgisiV1",
            "getArastirmaSertifkaBilgisiV1Request"),
        new(
            "Makaleler",
            "getMakaleBilgisiV1",
            "getMakaleBilgisiV1Request",
            "Makale ayrıntıları",
            "getMakaleBilgisiDetayV1",
            "getMakaleBilgisiDetayV1Request",
            "YAYIN_ID"),
        new(
            "Projeler",
            "getirProjeListesi",
            "getirProjeListesiRequest",
            "Proje ayrıntıları",
            "getirProjeListesiDetay",
            "getirProjeListesiDetayRequest",
            "PROJE_ID"),
        new(
            "Akademik görevler",
            "getirAkademikGorevListesi",
            "getirAkademikGorevListesiRequest"),
        new(
            "Kitaplar",
            "getKitapBilgisiV1",
            "getKitapBilgisiV1Request",
            "Kitap ayrıntıları",
            "getKitapBilgisiDetayV1",
            "getKitapBilgisiDetayV1Request",
            "YAYIN_ID"),
        new(
            "İdari görevler",
            "getirIdariGorevListesi",
            "getirIdariGorevListesiRequest"),
        new(
            "Temel alanlar",
            "getTemelAlanBilgisiV1",
            "getTemelAlanBilgisiV1Request"),
        new(
            "Öğrenim bilgileri",
            "getirOgrenimBilgisiListesi",
            "getirOgrenimBilgisiListesiRequest"),
        new(
            "Yabancı diller",
            "getirYabanciDilListesi",
            "getirYabanciDilListesiRequest"),
        new(
            "Patentler",
            "getPatentBilgisiV1",
            "getPatentBilgisiV1Request",
            "Patent ayrıntıları",
            "getPatentBilgisiDetayV1",
            "getPatentBilgisiDetayV1Request",
            "PATENT_ID"),
        new(
            "Üyelikler",
            "getirUyelikListesi",
            "getirUyelikListesiRequest"),
        new(
            "Editörlükler",
            "getEditorlukBilgisiV1",
            "getEditorlukBilgisiV1Request"),
        new(
            "Sanatsal faaliyetler",
            "getSanatsalFaalV1",
            "getSanatsalFaalV1Request")
    ];

    private readonly YoksisClient _yoksisClient;

    public YoksisCollectionService(YoksisClient yoksisClient)
    {
        _yoksisClient = yoksisClient;
    }

    public async Task<YoksisCollectResponse> CollectAsync(
        YoksisCollectRequest request)
    {
        YoksisCollectResponse? response = null;
        string? tcKimlikNo = null;

        tcKimlikNo = ValidateTcKimlikNo(request.TcKimlikNo);
        response = new YoksisCollectResponse();
        response.CollectedAt = DateTime.UtcNow;

        foreach (YoksisOperationDefinition operation in Operations)
        {
            YoksisOperationResult? category = null;

            try
            {
                category = await _yoksisClient.GetAsync(
                    operation,
                    tcKimlikNo,
                    request.UpdatedAfter);
                response.Categories.Add(category);
                AddFeedback(response.Messages, category);

                if (HasDetailOperation(operation) && category.IsSuccess)
                {
                    YoksisOperationResult? details = null;

                    details = await CollectDetailsAsync(
                        operation,
                        category,
                        tcKimlikNo,
                        request.UpdatedAfter);
                    response.Categories.Add(details);
                    AddFeedback(response.Messages, details);
                }
            }
            catch (Exception exception)
            {
                category = CreateFailure(operation, exception.Message);
                response.Categories.Add(category);
                AddFeedback(response.Messages, category);
            }
        }

        response.SuccessfulCategoryCount = response.Categories.Count(item =>
            item.IsSuccess);
        response.FailedCategoryCount = response.Categories.Count(item =>
            !item.IsSuccess);
        response.TotalRecordCount = response.Categories.Sum(item =>
            item.RecordCount);
        return response;
    }

    internal static void RemoveUnrequestedResponseData(
        YoksisCollectResponse response,
        YoksisCollectRequest request)
    {
        foreach (YoksisOperationResult category in response.Categories)
        {
            if (!request.IncludeRecords)
            {
                category.Records.Clear();
            }

            if (!request.IncludeRawResponses)
            {
                category.RawResponsesXml.Clear();
            }
        }
    }

    private async Task<YoksisOperationResult> CollectDetailsAsync(
        YoksisOperationDefinition operation,
        YoksisOperationResult listResult,
        string tcKimlikNo,
        DateTime? updatedAfter)
    {
        YoksisOperationResult? combinedResult = null;
        List<string>? identifiers = null;

        combinedResult = new YoksisOperationResult();
        combinedResult.CategoryName = operation.DetailCategoryName;
        combinedResult.OperationName = operation.DetailOperationName;
        combinedResult.IsSuccess = true;
        identifiers = GetDistinctIdentifiers(
            listResult,
            operation.DetailIdentifierFieldName!);

        if (identifiers.Count == 0)
        {
            combinedResult.ResultMessage = "Ayrıntı istenecek kayıt bulunamadı.";
            return combinedResult;
        }

        foreach (string identifier in identifiers)
        {
            try
            {
                YoksisOperationResult? detailResult = null;

                detailResult = await _yoksisClient.GetDetailAsync(
                    operation,
                    tcKimlikNo,
                    identifier,
                    updatedAfter);
                Merge(combinedResult, detailResult);
            }
            catch (Exception exception)
            {
                combinedResult.IsSuccess = false;
                combinedResult.RequestCount++;
                combinedResult.Errors.Add(
                    $"{operation.DetailIdentifierFieldName}={identifier}: " +
                    exception.Message);
            }
        }

        combinedResult.RecordCount = combinedResult.Records.Count;
        combinedResult.ResultMessage = combinedResult.Errors.Count == 0
            ? "Bütün ayrıntılar alındı."
            : $"{combinedResult.Errors.Count} ayrıntı isteği başarısız oldu.";
        return combinedResult;
    }

    private static void Merge(
        YoksisOperationResult target,
        YoksisOperationResult source)
    {
        target.RequestCount += source.RequestCount;
        target.Records.AddRange(source.Records);
        target.RawResponsesXml.AddRange(source.RawResponsesXml);
        target.Errors.AddRange(source.Errors);

        if (!source.IsSuccess)
        {
            target.IsSuccess = false;
        }
    }

    private static List<string> GetDistinctIdentifiers(
        YoksisOperationResult result,
        string fieldName)
    {
        return result.Records
            .Select(record => record.GetValueOrDefault(fieldName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool HasDetailOperation(
        YoksisOperationDefinition operation)
    {
        return !string.IsNullOrWhiteSpace(operation.DetailOperationName) &&
            !string.IsNullOrWhiteSpace(operation.DetailRequestElementName) &&
            !string.IsNullOrWhiteSpace(operation.DetailIdentifierFieldName);
    }

    private static YoksisOperationResult CreateFailure(
        YoksisOperationDefinition operation,
        string error)
    {
        YoksisOperationResult? result = null;

        result = new YoksisOperationResult();
        result.CategoryName = operation.CategoryName;
        result.OperationName = operation.OperationName;
        result.IsSuccess = false;
        result.Errors.Add(error);
        return result;
    }

    private static void AddFeedback(
        List<string> messages,
        YoksisOperationResult result)
    {
        if (result.IsSuccess)
        {
            messages.Add(
                $"[OK] YÖKSİS {result.CategoryName}: " +
                $"{result.RecordCount} kayıt alındı.");
            return;
        }

        messages.Add(
            $"[HATA] YÖKSİS {result.CategoryName}: " +
            $"{result.Errors.FirstOrDefault() ?? result.ResultMessage ?? "Veri alınamadı."}");
    }

    private static string ValidateTcKimlikNo(string? value)
    {
        string? tcKimlikNo = null;

        tcKimlikNo = value?.Trim();

        if (string.IsNullOrWhiteSpace(tcKimlikNo) ||
            tcKimlikNo.Length != 11 ||
            tcKimlikNo[0] == '0' ||
            !tcKimlikNo.All(char.IsDigit))
        {
            throw new ArgumentException(
                "YÖKSİS sorgusu için 11 haneli geçerli biçimde " +
                "T.C. kimlik numarası verilmelidir.");
        }

        return tcKimlikNo;
    }
}
