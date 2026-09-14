using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ResearcherAnalysisService.Integrations.Gemini;

namespace ServiceAcceptancePilot;

/// <summary>Enforces the released request envelope and bounded faculty/review dispatch shapes.</summary>
public sealed class RetainedBudgetHandler(ServiceAcceptanceBudget budget,
    RetainedProviderCapture? capture = null, bool allowFacultyInitialMedium = false) : DelegatingHandler
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? mediumPermitBodyHash;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string? expectedPermit = mediumPermitBodyHash;
            mediumPermitBodyHash = null;
            DateTime startedAt = DateTime.UtcNow;
            ValidateTarget(request);
            byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            BodyFacts facts = InspectBody(body);
            if (facts.Thinking == "medium")
            {
                bool releasedRecovery = (facts.MaximumOutputTokens == 16384 && facts.IsFacultyGeneration) ||
                    (facts.MaximumOutputTokens == 8192 && facts.IsReviewGeneration);
                bool releasedInitial = allowFacultyInitialMedium && expectedPermit is null &&
                    ((facts.MaximumOutputTokens == 16384 && (facts.IsFacultyGeneration || facts.IsFacultyRepair)) ||
                     (facts.MaximumOutputTokens == 8192 && facts.IsFacultyVerification));
                if (!(releasedRecovery && expectedPermit == facts.NormalizedHash || releasedInitial))
                    throw new ServiceAcceptanceBudgetException(
                        "Medium thinking is allowed only for a matching one-shot released MAX_TOKENS recovery.");
            }
            else
            {
                if (facts.Thinking != "high" || facts.MaximumOutputTokens is not (8192 or 16384))
                    throw new ServiceAcceptanceBudgetException("Serialized Gemini bounds are not released values.");
            }

            ServiceAcceptanceReservation reservation = await budget.ReserveAsync(
                body.Length, facts.MaximumOutputTokens, ServiceAcceptanceBudget.RequiredModel, startedAt);
            if (capture is not null)
                await capture.WriteRequestAsync(reservation.Ordinal, startedAt, request, body, cancellationToken);
            HttpResponseMessage? response = null;
            GeminiUsageCompletion? completion = null;
            bool outputLimit = false;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, cancellationToken);
                byte[] responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (capture is not null)
                    await capture.WriteResponseAsync(reservation.Ordinal, response, responseBody, cancellationToken);
                try
                {
                    using JsonDocument document = JsonDocument.Parse(responseBody);
                    completion = GeminiUsagePricing.Parse(document.RootElement,
                        ServiceAcceptanceBudget.RequiredModel, startedAt, (int)response.StatusCode,
                        response.IsSuccessStatusCode ? "Observed" : "Rejected");
                    outputLimit = response.StatusCode == HttpStatusCode.OK &&
                        document.RootElement.TryGetProperty("candidates", out JsonElement candidates) &&
                        candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() == 1 &&
                        candidates[0].TryGetProperty("finishReason", out JsonElement finish) &&
                        finish.GetString() == "MAX_TOKENS";
                }
                catch (JsonException) { }
                return response;
            }
            finally
            {
                await budget.CompleteAsync(reservation.Ordinal, response?.StatusCode, completion);
                ServiceAcceptanceBudgetSnapshot snapshot = budget.Snapshot();
                bool attributable = snapshot.Items.Single(value => value.Ordinal == reservation.Ordinal)
                    is { ActualUsageReliable: true, ReturnedModel: ServiceAcceptanceBudget.RequiredModel };
                bool recoverableGeneration = (facts.IsFacultyGeneration && facts.MaximumOutputTokens == 16384) ||
                    (facts.IsReviewGeneration && facts.MaximumOutputTokens == 8192);
                mediumPermitBodyHash = facts.Thinking == "high" && recoverableGeneration &&
                    outputLimit && attributable && !snapshot.DispatchStopped ? facts.NormalizedHash : null;
            }
        }
        finally { gate.Release(); }
    }

    internal static BodyFacts InspectBody(byte[] body)
    {
        try
        {
            JsonNode root = JsonNode.Parse(body) ?? throw new JsonException();
            JsonObject generation = root["generationConfig"]?.AsObject() ?? throw new JsonException();
            int maximum = generation["maxOutputTokens"]?.GetValue<int>() ?? throw new JsonException();
            JsonObject thinking = generation["thinkingConfig"]?.AsObject() ?? throw new JsonException();
            string level = thinking["thinkingLevel"]?.GetValue<string>() ?? throw new JsonException();
            string? instructions = root["systemInstruction"]?["parts"]?[0]?["text"]?.GetValue<string>();
            bool faculty = instructions == ResearcherAnalysisService.Analysis.FacultyAssistantPrompt.Instructions;
            bool facultyVerification = instructions ==
                ResearcherAnalysisService.Analysis.FacultyAssistantVerificationPrompt.Instructions;
            bool facultyRepair = instructions ==
                ResearcherAnalysisService.Analysis.FacultyAssistantRepairPrompt.Instructions;
            bool review = ResearcherAnalysisService.Analysis.ArticleReviewer.Roles.Any(role =>
                instructions == ResearcherAnalysisService.Analysis.ArticleReviewPrompt.InstructionsForRole(role));
            thinking.Remove("thinkingLevel");
            string normalized = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))
                .ToLowerInvariant();
            return new(maximum, level, hash, faculty, facultyVerification, facultyRepair, review);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new ServiceAcceptanceBudgetException("Serialized Gemini settings could not be verified.");
        }
    }

    private static void ValidateTarget(HttpRequestMessage request)
    {
        const string path = "/v1beta/models/gemini-3.8-flash:generateContent";
        if (request.Method != HttpMethod.Post || request.RequestUri is null ||
            request.RequestUri.Scheme != Uri.UriSchemeHttps || !request.RequestUri.IsDefaultPort ||
            !string.IsNullOrEmpty(request.RequestUri.UserInfo) || request.RequestUri.Query.Length != 0 ||
            !string.Equals(request.RequestUri.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            request.RequestUri.AbsolutePath != path || request.Content is null)
            throw new ServiceAcceptanceBudgetException("Only the exact Gemini 3.8 Flash generateContent target is allowed.");
    }

    internal sealed record BodyFacts(int MaximumOutputTokens, string Thinking, string NormalizedHash,
        bool IsFacultyGeneration, bool IsFacultyVerification, bool IsFacultyRepair, bool IsReviewGeneration);
}
