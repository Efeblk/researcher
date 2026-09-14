namespace ServiceAcceptancePilot;

public static class Program
{
    public const string RunId = "service-acceptance-20260914-v5";
    public const string LiveReleaseValue = "ROOT_RELEASED_QUALIFIED_SERVICE_ACCEPTANCE_20260914_V5";
    public const string CleanupReleaseValue = "ROOT_RELEASED_QUALIFIED_SERVICE_ACCEPTANCE_CLEANUP_20260914_V5";
    public const bool IncludeQualifiedPriorSpend = true;

    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        return command switch
        {
            "self-test" => await ServiceAcceptanceSelfTest.RunAsync(),
            "preflight" => await QualifiedServiceAcceptancePreflight.RunAsync(),
            "retrieval-preflight" => await RetainedV4RetrievalPreflight.RunAsync(),
            "live" when args.Contains("--execute-paid-acceptance", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("QUALIFIED_SERVICE_ACCEPTANCE_RELEASE") == LiveReleaseValue =>
                await LiveFinalServiceAcceptance.RunAsync(QualificationRegression.RunAsync),
            "live" => Refuse("Paid execution"),
            "cleanup" when Environment.GetEnvironmentVariable("QUALIFIED_SERVICE_ACCEPTANCE_CLEANUP_RELEASE") ==
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
        Console.Error.WriteLine("Usage: QualifiedServiceAcceptance [self-test|preflight|retrieval-preflight|live --execute-paid-acceptance|cleanup]");
        return 2;
    }
}
