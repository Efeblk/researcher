namespace AcademicCollectorDemo.Modules.AcademicPerformance.Analysis;

public sealed class ResearcherIdentityMismatchException : Exception
{
    public ResearcherIdentityMismatchException()
        : base("PersonelID and ResearcherId must identify the same researcher.")
    {
    }
}
