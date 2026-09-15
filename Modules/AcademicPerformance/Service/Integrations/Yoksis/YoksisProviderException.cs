namespace AcademicCollectorDemo.Modules.AcademicPerformance.Integrations.Yoksis;

internal sealed class YoksisProviderException : Exception
{
    public bool StopsCollection { get; }
    public YoksisOperationResult? PartialResult { get; }

    public YoksisProviderException(string message, bool stopsCollection, Exception? inner = null,
        YoksisOperationResult? partialResult = null)
        : base(message, inner)
    {
        StopsCollection = stopsCollection;
        PartialResult = partialResult;
    }
}
