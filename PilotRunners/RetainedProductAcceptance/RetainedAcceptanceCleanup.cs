using System.Text.Json.Nodes;
using System.Text.Json;

namespace ServiceAcceptancePilot;

internal static class RetainedAcceptanceCleanup
{
    internal static async Task<int> RunAsync()
    {
        string root = RetainedAcceptancePreflight.FindRoot();
        JsonObject state = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs", Program.RunId, "state.json")))!.AsObject();
        JsonObject result = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "docs", Program.RunId, "result.json")))!.AsObject();
        string database = state["databaseName"]?.GetValue<string>() ?? throw new InvalidOperationException("Recorded database absent.");
        RetainedDatabaseClone clone = result["cloneReceipt"]?.Deserialize<RetainedDatabaseClone>() ??
            throw new InvalidOperationException("Recorded clone receipt absent.");
        RetainedAcceptanceDatabase.ValidateTargetName(database);
        if (state["runId"]?.GetValue<string>() != Program.RunId || state["phase"]?.GetValue<string>() != "awaiting-root-audit" ||
            result["success"]?.GetValue<bool>() != true || result["databaseName"]?.GetValue<string>() != database)
            throw new InvalidOperationException("The target is not eligible for released cleanup.");
        if (clone.TargetName != database) throw new InvalidOperationException("Clone receipt target mismatch.");
        await RetainedAcceptanceDatabase.DropTargetAsync(clone);
        state["phase"] = "cleaned"; state["updatedAtUtc"] = DateTimeOffset.UtcNow;
        result["status"] = "complete";
        result["cleanup"] = "root-released cleanup removed only the recorded v10 target and owned files";
        await FinalAcceptanceArtifacts.WriteAtomicAsync(Path.Combine(root, "docs", Program.RunId, "state.json"),
            state.ToJsonString(new() { WriteIndented = true }));
        await FinalAcceptanceArtifacts.WriteAtomicAsync(Path.Combine(root, "docs", Program.RunId, "result.json"),
            result.ToJsonString(new() { WriteIndented = true }));
        return 0;
    }
}
