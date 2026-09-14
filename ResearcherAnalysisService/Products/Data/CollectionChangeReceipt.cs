namespace ResearcherAnalysisService.Products.Data;

public sealed class CollectionChangeReceipt
{
    public Guid EventId { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ScheduledAtUtc { get; set; }
}
