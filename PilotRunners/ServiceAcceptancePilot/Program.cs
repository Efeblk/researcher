namespace ServiceAcceptancePilot;

public static class Program
{
    public const string RunId = "service-acceptance-20260913-v1";
    public const string ReleaseValue = "ROOT_RELEASED_SERVICE_ACCEPTANCE_20260913_V1";

    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        return command switch
        {
            "self-test" => await ServiceAcceptanceSelfTest.RunAsync(),
            "preflight" => await ServiceAcceptancePreflight.RunAsync(),
            "rehearse" => await ServiceAcceptanceRehearsal.RunAsync(),
            "cleanup" when Environment.GetEnvironmentVariable("SERVICE_ACCEPTANCE_CLEANUP_RELEASE") ==
                "ROOT_RELEASED_SERVICE_ACCEPTANCE_CLEANUP_20260913_V1" => await ServiceAcceptanceCleanup.RunAsync(),
            "cleanup" => RefuseLive(),
            "live" when args.Contains("--execute-paid-acceptance", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("SERVICE_ACCEPTANCE_RELEASE") == ReleaseValue =>
                await LiveServiceAcceptance.RunAsync(),
            "live" => RefuseLive(),
            _ => Usage()
        };
    }

    private static int RefuseLive()
    {
        Console.Error.WriteLine("Paid execution is locked. Supply --execute-paid-acceptance and the exact root release value.");
        return 2;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: ServiceAcceptancePilot [self-test|preflight|rehearse|live --execute-paid-acceptance|cleanup]");
        return 2;
    }
}
