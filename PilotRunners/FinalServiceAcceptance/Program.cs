namespace ServiceAcceptancePilot;

public static class Program
{
    public const string RunId = "service-acceptance-20260914-v1";
    public const string LiveReleaseValue = "ROOT_RELEASED_FINAL_SERVICE_ACCEPTANCE_20260914_V1";
    public const string CleanupReleaseValue = "ROOT_RELEASED_FINAL_SERVICE_ACCEPTANCE_CLEANUP_20260914_V1";
    public const bool IncludeQualifiedPriorSpend = false;

    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        return command switch
        {
            "self-test" => await ServiceAcceptanceSelfTest.RunAsync(),
            "preflight" => await FinalServiceAcceptancePreflight.RunAsync(),
            "live" when args.Contains("--execute-paid-acceptance", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("FINAL_SERVICE_ACCEPTANCE_RELEASE") == LiveReleaseValue =>
                await LiveFinalServiceAcceptance.RunAsync(),
            "live" => Refuse("Paid execution"),
            "cleanup" when Environment.GetEnvironmentVariable("FINAL_SERVICE_ACCEPTANCE_CLEANUP_RELEASE") ==
                CleanupReleaseValue => await FinalServiceAcceptanceCleanup.RunAsync(),
            "cleanup" => Refuse("Cleanup"),
            _ => Usage()
        };
    }

    private static int Refuse(string operation)
    {
        Console.Error.WriteLine($"{operation} is locked; supply the exact command argument and internal release value.");
        return 2;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: FinalServiceAcceptance [self-test|preflight|live --execute-paid-acceptance|cleanup]");
        return 2;
    }
}
