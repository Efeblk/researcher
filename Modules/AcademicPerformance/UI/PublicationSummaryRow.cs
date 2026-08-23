using AcademicCollectorDemo.Modules.AcademicPerformance.Works;
using Serenity.ComponentModel;
using Serenity.Data;
using Serenity.Data.Mapping;
using System.ComponentModel;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.UI;

[ConnectionKey("AcademicDatabase"), Module("AcademicPerformance"), TableName("PublicationSummaries")]
[DisplayName("Yayınlar"), InstanceName("Yayın")]
[ReadPermission("*")]
public sealed class PublicationSummaryRow : Row<PublicationSummaryRow.RowFields>, IIdRow, INameRow
{
    [DisplayName("ID"), Identity, IdProperty]
    public int? Id { get => fields.Id[this]; set => fields.Id[this] = value; }

    [DisplayName("Akademisyen ID"), NotNull]
    public int? ResearcherId { get => fields.ResearcherId[this]; set => fields.ResearcherId[this] = value; }

    [DisplayName("Başlık"), NotNull, QuickSearch, NameProperty]
    public string? Title { get => fields.Title[this]; set => fields.Title[this] = value; }

    [DisplayName("Yıl")]
    public int? PublicationYear { get => fields.PublicationYear[this]; set => fields.PublicationYear[this] = value; }

    [DisplayName("Tür")]
    public AcademicWorkCategory? Category { get => fields.Category[this]; set => fields.Category[this] = value; }

    [DisplayName("Yazarlar")]
    public string? Authors { get => fields.Authors[this]; set => fields.Authors[this] = value; }

    [DisplayName("Yayın Yeri")]
    public string? Publication { get => fields.Publication[this]; set => fields.Publication[this] = value; }

    [DisplayName("DOI")]
    public string? Doi { get => fields.Doi[this]; set => fields.Doi[this] = value; }

    [DisplayName("Atıf")]
    public int? CitedByCount { get => fields.CitedByCount[this]; set => fields.CitedByCount[this] = value; }

    [DisplayName("Açık Erişim")]
    public bool? IsOpenAccess { get => fields.IsOpenAccess[this]; set => fields.IsOpenAccess[this] = value; }

    [DisplayName("Yayın Bağlantısı")]
    public string? PublicationUrl { get => fields.PublicationUrl[this]; set => fields.PublicationUrl[this] = value; }

    [DisplayName("PDF Bağlantısı")]
    public string? PdfUrl { get => fields.PdfUrl[this]; set => fields.PdfUrl[this] = value; }

    [DisplayName("Kaynaklar")]
    public string? Sources { get => fields.Sources[this]; set => fields.Sources[this] = value; }

    [DisplayName("Güncelleme")]
    public DateTime? UpdatedAt { get => fields.UpdatedAt[this]; set => fields.UpdatedAt[this] = value; }

    public sealed class RowFields : RowFieldsBase
    {
        public Int32Field Id = null!;
        public Int32Field ResearcherId = null!;
        public StringField Title = null!;
        public Int32Field PublicationYear = null!;
        public EnumField<AcademicWorkCategory> Category = null!;
        public StringField Authors = null!;
        public StringField Publication = null!;
        public StringField Doi = null!;
        public Int32Field CitedByCount = null!;
        public BooleanField IsOpenAccess = null!;
        public StringField PublicationUrl = null!;
        public StringField PdfUrl = null!;
        public StringField Sources = null!;
        public DateTimeField UpdatedAt = null!;
    }
}
