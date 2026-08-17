using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using AcademicCollectorDemo.Modules.AcademicPerformance.Researchers;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Console;

public sealed class AcademicPerformanceConsoleHost
{
    private const string ClearDatabaseArgument = "--clear-db";
    private const string DatabaseInfoArgument = "--db-info";
    private const string DatabaseRandomArgument = "-db--random";
    private const string StandardDatabaseRandomArgument = "--db-random";

    private readonly ResearcherEndpoint _researcherEndpoint;
    private readonly DatabaseMaintenance _databaseMaintenance;
    private readonly ResearcherConsolePresenter _presenter;

    public AcademicPerformanceConsoleHost(
        ResearcherEndpoint researcherEndpoint,
        DatabaseMaintenance databaseMaintenance,
        ResearcherConsolePresenter presenter)
    {
        _researcherEndpoint = researcherEndpoint;
        _databaseMaintenance = databaseMaintenance;
        _presenter = presenter;
    }

    public async Task RunAsync(string[] args)
    {
        ResearcherCollectRequest? request = null;
        ResearcherCollectResponse? response = null;
        Researcher? randomResearcher = null;

        if (args.Length == 1 && args[0] == ClearDatabaseArgument)
        {
            await _databaseMaintenance.ClearSqliteAsync();
            return;
        }

        if (args.Length == 1 && args[0] == DatabaseInfoArgument)
        {
            await _databaseMaintenance.PrintSummaryAsync();
            return;
        }

        if (args.Length == 1 &&
            (args[0] == DatabaseRandomArgument ||
             args[0] == StandardDatabaseRandomArgument))
        {
            randomResearcher = await _databaseMaintenance.GetRandomResearcherAsync();

            if (randomResearcher is null)
            {
                System.Console.WriteLine("Veritabanında akademisyen kaydı bulunamadı.");
                return;
            }

            _presenter.PrintDatabaseResearcher(randomResearcher);
            return;
        }

        request = new ResearcherCollectRequest();
        request.Identifiers = args.ToList();
        request.UseTestIdentifiers = args.Length == 0;

        response = await _researcherEndpoint.CollectAsync(request);
        _presenter.Print(response);
    }
}
