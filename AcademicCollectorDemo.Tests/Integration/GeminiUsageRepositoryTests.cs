using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AcademicCollectorDemo.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ResearcherAnalysisService.Configuration;
using ResearcherAnalysisService.Integrations.Gemini;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class GeminiUsageRepositoryTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Repository_PersistsOrdersAndAggregatesIncludingPendingAsUnknown()
    {
        GeminiUsageRepository repository = CreateRepository();
        DateTime start = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await ClearAsync();
        try
        {
            GeminiSpendingStatus empty = await repository.GetSpendingAsync(default);
            Assert.True(empty.Available);
            Assert.Equal(0, empty.RequestCount);
            Assert.Equal(0, empty.UnknownCount);
            Assert.Equal(0, empty.EstimatedTotalUsd);
            Assert.Null(empty.Since);
            Assert.Empty(empty.Last3);

            for (int index = 0; index < ids.Length; index++)
                await repository.BeginAsync(ids[index], start.AddMinutes(index), $"model-{index + 1}", default);
            for (int index = 0; index < 3; index++)
                await repository.CompleteAsync(ids[index], start.AddMinutes(index).AddSeconds(1), new()
                {
                    Outcome = "Success", HttpStatus = 200, ReturnedModel = $"returned-{index + 1}",
                    PromptTokenCount = 100, CachedTokenCount = 0, CandidateTokenCount = 20,
                    ThoughtTokenCount = 0, TotalTokenCount = 120, PricingVersion = "synthetic-pricing",
                    EstimatedUsd = (index + 1) / 10m
                }, default);

            GeminiSpendingStatus pending = await repository.GetSpendingAsync(default);

            Assert.True(pending.Available);
            Assert.Equal(start, pending.Since);
            Assert.Equal(4, pending.RequestCount);
            Assert.Equal(1, pending.UnknownCount);
            Assert.Null(pending.EstimatedTotalUsd);
            Assert.Equal(["model-4", "returned-3", "returned-2"], pending.Last3.Select(item => item.Model));
            Assert.Null(pending.Last3[0].EstimatedUsd);

            await repository.CompleteAsync(ids[3], start.AddMinutes(4), new()
            {
                Outcome = "Rejected", HttpStatus = 429, ReturnedModel = "returned-4",
                EstimatedUsd = 0.4m, PricingVersion = "synthetic-pricing"
            }, default);
            GeminiSpendingStatus complete = await CreateRepository().GetSpendingAsync(default);
            Assert.Equal(0, complete.UnknownCount);
            Assert.Equal(1.0m, complete.EstimatedTotalUsd);
            Assert.Equal("returned-4", complete.Last3[0].Model);

            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Outcome,HttpStatus,PromptTokenCount,EstimatedUsd FROM [analysis].[GeminiUsageAttempts] WHERE AttemptId=@id";
            command.Parameters.AddWithValue("@id", ids[0]);
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Success", reader.GetString(0));
            Assert.Equal(200, reader.GetInt32(1));
            Assert.Equal(100, reader.GetInt64(2));
            Assert.Equal(0.1m, reader.GetDecimal(3));
        }
        finally
        {
            await ClearAsync();
        }
    }

    [Fact]
    public async Task CompletionFailure_LeavesInsertedRowPendingAndAggregateUnknown()
    {
        GeminiUsageRepository repository = CreateRepository();
        await ClearAsync();
        try
        {
            FailingCompletionRepository failing = new(repository);
            int sends = 0;
            using HttpClient http = new(new StubHandler(_ =>
            {
                sends++;
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        candidates = new[] { new { content = new { parts = new[] { new { text = "{}" } } },
                            finishReason = "STOP" } },
                        usageMetadata = new { promptTokenCount = 10, candidatesTokenCount = 2,
                            totalTokenCount = 12 },
                        modelVersion = "gemini-3.8-flash"
                    })
                };
            }));
            IOptions<AiOptions> ai = Options.Create(new AiOptions());
            GeminiArticleClient client = new(http, ai,
                Options.Create(new GeminiOptions { ApiKey = "synthetic" }), failing);

            await client.GenerateAsync("gemini-3.8-flash", "instructions", "input", new JsonObject(),
                512, default);

            Assert.Equal(1, sends);
            GeminiSpendingStatus spending = await repository.GetSpendingAsync(default);
            Assert.Equal(1, spending.RequestCount);
            Assert.Equal(1, spending.UnknownCount);
            Assert.Null(spending.EstimatedTotalUsd);
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Outcome,CompletedAt FROM [analysis].[GeminiUsageAttempts]";
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Pending", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
        }
        finally
        {
            await ClearAsync();
        }
    }

    private GeminiUsageRepository CreateRepository() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:UsageDatabase"] = fixture.ConnectionString
        }).Build());

    private async Task ClearAsync()
    {
        await using SqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM [analysis].[GeminiUsageAttempts]";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class FailingCompletionRepository(IGeminiUsageRepository inner) : IGeminiUsageRepository
    {
        public Task BeginAsync(Guid attemptId, DateTime startedAt, string requestedModel,
            CancellationToken cancellationToken) => inner.BeginAsync(attemptId, startedAt, requestedModel,
                cancellationToken);

        public Task CompleteAsync(Guid attemptId, DateTime completedAt, GeminiUsageCompletion completion,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Synthetic update failure.");

        public Task<GeminiSpendingStatus> GetSpendingAsync(CancellationToken cancellationToken) =>
            inner.GetSpendingAsync(cancellationToken);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
