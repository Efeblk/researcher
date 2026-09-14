namespace ServiceAcceptancePilot;

public static class Program
{
    public const string RunId = "service-acceptance-20260914-v14";
    public const string LiveReleaseValue = "ROOT_RELEASED_RETAINED_FACULTY_COMPLETION_20260914_V14";

    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        return command switch
        {
            "self-test" => await RetainedAcceptanceSelfTest.RunAsync(),
            "preflight" => await RetainedTeachingDiagnosticPreflight.RunAsync(),
            "live" when args.Contains("--execute-paid-acceptance", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("RETAINED_PRODUCT_ACCEPTANCE_RELEASE") == LiveReleaseValue =>
                await RetainedFacultyCompletion.RunAsync(),
            "live" => Refuse("Paid execution"),
            _ => Usage()
        };
    }

    private static int Refuse(string operation)
    {
        Console.Error.WriteLine($"{operation} is locked; supply the exact command and internal release value.");
        return 2;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: RetainedProductAcceptance [self-test|preflight|live --execute-paid-acceptance]");
        return 2;
    }
}
