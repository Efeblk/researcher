using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceAcceptancePilot;

internal sealed class FinalAcceptanceArtifacts
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string runId;
    private readonly string directory;
    private int phaseOrdinal;

    public FinalAcceptanceArtifacts(string repositoryRoot, string? artifactRunId = null)
    {
        runId = artifactRunId ?? Program.RunId;
        directory = Path.Combine(repositoryRoot, "docs", runId);
    }

    public string DirectoryPath => directory;

    public void EnsureLiveIsNew()
    {
        Directory.CreateDirectory(directory);
        string[] liveFiles = ["budget.json", "state.json", "result.json", "failure.json"];
        if (liveFiles.Any(file => File.Exists(Path.Combine(directory, file))))
            throw new InvalidOperationException("This final acceptance lifecycle already has live state; implicit rerun is refused.");
        string phases = Path.Combine(directory, "phases");
        if (Directory.Exists(phases) && Directory.EnumerateFileSystemEntries(phases).Any())
            throw new InvalidOperationException("This final acceptance lifecycle already has phase artifacts; implicit rerun is refused.");
        Directory.CreateDirectory(phases);
    }

    public async Task WriteBudgetAsync(ServiceAcceptanceBudgetSnapshot value) =>
        await LockedWriteAsync(Path.Combine(directory, "budget.json"), JsonSerializer.Serialize(value, JsonOptions));

    public async Task WritePhaseAsync(string phase, JsonObject state, ServiceAcceptanceBudgetSnapshot budget)
    {
        await gate.WaitAsync();
        try
        {
            int ordinal = ++phaseOrdinal;
            JsonObject snapshot = (JsonObject)state.DeepClone();
            snapshot["phase"] = phase;
            snapshot["writtenAtUtc"] = DateTimeOffset.UtcNow;
            snapshot["budget"] = JsonSerializer.SerializeToNode(budget, JsonOptions);
            await WriteAtomicAsync(Path.Combine(directory, "phases", $"{ordinal:D3}-{phase}.json"),
                snapshot.ToJsonString(JsonOptions));
            await WriteAtomicAsync(Path.Combine(directory, "state.json"), snapshot.ToJsonString(JsonOptions));
        }
        finally { gate.Release(); }
    }

    public Task WriteResultAsync(JsonObject value) => LockedWriteAsync(
        Path.Combine(directory, "result.json"), value.ToJsonString(JsonOptions));

    public Task WriteFailureAsync(JsonObject value) => LockedWriteAsync(
        Path.Combine(directory, "failure.json"), value.ToJsonString(JsonOptions));

    public Task WriteHeartbeatAsync(string phase, string databaseName, DateTimeOffset startedAt,
        ServiceAcceptanceBudgetSnapshot budget) => LockedWriteAsync(Path.Combine(directory, "heartbeat.json"),
        JsonSerializer.Serialize(new
        {
            runId,
            phase,
            databaseName,
            updatedAtUtc = DateTimeOffset.UtcNow,
            elapsedSeconds = (DateTimeOffset.UtcNow - startedAt).TotalSeconds,
            calls = budget.Calls,
            knownActualSpendUsd = budget.KnownActualSpendUsd,
            conservativeCommittedSpendUsd = budget.CommittedSpendUsd,
            unknownUsageCalls = budget.UnknownUsageCalls,
            budget.DispatchStopped
        }, JsonOptions));

    private async Task LockedWriteAsync(string path, string value)
    {
        await gate.WaitAsync();
        try { await WriteAtomicAsync(path, value); }
        finally { gate.Release(); }
    }

    internal static async Task WriteAtomicAsync(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, value + Environment.NewLine);
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                File.Move(temporary, path, true);
                return;
            }
            catch (UnauthorizedAccessException) when (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
            catch (IOException exception) when (IsSharingViolation(exception) &&
                DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xffff) is 32 or 33;
}

internal sealed class FinalAcceptanceHeartbeat : IAsyncDisposable
{
    private readonly FinalAcceptanceArtifacts artifacts;
    private readonly ServiceAcceptanceBudget budget;
    private readonly string databaseName;
    private readonly DateTimeOffset startedAt;
    private readonly CancellationTokenSource stop = new();
    private readonly Task loop;
    private string phase = "initialized";

    public FinalAcceptanceHeartbeat(FinalAcceptanceArtifacts artifacts, ServiceAcceptanceBudget budget,
        string databaseName, DateTimeOffset startedAt)
    {
        this.artifacts = artifacts;
        this.budget = budget;
        this.databaseName = databaseName;
        this.startedAt = startedAt;
        loop = RunAsync();
    }

    public void Set(string value)
    {
        Volatile.Write(ref phase, value);
        budget.SetPhase(value);
    }

    private async Task RunAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        try
        {
            await artifacts.WriteHeartbeatAsync(phase, databaseName, startedAt, budget.Snapshot());
            while (await timer.WaitForNextTickAsync(stop.Token))
                await artifacts.WriteHeartbeatAsync(Volatile.Read(ref phase), databaseName, startedAt,
                    budget.Snapshot());
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        await loop;
        stop.Dispose();
    }
}
