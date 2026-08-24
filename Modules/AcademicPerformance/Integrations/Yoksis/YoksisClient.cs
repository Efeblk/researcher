using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

public sealed class YoksisClient
{
    private const string DefaultServiceUrl =
        "https://servisler.yok.gov.tr/ws/OzgecmisV2";
    private const string ServiceNamespace =
        "http://www.yok.gov.tr/ozgecmisv1/2021/01";
    private const string SoapEnvelopeNamespace =
        "http://schemas.xmlsoap.org/soap/envelope/";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public YoksisClient(
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    internal Task<YoksisOperationResult> GetAsync(
        YoksisOperationDefinition operation,
        string tcKimlikNo,
        DateTime? updatedAfter)
    {
        XDocument? envelope = null;

        envelope = CreateEnvelope(
            operation.RequestElementName,
            tcKimlikNo,
            updatedAfter,
            eserId: null);

        return SendAsync(operation, envelope, tcKimlikNo);
    }

    internal Task<YoksisOperationResult> GetDetailAsync(
        YoksisOperationDefinition operation,
        string tcKimlikNo,
        string eserId,
        DateTime? updatedAfter)
    {
        XDocument? envelope = null;

        if (string.IsNullOrWhiteSpace(operation.DetailOperationName) ||
            string.IsNullOrWhiteSpace(operation.DetailRequestElementName) ||
            string.IsNullOrWhiteSpace(operation.DetailCategoryName))
        {
            throw new ArgumentException(
                "Bu YÖKSİS kategorisi için ayrıntı işlemi tanımlı değil.");
        }

        envelope = CreateEnvelope(
            operation.DetailRequestElementName,
            tcKimlikNo,
            updatedAfter,
            eserId);

        return SendAsync(
            new YoksisOperationDefinition(
                operation.DetailCategoryName,
                operation.DetailOperationName,
                operation.DetailRequestElementName),
            envelope,
            tcKimlikNo);
    }

    private async Task<YoksisOperationResult> SendAsync(
        YoksisOperationDefinition operation,
        XDocument envelope,
        string tcKimlikNo)
    {
        string? serviceUrl = null;
        string? responseXml = null;
        string? safeError = null;
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        YoksisOperationResult? result = null;

        serviceUrl = _configuration["Yoksis:ServiceUrl"]
            ?? DefaultServiceUrl;

        try
        {
            request = new HttpRequestMessage(HttpMethod.Post, serviceUrl);
            request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
            request.Content = new StringContent(
                envelope.ToString(SaveOptions.DisableFormatting),
                Encoding.UTF8,
                "text/xml");

            response = await _httpClient.SendAsync(request);
            responseXml = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                safeError = GetSafeErrorMessage(responseXml, tcKimlikNo);
                throw new HttpRequestException(
                    $"YÖKSİS SOAP servisi {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}) döndürdü: {safeError}",
                    inner: null,
                    response.StatusCode);
            }

            result = ParseResponse(operation, responseXml, tcKimlikNo);

            if (!result.IsSuccess)
            {
                result.Errors.Add(
                    result.ResultMessage ?? "YÖKSİS işlemi başarısız oldu.");
            }

            return result;
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    private XDocument CreateEnvelope(
        string requestElementName,
        string tcKimlikNo,
        DateTime? updatedAfter,
        string? eserId)
    {
        string? username = null;
        string? password = null;
        XElement? parameters = null;
        XElement? requestElement = null;
        XNamespace service = ServiceNamespace;
        XNamespace soap = SoapEnvelopeNamespace;

        username = _configuration["Yoksis:Username"];
        password = _configuration["Yoksis:Password"];

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                "Yoksis:Username User Secret değeri bulunamadı.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Yoksis:Password User Secret değeri bulunamadı.");
        }

        parameters = new XElement(
            service + "parametre",
            new XElement(service + "P_KULLANICI_ID", username.Trim()),
            new XElement(service + "P_SIFRE", password),
            new XElement(service + "P_TC_KIMLIK_NO", tcKimlikNo));

        if (!string.IsNullOrWhiteSpace(eserId))
        {
            parameters.Add(new XElement(service + "P_ESER_ID", eserId));
        }

        if (updatedAfter.HasValue)
        {
            parameters.Add(new XElement(
                service + "P_TARIH",
                updatedAfter.Value.ToString(
                    "dd/MM/yyyy HH:mm:ss.fff",
                    CultureInfo.InvariantCulture)));
        }

        requestElement = new XElement(
            service + requestElementName,
            parameters);

        return new XDocument(
            new XElement(
                soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", soap),
                new XElement(soap + "Body", requestElement)));
    }

