namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis.Collection;

public sealed class YoksisCollectionService
{
    private readonly YoksisClient _yoksisClient;

    public YoksisCollectionService(YoksisClient yoksisClient)
    {
        _yoksisClient = yoksisClient;
    }

    public async Task<YoksisCollectResponse> CollectAsync(
        YoksisCollectRequest request,
        IProgress<YoksisCollectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string tcKimlikNo = ValidateTcKimlikNo(request.TcKimlikNo);
        _yoksisClient.ValidateConfiguration();
        YoksisCollectResponse? response = new YoksisCollectResponse();
        response.CollectedAt = DateTime.UtcNow;

        int operationIndex = 0;
        foreach (YoksisOperationDefinition operation in YoksisOperationCatalog.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            operationIndex++;
            YoksisOperationResult? category = null;

            try
            {
                Report(progress, "category", $"{operation.CategoryName} isteniyor.", operation,
                    operationIndex, YoksisOperationCatalog.All.Count);
                category = await _yoksisClient.GetAsync(
                    operation,
                    tcKimlikNo,
                    request.UpdatedAfter,
                    cancellationToken);
                if (!category.IsSuccess)
                    AddProviderFailure(category);
                response.Categories.Add(category);
                AddFeedback(response.Messages, category);
                Report(progress, category.IsSuccess ? "category-complete" : "category-failed",
                    category.IsSuccess
                        ? $"{operation.CategoryName}: {category.RecordCount} kayıt alındı."
                        : $"{operation.CategoryName} yanıtı başarısız oldu.", operation,
                    operationIndex, YoksisOperationCatalog.All.Count, category.RecordCount);

                if (HasDetailOperation(operation) && category.IsSuccess)
                {
                    YoksisOperationResult? details = await CollectDetailsAsync(
                        operation,
                        category,
                        tcKimlikNo,
                        request.UpdatedAfter,
                        progress,
                        cancellationToken);
                    response.Categories.Add(details);
                    AddFeedback(response.Messages, details);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (exception is YoksisProviderException { PartialResult: not null } systemic)
                {
                    response.Categories.Add(systemic.PartialResult);
                    AddFeedback(response.Messages, systemic.PartialResult);
                }
                category = CreateFailure(operation, exception);
                response.Categories.Add(category);
                AddFeedback(response.Messages, category);
                if (exception is YoksisProviderException { StopsCollection: true })
                {
                    response.StopReason = exception.Message;
                    break;
                }
            }
        }

        PopulatePublicationCoverage(response);

        response.SuccessfulCategoryCount = response.Categories.Count(item =>
            item.IsSuccess);
        response.FailedCategoryCount = response.Categories.Count(item =>
            !item.IsSuccess);
        response.TotalRecordCount = response.Categories.Sum(item => item.RecordCount);
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
        DateTime? updatedAfter,
        IProgress<YoksisCollectionProgress>? progress,
        CancellationToken cancellationToken)
    {
        YoksisOperationResult? combinedResult = new YoksisOperationResult();
        combinedResult.CategoryName = operation.DetailCategoryName;
        combinedResult.OperationName = operation.DetailOperationName;
        combinedResult.IsSuccess = true;
        List<string>? identifiers = GetDistinctIdentifiers(
            listResult,
            operation.DetailIdentifierFieldName!);
        int missingIdentifierCount = listResult.Records.Count(record =>
            string.IsNullOrWhiteSpace(record.GetValueOrDefault(
                operation.DetailIdentifierFieldName!)));
        combinedResult.ExpectedDetailCount = identifiers.Count + missingIdentifierCount;
        for (int index = 0; index < missingIdentifierCount; index++)
            AddFailure(combinedResult, "MissingIdentifier");

        if (identifiers.Count == 0)
        {
            combinedResult.FailedDetailCount = missingIdentifierCount;
            combinedResult.IsSuccess = missingIdentifierCount == 0;
            combinedResult.ResultMessage = missingIdentifierCount == 0
                ? "Ayrıntı istenecek kayıt bulunamadı."
                : $"{missingIdentifierCount} liste kaydında ayrıntı kimliği yok.";
            return combinedResult;
        }

        int detailIndex = 0;
        foreach (string identifier in identifiers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            detailIndex++;
            try
            {
                Report(progress, "detail",
                    $"{operation.DetailCategoryName} ayrıntısı alınıyor ({detailIndex}/{identifiers.Count}).",
                    operation, detailIndex, identifiers.Count);
                YoksisOperationResult? detailResult = await _yoksisClient.GetDetailAsync(
                    operation,
                    tcKimlikNo,
                    identifier,
                    updatedAfter,
                    cancellationToken);
                MergeDetail(
                    combinedResult,
                    detailResult,
                    operation.DetailIdentifierFieldName!,
                    identifier);
                Report(progress, "detail-complete",
                    $"{operation.DetailCategoryName} ayrıntısı tamamlandı ({detailIndex}/{identifiers.Count}).",
                    operation, detailIndex, identifiers.Count, detailResult.RecordCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                combinedResult.RequestCount++;
                AddFailure(combinedResult, ClassifyFailure(exception));
                if (exception is YoksisProviderException { StopsCollection: true })
                {
                    int unattemptedCount = identifiers.Count - detailIndex;
                    for (int index = 0; index < unattemptedCount; index++)
                        AddFailure(combinedResult, "NotAttempted");
                    combinedResult.RecordCount = combinedResult.Records.Count;
                    combinedResult.FailedDetailCount = combinedResult.ExpectedDetailCount.Value -
                        combinedResult.RetrievedDetailCount;
                    combinedResult.IsSuccess = false;
                    combinedResult.ResultMessage = "Sistemik hata nedeniyle sonraki ayrıntı istekleri gönderilmedi.";
                    throw new YoksisProviderException(exception.Message, true, exception,
                        combinedResult);
                }
            }
        }

        combinedResult.RecordCount = combinedResult.Records.Count;
        combinedResult.FailedDetailCount = combinedResult.ExpectedDetailCount.Value -
            combinedResult.RetrievedDetailCount;
        combinedResult.IsSuccess = combinedResult.FailedDetailCount == 0;
        combinedResult.ResultMessage = combinedResult.FailedDetailCount == 0
            ? "Bütün ayrıntılar alındı."
            : $"{combinedResult.FailedDetailCount} ayrıntı isteği tamamlanamadı.";
        return combinedResult;
    }

    private static void Report(IProgress<YoksisCollectionProgress>? progress,
        string stage, string message, YoksisOperationDefinition operation,
        int current, int total, int? recordCount = null)
    {
        progress?.Report(new()
        {
            Stage = stage,
            Message = message,
            CategoryName = operation.CategoryName,
            OperationName = operation.OperationName,
            Current = current,
            Total = total,
            RecordCount = recordCount
        });
    }

    private static void MergeDetail(
        YoksisOperationResult target,
        YoksisOperationResult source,
        string identifierFieldName,
        string requestedIdentifier)
    {
        target.RequestCount += source.RequestCount;
        target.RawResponsesXml.AddRange(source.RawResponsesXml);
        bool hasMatchingRecord = source.Records.Any(record =>
            string.Equals(
                NormalizeIdentifier(record.GetValueOrDefault(identifierFieldName)),
                requestedIdentifier,
                StringComparison.Ordinal));
        if (source.IsSuccess && hasMatchingRecord)
        {
            target.Records.AddRange(source.Records.Where(record =>
                string.Equals(
                    NormalizeIdentifier(record.GetValueOrDefault(identifierFieldName)),
                    requestedIdentifier,
                    StringComparison.Ordinal)));
            target.RetrievedDetailCount++;
            return;
        }

        if (source.IsSuccess)
        {
            target.Errors.Add(AddFailure(target, "EmptyDetail"));
        }
        else
        {
            string resultCode = source.ExternalResultCode ??
                source.ResultCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ??
                "Unknown";
            string? explanation = SafeProviderExplanation(source.ResultMessage);
            target.Errors.Add(AddFailure(
                target,
                $"ProviderRejected:{resultCode}",
                explanation is null
                    ? $"YÖKSİS isteği reddetti (sonuç kodu: {resultCode})"
                    : $"YÖKSİS isteği reddetti (sonuç kodu: {resultCode}): {explanation}"));
        }
    }

    private static void PopulatePublicationCoverage(YoksisCollectResponse response)
    {
        string[] publicationDetails = YoksisOperationCatalog.All
            .Where(x => x.IsPublication)
            .Select(x => x.DetailOperationName!)
            .ToArray();
        string[] publicationLists = YoksisOperationCatalog.All
            .Where(x => x.IsPublication)
            .Select(x => x.OperationName)
            .ToArray();
        List<YoksisOperationResult> details = response.Categories
            .Where(x => publicationDetails.Contains(x.OperationName, StringComparer.Ordinal))
            .ToList();
        bool allListsSucceeded = publicationLists.All(name => response.Categories
            .Any(x => x.OperationName == name && x.IsSuccess));

        response.PublicationDetailRetrievedCount = details.Sum(x => x.RetrievedDetailCount);
        response.PublicationDetailFailedCount = details.Sum(x => x.FailedDetailCount);
        response.PublicationDetailTotalCount = allListsSucceeded &&
            details.All(x => x.ExpectedDetailCount.HasValue)
            ? details.Sum(x => x.ExpectedDetailCount!.Value)
            : null;
        response.PublicationFailureReasons = details.SelectMany(x => x.FailureReasons)
            .GroupBy(x => new { x.Code, x.Description })
            .Select(group => new YoksisFailureSummary
            {
                Code = group.Key.Code,
                Description = group.Key.Description,
                AffectedCount = group.Sum(x => x.AffectedCount)
            })
            .OrderByDescending(x => x.AffectedCount)
            .ToList();
        response.FailureReasons = response.Categories.SelectMany(x => x.FailureReasons)
            .GroupBy(x => new { x.Code, x.Description })
            .Select(group => new YoksisFailureSummary
            {
                Code = group.Key.Code,
                Description = group.Key.Description,
                AffectedCount = group.Sum(x => x.AffectedCount)
            })
            .OrderByDescending(x => x.AffectedCount)
            .ToList();
    }

    private static string AddFailure(
        YoksisOperationResult result,
        string code,
        string? descriptionOverride = null)
    {
        string description = code switch
        {
            "EmptyDetail" => "YÖKSİS ayrıntı kaydı döndürmedi",
            "MissingIdentifier" => "YÖKSİS liste kaydında ayrıntı kimliği yok",
            "AccessDenied" => "YÖKSİS erişimi reddetti",
            "TimedOut" => "YÖKSİS zamanında yanıt vermedi",
            "Configuration" => "YÖKSİS bağlantı ayarı eksik",
            "NotAttempted" => "Sistemik hata nedeniyle YÖKSİS ayrıntısı istenemedi",
            "ProviderRejected" => "YÖKSİS ayrıntı isteğini kabul etmedi",
            _ => "YÖKSİS hizmetine ulaşılamadı"
        };
        description = descriptionOverride ?? description;
        YoksisFailureSummary? existing = result.FailureReasons
            .FirstOrDefault(x => x.Code == code && x.Description == description);
        if (existing is null)
        {
            result.FailureReasons.Add(new()
            {
                Code = code,
                Description = description,
                AffectedCount = 1
            });
        }
        else
        {
            existing.AffectedCount++;
        }
        return description;
    }

    private static string? SafeProviderExplanation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;
        string normalized = string.Join(' ', message.Split(
            ['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 160 ? normalized : normalized[..157] + "...";
    }

    private static void AddProviderFailure(YoksisOperationResult result)
    {
        string resultCode = result.ExternalResultCode ??
            result.ResultCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ??
            "Unknown";
        string? explanation = SafeProviderExplanation(result.ResultMessage);
        string safeError = AddFailure(
            result,
            $"ProviderRejected:{resultCode}",
            explanation is null
                ? $"YÖKSİS isteği reddetti (sonuç kodu: {resultCode})"
                : $"YÖKSİS isteği reddetti (sonuç kodu: {resultCode}): {explanation}");
        result.Errors.Clear();
        result.Errors.Add(safeError);
    }

    private static string ClassifyFailure(Exception exception)
    {
        if (exception is TaskCanceledException or TimeoutException)
            return "TimedOut";
        if (exception is InvalidOperationException)
            return "Configuration";
        if (exception is HttpRequestException httpException &&
            httpException.StatusCode is System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden)
            return "AccessDenied";
        return "Unavailable";
    }

    private static List<string> GetDistinctIdentifiers(
        YoksisOperationResult result,
        string fieldName)
    {
        return result.Records
            .Select(record => record.GetValueOrDefault(fieldName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeIdentifier(value)!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool HasDetailOperation(
        YoksisOperationDefinition operation)
    {
        return !string.IsNullOrWhiteSpace(operation.DetailOperationName) &&
            !string.IsNullOrWhiteSpace(operation.DetailRequestElementName) &&
            !string.IsNullOrWhiteSpace(operation.DetailIdentifierFieldName);
    }

    private static YoksisOperationResult CreateFailure(
        YoksisOperationDefinition operation,
        Exception exception)
    {
        YoksisOperationResult? result = new YoksisOperationResult();
        result.CategoryName = operation.CategoryName;
        result.OperationName = operation.OperationName;
        result.IsSuccess = false;
        result.Errors.Add(AddFailure(result, ClassifyFailure(exception)));
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

    internal static string ValidateTcKimlikNo(string? value)
    {
        string? tcKimlikNo = value?.Trim();

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
