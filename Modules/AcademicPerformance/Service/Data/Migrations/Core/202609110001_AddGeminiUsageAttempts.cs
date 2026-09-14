using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110001, "Track durable Gemini usage attempts")]
public sealed class AddGeminiUsageAttempts : Migration
{
    // This version stays in the collector history for compatibility. The analysis
    // service now exclusively owns the Gemini usage ledger and its migrations.
    public override void Up() { }

    public override void Down() { }
}
