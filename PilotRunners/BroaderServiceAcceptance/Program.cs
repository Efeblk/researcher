namespace ServiceAcceptancePilot;

public static class Program
{
    public const string RunId = "broader-service-acceptance-20260914-v1";
    public const string LiveReleaseValue =
        "ROOT_RELEASED_BROADER_SERVICE_ACCEPTANCE_20260914_V1_6f9384d1";
    public const string CleanupReleaseValue =
        "ROOT_RELEASED_BROADER_SERVICE_ACCEPTANCE_CLEANUP_20260914_V1_6f9384d1";
    public const string ContinuationRunId = "broader-service-acceptance-20260914-v1-continuation";
    public const string ContinuationLiveReleaseValue =
        "ROOT_RELEASED_BROADER_SERVICE_ACCEPTANCE_CONTINUATION_20260914_V1_917ac42e";
    public const string CorrectionRunId =
        "broader-service-acceptance-20260914-v1-correction";
    public const string CorrectionLiveReleaseValue =
        "ROOT_RELEASED_BROADER_SERVICE_ACCEPTANCE_CORRECTION_20260914_V1_b8d337a1";
    public const string FinalRelatedRunId =
        "broader-service-acceptance-20260914-v1-related-final";
    public const string FinalRelatedLiveReleaseValue =
        "ROOT_RELEASED_BROADER_SERVICE_ACCEPTANCE_RELATED_FINAL_20260914_V1_4d9f5c2a";

    public static async Task<int> Main(string[] args)
    {
        string command = args.FirstOrDefault() ?? "preflight";
        return command switch
        {
            "self-test" => await BroaderServiceAcceptancePreflight.RunSelfTestAsync(),
            "preflight" => await BroaderServiceAcceptancePreflight.RunAsync(),
            "continuation-preflight" => await LiveBroaderServiceAcceptance.RunContinuationPreflightAsync(),
            "continuation-v4-preflight" => await LiveBroaderServiceAcceptance.RunContinuationPreflightAsync(
                "preflight-v4.json"),
            "correction-preflight" => await LiveBroaderServiceAcceptance.RunCorrectionPreflightAsync(),
            "final-related-preflight" => await LiveBroaderServiceAcceptance.RunFinalRelatedPreflightAsync(),
            "final-related-live" when args.Contains("--execute-paid-final-related", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("BROADER_SERVICE_ACCEPTANCE_FINAL_RELATED_RELEASE") ==
                    FinalRelatedLiveReleaseValue => await RunFinalRelatedLiveAsync(),
            "final-related-live" => Refuse("Paid final RelatedWorks correction"),
            "correction-live" when args.Contains("--execute-paid-correction", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("BROADER_SERVICE_ACCEPTANCE_CORRECTION_RELEASE") ==
                    CorrectionLiveReleaseValue => await RunCorrectionLiveAsync(),
            "correction-live" => Refuse("Paid correction"),
            "continuation-live" when args.Contains("--execute-paid-continuation", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("BROADER_SERVICE_ACCEPTANCE_CONTINUATION_RELEASE") ==
                    ContinuationLiveReleaseValue => await RunContinuationLiveAsync(),
            "continuation-live" => Refuse("Paid continuation"),
            "live" when args.Contains("--execute-paid-acceptance", StringComparer.Ordinal) &&
                Environment.GetEnvironmentVariable("BROADER_SERVICE_ACCEPTANCE_RELEASE") == LiveReleaseValue =>
                await RunLiveAsync(),
            "live" => Refuse("Paid execution"),
            "cleanup" when Environment.GetEnvironmentVariable("BROADER_SERVICE_ACCEPTANCE_CLEANUP_RELEASE") ==
                CleanupReleaseValue => await LiveBroaderServiceAcceptance.CleanupAsync(),
            "cleanup" => Refuse("Cleanup"),
            _ => Usage()
        };
    }

    private static async Task<int> RunLiveAsync()
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try { return await LiveBroaderServiceAcceptance.RunAsync(cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> RunContinuationLiveAsync()
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try { return await LiveBroaderServiceAcceptance.RunContinuationAsync(cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> RunCorrectionLiveAsync()
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try { return await LiveBroaderServiceAcceptance.RunCorrectionAsync(cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> RunFinalRelatedLiveAsync()
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try { return await LiveBroaderServiceAcceptance.RunFinalRelatedAsync(cancellation.Token); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static int Refuse(string operation)
    {
        Console.Error.WriteLine($"{operation} is locked; supply the exact argument and internal release value.");
        return 2;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: BroaderServiceAcceptance [self-test|preflight|continuation-preflight|continuation-v4-preflight|correction-preflight|final-related-preflight|final-related-live --execute-paid-final-related|correction-live --execute-paid-correction|continuation-live --execute-paid-continuation|live --execute-paid-acceptance|cleanup]");
        return 2;
    }
}
