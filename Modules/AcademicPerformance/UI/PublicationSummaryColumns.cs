using Serenity.ComponentModel;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.UI;

[ColumnsScript("AcademicPerformance.PublicationSummary")]
[BasedOnRow(typeof(PublicationSummaryRow), CheckNames = true)]
public sealed class PublicationSummaryColumns
{
    [Width(360)]
    public string? Title { get; set; }

    [Width(70)]
    public int? PublicationYear { get; set; }

    [Width(130)]
    public string? Category { get; set; }

    [Width(230)]
    public string? Authors { get; set; }

    [Width(180)]
    public string? Publication { get; set; }

    [Width(160)]
    public string? Doi { get; set; }

    [Width(70)]
    public int? CitedByCount { get; set; }

    [Width(100)]
    public bool? IsOpenAccess { get; set; }

    [Width(80)]
    public string? PublicationUrl { get; set; }

    [Width(80)]
    public string? PdfUrl { get; set; }

    [Width(120)]
    public string? Sources { get; set; }
}
