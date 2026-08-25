using FluentMigrator.Runner;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Data;

public sealed class AcademicDatabaseMigrator
{
    private readonly IMigrationRunner _migrationRunner;

    public AcademicDatabaseMigrator(IMigrationRunner migrationRunner)
    {
        _migrationRunner = migrationRunner;
    }

    public void MigrateUp()
    {
        _migrationRunner.MigrateUp();
    }
}
