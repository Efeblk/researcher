namespace ProductPilot;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        return command switch
        {
            "self-test" => await ProductPilotBudgetTests.RunAsync(),
            "preflight" => await LiveProductPilot.RunPreflightAsync(),
            "retest-preflight" => await LiveProductPilot.RunRetestPreflightAsync(),
            "live" when args.Contains("--execute-paid-pilot", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("PRODUCT_PILOT_RELEASE") ==
                    "ROOT_RELEASED_PRODUCT_PILOT_20260913" => await LiveProductPilot.RunLiveAsync(),
            "live" => RefuseLive(),
            "retest3-live" when args.Contains("--execute-paid-pilot", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("PRODUCT_PILOT_RETEST3_RELEASE") ==
                    "ROOT_RELEASED_PRODUCT_PILOT_RETEST3_20260913" => await LiveProductPilot.RunRetestAsync(),
            "retest3-live" => RefuseLive(),
            _ => Usage()
        };
    }

    private static int RefuseLive()
    {
        Console.Error.WriteLine("Paid execution is locked. Supply --execute-paid-pilot and the exact root release value.");
        return 2;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: ProductPilot [self-test|preflight|retest-preflight|live --execute-paid-pilot|retest3-live --execute-paid-pilot]");
        return 2;
    }
}
