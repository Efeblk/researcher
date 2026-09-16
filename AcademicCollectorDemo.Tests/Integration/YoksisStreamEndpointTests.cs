using System.Net.Http.Json;
using System.Text.Json;
using AcademicCollectorDemo.Tests.Infrastructure;

namespace AcademicCollectorDemo.Tests.Integration;

[Collection("SQL Server")]
public sealed class YoksisStreamEndpointTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task CollectStream_MissingConfiguration_ReturnsNdjsonTerminalError()
    {
        using HostProcess host = new(fixture.ConnectionString);
        await host.WaitUntilReadyAsync();

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/Services/AcademicPerformance/V1/Yoksis/CollectStream",
            new { PersonelID = "stream-test", TcKimlikNo = new string('1', 11) });
        string body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.StartsWith("application/x-ndjson", response.Content.Headers.ContentType?.ToString());
        JsonElement[] events = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        Assert.Contains(events, item => item.GetProperty("Type").GetString() == "progress");
        Assert.Equal("error", events[^1].GetProperty("Type").GetString());
        Assert.DoesNotContain("Username", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }
}
