using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ServiceAcceptancePilot;

internal static class RetainedV10Audit
{
    private const string FailureSha = "97d4aad807d75b09be61a10712b34614148ca8786568c595b56d2189532868f7";
    private const string StateSha = "f88eb67c12246a52c97355ab9929cb787b2d587b348b630cb70720fd6c74508e";
    private const string BudgetSha = "d7611fa0484390cd390b767fbc36d577a233ed798bb3fc98aa4ec7b4b5a20254";
    private const string PreflightSha = "56cdbd7460c168f7d57ff1506255cc3342ab10b72c16c63f779d064ca0a6c36c";

    internal static async Task<JsonObject> InspectAsync(string root)
    {
        string directory = Path.Combine(root, "docs", "service-acceptance-20260914-v10");
        Require(HashFile(Path.Combine(directory, "failure.json")) == FailureSha, "Frozen V10 failure changed.");
        Require(HashFile(Path.Combine(directory, "state.json")) == StateSha, "Frozen V10 state changed.");
        Require(HashFile(Path.Combine(directory, "budget.json")) == BudgetSha, "Frozen V10 budget changed.");
        Require(HashFile(Path.Combine(directory, "preflight.json")) == PreflightSha, "Frozen V10 preflight changed.");
        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is not "failure-artifact-manifest.json" and not "failure-attestation.json")
            .Order(StringComparer.CurrentCulture).ToArray();
        Require(files.Length == 59, "Frozen V10 raw/source artifact count changed.");
        JsonArray entries = [];
        StringBuilder canonical = new();
        foreach (string path in files)
        {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            long bytes = new FileInfo(path).Length;
            string sha = HashFile(path);
            canonical.Append(relative).Append('|').Append(bytes).Append('|').Append(sha).Append('\n');
            entries.Add(new JsonObject { ["path"] = relative, ["bytes"] = bytes, ["sha256"] = sha });
        }
        string aggregate = Hash(Encoding.UTF8.GetBytes(canonical.ToString()));
        Require(aggregate == "5f2dc56a8c16e96a7f37bd1fd41dd41b215360da2ec1467aaee456c5e7bef583",
            "The frozen V10 artifact entry set changed.");
        return new()
        {
            ["directory"] = directory,
            ["artifactCount"] = entries.Count,
            ["entries"] = entries,
            ["computedCanonicalEntriesSha256"] = aggregate,
            ["historicalManifestRecordedSha256"] = "5f2dc56a8c16e96a7f37bd1fd41dd41b215360da2ec1467aaee456c5e7bef583",
            ["sortConvention"] = "PowerShell Sort-Object FullName current-culture order",
            ["historicalManifestSerializationReproduced"] = true,
            ["keyArtifactsVerified"] = true
        };
    }

    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
