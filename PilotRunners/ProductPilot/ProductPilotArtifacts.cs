using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProductPilot;

internal sealed class ProductPilotArtifacts(string repositoryRoot, string runId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory = Path.Combine(repositoryRoot, "docs", runId);
    private int phase;

    public string DirectoryPath => directory;

    public void EnsureNew()
    {
        if (Directory.Exists(directory))
            throw new InvalidOperationException("The product pilot artifact directory already exists.");
        Directory.CreateDirectory(Path.Combine(directory, "phases"));
    }

    public async Task WriteBudgetAsync(ProductPilotBudgetSnapshot budget)
    {
        await gate.WaitAsync();
        try { await WriteAtomicAsync(Path.Combine(directory, "budget.json"), JsonSerializer.Serialize(budget, JsonOptions)); }
        finally { gate.Release(); }
    }

    public async Task WritePhaseAsync(string name, JsonObject state, ProductPilotBudgetSnapshot budget)
    {
        await gate.WaitAsync();
        try
        {
            int ordinal = ++phase;
            JsonObject value = (JsonObject)state.DeepClone();
            value["phase"] = name; value["writtenAtUtc"] = DateTime.UtcNow;
            value["budget"] = JsonSerializer.SerializeToNode(budget, JsonOptions);
            await WriteAtomicAsync(Path.Combine(directory, "phases", $"{ordinal:D3}-{name}.json"), value.ToJsonString(JsonOptions));
            await WriteAtomicAsync(Path.Combine(directory, "state.json"), state.ToJsonString(JsonOptions));
        }
        finally { gate.Release(); }
    }

    public async Task WriteHeartbeatAsync(string name, string databaseName, ProductPilotBudgetSnapshot budget)
    {
        await gate.WaitAsync();
        try
        {
            await WriteAtomicAsync(Path.Combine(directory, "heartbeat.json"), JsonSerializer.Serialize(new
            { phase = name, databaseName, heartbeatAtUtc = DateTime.UtcNow, budget }, JsonOptions));
        }
        finally { gate.Release(); }
    }

    public async Task WriteFinalAsync(JsonObject state, ProductPilotBudgetSnapshot budget)
    {
        await WritePhaseAsync("final", state, budget);
        await gate.WaitAsync();
        try { await WriteAtomicAsync(Path.Combine(directory, "result.json"), state.ToJsonString(JsonOptions)); }
        finally { gate.Release(); }
    }

    private static async Task WriteAtomicAsync(string path, string value)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, value + Environment.NewLine);
        File.Move(temporary, path, true);
    }
}

internal sealed class ProductPilotHeartbeat : IAsyncDisposable
{
    private readonly ProductPilotArtifacts artifacts;
    private readonly ProductPilotBudgetState budget;
    private readonly string databaseName;
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private string phase = "initialized";

    public ProductPilotHeartbeat(ProductPilotArtifacts artifacts, ProductPilotBudgetState budget, string databaseName)
    {
        this.artifacts = artifacts; this.budget = budget; this.databaseName = databaseName;
        loop = RunAsync();
    }

    public void Set(string value) => Volatile.Write(ref phase, value);
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try { await loop; } catch (OperationCanceledException) { }
        stop.Dispose();
    }
    private async Task RunAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(20));
        await artifacts.WriteHeartbeatAsync(phase, databaseName, budget.Snapshot());
        while (await timer.WaitForNextTickAsync(stop.Token))
            await artifacts.WriteHeartbeatAsync(Volatile.Read(ref phase), databaseName, budget.Snapshot());
    }
}
