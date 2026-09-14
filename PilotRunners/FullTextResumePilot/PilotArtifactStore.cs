using System.Text.Json;
using System.Text.Json.Nodes;

namespace FullTextResumePilot;

internal sealed class PilotArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly string directory;
    private readonly string phaseDirectory;
    private int phaseOrdinal;

    public PilotArtifactStore(string repositoryRoot, string runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new InvalidOperationException("The pilot run ID is invalid.");
        directory = Path.Combine(repositoryRoot, "docs", runId);
        phaseDirectory = Path.Combine(directory, "phases");
        phaseOrdinal = Directory.Exists(phaseDirectory)
            ? Directory.EnumerateFiles(phaseDirectory, "*.json").Count()
            : 0;
    }

    public void EnsureNewRun()
    {
        if (Directory.Exists(directory))
            throw new InvalidOperationException("The acceptance run artifact directory already exists; use --resume.");
        Directory.CreateDirectory(phaseDirectory);
    }

    public async Task<JsonObject> ReadStateAsync()
    {
        string path = Path.Combine(directory, "state.json");
        JsonObject? state = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject;
        return state ?? throw new InvalidOperationException("The persisted pilot state is invalid.");
    }

    public async Task<PilotGeminiBudgetSnapshot> ReadBudgetAsync()
    {
        string path = Path.Combine(directory, "budget.json");
        PilotGeminiBudgetSnapshot? snapshot = JsonSerializer.Deserialize<PilotGeminiBudgetSnapshot>(
            await File.ReadAllTextAsync(path), JsonOptions);
        return snapshot ?? throw new InvalidOperationException("The persisted pilot budget is invalid.");
    }

    public async Task WritePhaseAsync(string phase, JsonObject result, PilotGeminiBudgetSnapshot budget)
    {
        await writeGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(phaseDirectory);
            int ordinal = ++phaseOrdinal;
            JsonObject snapshot = (JsonObject)result.DeepClone();
            snapshot["artifactPhase"] = phase;
            snapshot["artifactWrittenAtUtc"] = DateTime.UtcNow;
            snapshot["budget"] = JsonSerializer.SerializeToNode(budget, JsonOptions);
            await WriteAtomicAsync(Path.Combine(phaseDirectory, $"{ordinal:D3}-{SafeName(phase)}.json"),
                snapshot.ToJsonString(JsonOptions) + Environment.NewLine);
            await WriteAtomicAsync(Path.Combine(directory, "state.json"),
                result.ToJsonString(JsonOptions) + Environment.NewLine);
            await WriteAtomicAsync(Path.Combine(directory, "budget.json"),
                JsonSerializer.Serialize(budget, JsonOptions) + Environment.NewLine);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task WriteHeartbeatAsync(string phase, string databaseName,
        PilotGeminiBudgetSnapshot budget)
    {
        await writeGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(new
            {
                runId = LivePilot.AcceptanceRunId,
                phase,
                databaseName,
                heartbeatAtUtc = DateTime.UtcNow,
                budget
            }, JsonOptions);
            await WriteAtomicAsync(Path.Combine(directory, "heartbeat.json"), json + Environment.NewLine);
            await WriteAtomicAsync(Path.Combine(directory, "budget.json"),
                JsonSerializer.Serialize(budget, JsonOptions) + Environment.NewLine);
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async Task WriteFinalAsync(JsonObject result, PilotGeminiBudgetSnapshot budget)
    {
        await WritePhaseAsync("final", result, budget);
        await writeGate.WaitAsync();
        try
        {
            await WriteAtomicAsync(Path.Combine(directory, "result.json"),
                result.ToJsonString(JsonOptions) + Environment.NewLine);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static string SafeName(string value) => new(value.Select(character =>
        char.IsLetterOrDigit(character) || character == '-' ? character : '-').ToArray());

    private static async Task WriteAtomicAsync(string path, string contents)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, contents);
        File.Move(temporary, path, true);
    }
}

internal sealed class PilotArtifactHeartbeat : IAsyncDisposable
{
    private readonly PilotArtifactStore artifacts;
    private readonly PilotGeminiBudgetState budget;
    private readonly string databaseName;
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private string phase = "initialized";

    public PilotArtifactHeartbeat(PilotArtifactStore artifacts, PilotGeminiBudgetState budget,
        string databaseName)
    {
        this.artifacts = artifacts;
        this.budget = budget;
        this.databaseName = databaseName;
        loop = RunAsync();
    }

    public void SetPhase(string value) => Volatile.Write(ref phase, value);

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try { await loop; }
        catch (OperationCanceledException) { }
        stop.Dispose();
    }

    private async Task RunAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        await artifacts.WriteHeartbeatAsync(Volatile.Read(ref phase), databaseName, budget.Snapshot());
        while (await timer.WaitForNextTickAsync(stop.Token))
            await artifacts.WriteHeartbeatAsync(Volatile.Read(ref phase), databaseName, budget.Snapshot());
    }
}
