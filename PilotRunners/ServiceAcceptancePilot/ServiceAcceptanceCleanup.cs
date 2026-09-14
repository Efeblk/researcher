using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceAcceptancePilot;

internal static class ServiceAcceptanceCleanup
{
    public static async Task<int> RunAsync()
    {
        string directory = Path.Combine(ServiceAcceptancePreflight.FindRoot(), "docs", Program.RunId);
        string statePath = Path.Combine(directory, "run-state.json");
        string resultPath = Path.Combine(directory, "result.json");
        JsonObject state = JsonNode.Parse(await File.ReadAllTextAsync(statePath))!.AsObject();
        JsonObject result = JsonNode.Parse(await File.ReadAllTextAsync(resultPath))!.AsObject();
        string name = state["databaseName"]!.GetValue<string>();
        if (state["runId"]?.GetValue<string>() != Program.RunId ||
            state["phase"]?.GetValue<string>() != "awaiting-root-cleanup" ||
            result["success"]?.GetValue<bool>() != true ||
            result["status"]?.GetValue<string>() != "awaiting_root_cleanup")
            throw new InvalidOperationException("Recorded acceptance state is not eligible for cleanup.");
        (string master, _) = ServiceAcceptanceRehearsal.Connections(name);
        await ServiceAcceptanceRehearsal.DropAsync(master, name);
        state["phase"] = "cleaned"; state["updatedAtUtc"] = DateTimeOffset.UtcNow;
        result["status"] = "complete";
        result["cleanup"] = "root-released cleanup dropped only the recorded exact acceptance database";
        await File.WriteAllTextAsync(statePath, state.ToJsonString(new() { WriteIndented = true }));
        await File.WriteAllTextAsync(resultPath, result.ToJsonString(new() { WriteIndented = true }));
        return 0;
    }
}
