using System.Text.Json.Nodes;

namespace ServiceAcceptancePilot;

internal static class FinalServiceAcceptanceCleanup
{
    public static async Task<int> RunAsync()
    {
        string directory = Path.Combine(FinalServiceAcceptancePreflight.FindRoot(), "docs", Program.RunId);
        string statePath = Path.Combine(directory, "state.json");
        string resultPath = Path.Combine(directory, "result.json");
        JsonObject state = JsonNode.Parse(await File.ReadAllTextAsync(statePath))!.AsObject();
        JsonObject result = JsonNode.Parse(await File.ReadAllTextAsync(resultPath))!.AsObject();
        string name = state["databaseName"]?.GetValue<string>() ??
            throw new InvalidOperationException("The recorded database name is absent.");
        FinalAcceptanceDatabase.ValidateName(name);
        if (state["runId"]?.GetValue<string>() != Program.RunId ||
            state["phase"]?.GetValue<string>() != "awaiting-root-audit" ||
            result["runId"]?.GetValue<string>() != Program.RunId ||
            result["success"]?.GetValue<bool>() != true ||
            result["status"]?.GetValue<string>() != "awaiting_root_audit")
            throw new InvalidOperationException("Recorded final acceptance state is not eligible for cleanup.");
        (string master, _) = FinalAcceptanceDatabase.Connections(name);
        await FinalAcceptanceDatabase.DropAsync(master, name);
        state["phase"] = "cleaned";
        state["updatedAtUtc"] = DateTimeOffset.UtcNow;
        result["status"] = "complete";
        result["cleanup"] = "root-released cleanup dropped only the exact recorded final-acceptance database";
        await FinalAcceptanceArtifacts.WriteAtomicAsync(statePath, state.ToJsonString(new() { WriteIndented = true }));
        await FinalAcceptanceArtifacts.WriteAtomicAsync(resultPath, result.ToJsonString(new() { WriteIndented = true }));
        return 0;
    }
}