    private YoksisOperationResult ParseResponse(
        YoksisOperationDefinition operation,
        string responseXml,
        string tcKimlikNo)
    {
        XDocument? document = null;
        XElement? body = null;
        XElement? fault = null;
        XElement? responseElement = null;
        XElement? resultElement = null;
        YoksisOperationResult? result = null;
        int resultCode = 0;

        try
        {
            document = XDocument.Parse(responseXml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception) when (
            exception is System.Xml.XmlException ||
            exception is InvalidOperationException)
        {
            throw new HttpRequestException(
                "YÖKSİS geçerli bir SOAP XML yanıtı döndürmedi.",
                exception);
        }

        body = document
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Body");
        fault = body?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Fault");

        if (fault is not null)
        {
            throw new HttpRequestException(
                SanitizeSensitiveData(
                    GetElementValue(fault, "faultstring") ??
                    "YÖKSİS SOAP Fault döndürdü.",
                    tcKimlikNo));
        }

        responseElement = body?.Elements().FirstOrDefault();

        if (responseElement is null)
        {
            throw new HttpRequestException(
                "YÖKSİS SOAP yanıt gövdesi boş geldi.");
        }

        resultElement = responseElement
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Sonuc");
        int.TryParse(
            GetElementValue(resultElement, "SonucKod"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out resultCode);

        result = new YoksisOperationResult();
        result.CategoryName = operation.CategoryName;
        result.OperationName = operation.OperationName;
        result.ResultCode = resultElement is null ? null : resultCode;
        result.ExternalResultCode = GetElementValue(
            resultElement,
            "DisSistemSonucKod");
        result.ResultMessage = GetElementValue(resultElement, "SonucMesaj");
        result.IsSuccess = resultCode == 1;
        result.RequestCount = 1;
        result.Records = CreateRecords(responseElement);
        result.RecordCount = result.Records.Count;
        result.RawResponsesXml.Add(responseXml);
        return result;
    }

    private static List<Dictionary<string, string?>> CreateRecords(
        XElement responseElement)
    {
        List<Dictionary<string, string?>>? records = null;

        records = [];

        foreach (XElement recordElement in responseElement.Elements())
        {
            Dictionary<string, string?>? record = null;

            if (recordElement.Name.LocalName == "Sonuc")
            {
                continue;
            }

            record = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (!recordElement.HasElements)
            {
                record[recordElement.Name.LocalName] = EmptyToNull(
                    recordElement.Value);
            }
            else
            {
                foreach (XElement field in recordElement.Elements())
                {
                    record[field.Name.LocalName] = EmptyToNull(field.Value);
                }
            }

            records.Add(record);
        }

        return records;
    }

    private string GetSafeErrorMessage(
        string responseXml,
        string tcKimlikNo)
    {
        string? message = null;
        XDocument? document = null;

        try
        {
            document = XDocument.Parse(responseXml);
            message = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "faultstring")?
                .Value;
        }
        catch (System.Xml.XmlException)
        {
            message = null;
        }

        message = string.IsNullOrWhiteSpace(message)
            ? "Ayrıntı verilmedi."
            : message.Trim();

        return SanitizeSensitiveData(message, tcKimlikNo);
    }

    private string SanitizeSensitiveData(
        string message,
        string tcKimlikNo)
    {
        string? username = null;
        string? password = null;
        string? sanitizedMessage = null;

        username = _configuration["Yoksis:Username"];
        password = _configuration["Yoksis:Password"];
        sanitizedMessage = message.Replace(
            tcKimlikNo,
            "[TC_KIMLIK_NO_GİZLENDİ]",
            StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(username))
        {
            sanitizedMessage = sanitizedMessage.Replace(
                username,
                "[KULLANICI_GİZLENDİ]",
                StringComparison.Ordinal);
        }

        if (!string.IsNullOrEmpty(password))
        {
            sanitizedMessage = sanitizedMessage.Replace(
                password,
                "[SİFRE_GİZLENDİ]",
                StringComparison.Ordinal);
        }

        return sanitizedMessage;
    }

    private static string? GetElementValue(
        XElement? parent,
        string localName)
    {
        return EmptyToNull(parent?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == localName)?
            .Value);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
