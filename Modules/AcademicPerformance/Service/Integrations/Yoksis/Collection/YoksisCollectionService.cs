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
                category = CreateFailure(operation, exception.Message);
                response.Categories.Add(category);
                AddFeedback(response.Messages, category);
                if (exception is YoksisProviderException { StopsCollection: true })
                {
                    response.StopReason = exception.Message;
                    break;
                }
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

        if (identifiers.Count == 0)
        {
            combinedResult.ResultMessage = "Ayrıntı istenecek kayıt bulunamadı.";
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
                Merge(combinedResult, detailResult);
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
                combinedResult.IsSuccess = false;
                combinedResult.RequestCount++;
                combinedResult.Errors.Add(
                    $"{operation.DetailIdentifierFieldName}={identifier}: " +
                    exception.Message);
                if (exception is YoksisProviderException { StopsCollection: true })
                {
                    combinedResult.RecordCount = combinedResult.Records.Count;
                    combinedResult.ResultMessage = "Sistemik hata nedeniyle sonraki ayrıntı istekleri gönderilmedi.";
                    throw new YoksisProviderException(exception.Message, true, exception,
                        combinedResult);
                }
            }
        }

        combinedResult.RecordCount = combinedResult.Records.Count;
        combinedResult.ResultMessage = combinedResult.Errors.Count == 0
            ? "Bütün ayrıntılar alındı."
            : $"{combinedResult.Errors.Count} ayrıntı isteği başarısız oldu.";
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
        YoksisOperationResult? result = new YoksisOperationResult();
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
