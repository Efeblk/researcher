using FluentMigrator;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data.Migrations.Core;

[Migration(202609110001, "Track durable Gemini usage attempts")]
public sealed class AddGeminiUsageAttempts : Migration
{
    public override void Up()
    {
        Create.Table("GeminiUsageAttempts")
            .WithColumn("AttemptId").AsGuid().PrimaryKey()
            .WithColumn("StartedAt").AsDateTime2().NotNullable()
            .WithColumn("CompletedAt").AsDateTime2().Nullable()
            .WithColumn("RequestedModel").AsString(200).NotNullable()
            .WithColumn("ReturnedModel").AsString(200).Nullable()
            .WithColumn("Outcome").AsString(40).NotNullable()
            .WithColumn("HttpStatus").AsInt32().Nullable()
            .WithColumn("PromptTokenCount").AsInt64().Nullable()
            .WithColumn("CachedTokenCount").AsInt64().Nullable()
            .WithColumn("CandidateTokenCount").AsInt64().Nullable()
            .WithColumn("ThoughtTokenCount").AsInt64().Nullable()
            .WithColumn("TotalTokenCount").AsInt64().Nullable()
            .WithColumn("PricingVersion").AsString(80).Nullable()
            .WithColumn("EstimatedUsd").AsDecimal(19, 9).Nullable();

        Create.Index("IX_GeminiUsageAttempts_StartedAt_AttemptId")
            .OnTable("GeminiUsageAttempts")
            .OnColumn("StartedAt").Descending()
            .OnColumn("AttemptId").Descending();
    }

    public override void Down() => Delete.Table("GeminiUsageAttempts");
}
