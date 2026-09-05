# Codebase guide

This application collects academic profiles and publications, combines duplicate publications, and lets a researcher choose which publications appear on a school website.

## Start with these files

1. `Program.cs` sets up the web host, registers services, and runs migrations.
2. `Modules/AcademicPerformance/Service/Application/AcademicPerformanceApplicationService.cs` exposes the main use cases: collect data, retrieve a researcher, list publications, and save selections.
3. `Modules/AcademicPerformance/Service/Researchers/Collection/ResearcherCollectionHandler.cs` shows the collection workflow and database transaction.
4. `Modules/AcademicPerformance/WebClient/Pages/AcademicPerformance/AcademicPerformancePage.ts` connects the browser form to the services and publication grid.

Paths below are relative to the repository root. Within the module, C# namespaces omit the physical `Service` folder: for example, `Service/Works/Models` uses `AcademicCollectorDemo.Modules.AcademicPerformance.Works.Models`.

## Folder map

```text
Program.cs                         Application startup
Host/                              Services needed by this standalone host
Modules/AcademicPerformance/
  Service/
    Api/V1/
      Contracts/                   Public request and response types
      Endpoints/                   Supported HTTP API entry points
    Application/                   Use cases and entity-to-API mapping
    Researchers/
      Collection/                  Parse identifiers and collect provider data
      Models/                      Shared researcher data
      Persistence/                 Find and save researchers
    Integrations/
      Orcid/                       ORCID client, profile, and work types
      GoogleScholar/               Google Scholar integration
      OpenAlex/                    Separate comparison data
      WebOfScience/                Web of Science integration
      Yoksis/
        Collection/                SOAP operation catalog and collection workflow
        Persistence/               Save records and normalize YÖKSİS works
    Works/
      Models/                      Normalized works, summaries, and approvals
      Processing/                  Categorization, synchronization, deduplication
    Data/                          EF context, registration, and SQL migrations
  WebClient/
    Contracts/                     Browser and UI-adapter request/response types
    Endpoints/                     Serenity UI adapters
    Pages/AcademicPerformance/     Page markup, orchestration, and summary panels
    Publications/                  Publication grid and Serenity metadata
  Background/                      Reserved for scheduled jobs
AcademicCollectorDemo.Tests/
  Unit/                            Identifier parsing and browser storage tests
  Integration/                     Provider, persistence, and endpoint tests
  Infrastructure/                  Shared SQL fixture, host, and fake HTTP handler
Requests/                          Example HTTP requests
docs/                              Architecture, setup, and workflow notes
wwwroot/esm/                       Generated browser bundles; edit TypeScript sources
```

## Follow a collection request

The V1 endpoint accepts an `AcademicDataCollectRequest` and calls the application service. The application service converts it into the internal collection request.

`ResearcherCollectionHandler` parses identifiers, finds an existing researcher if one matches, and asks `ResearcherCollectionService` to collect provider data. Provider integrations handle HTTP responses and caching. The handler then saves the researcher, synchronizes normalized works, and rebuilds publication summaries inside a database transaction.

`AcademicPerformanceDtoMapper` turns the saved models into public response DTOs. It only maps data; database queries and workflow decisions stay in the application service.

YÖKSİS has its own collection handler and SOAP operation catalog under `Integrations/Yoksis`. Its persistence code converts supported records into the same shared work model.

## Understand the data layers

| Type of data | Purpose |
| --- | --- |
| Provider profiles and works | Preserve provider-specific fields and provenance. Each provider owns its types. |
| `AcademicWork` | Represent publications from supported providers in a common format. |
| `PublicationSummary` | Present one publication after deduplication, using DOI or normalized title and year. |
| `PublicationDisplayApproval` | Store the researcher's choice to display a summary on the school website. |

OpenAlex is collected for comparison and stored separately; its works do not enter the shared publication list. Public V1 DTOs are separate from EF entities and UI-only contracts.

## Find the right file for a change

| What you want to change | Start here |
| --- | --- |
| Accepted researcher identifiers | `Service/Researchers/Collection/ResearcherIdentifierParser.cs` |
| A provider's HTTP request or response parsing | `Service/Integrations/<Provider>/<Provider>Client.cs` |
| A provider's profile or publication fields | The type-named profile/work files in that provider folder |
| YÖKSİS operations to collect | `Service/Integrations/Yoksis/Collection/YoksisOperationCatalog.cs` |
| Publication categories | `Service/Works/Processing/AcademicWorkCategorizer.cs` |
| Publication deduplication | `Service/Works/Processing/PublicationSummarySynchronizer.cs` |
| A public API field | `Service/Api/V1/Contracts/`, then `AcademicPerformanceDtoMapper.cs` |
| Form submission and loading states | `WebClient/Pages/AcademicPerformance/AcademicPerformancePage.ts` |
| Profile and comparison panel rendering | `WebClient/Pages/AcademicPerformance/ResearcherSummaryPanels.ts` |
| Grid columns, checkboxes, and selection loading | `WebClient/Publications/PublicationSummaryGrid.ts` |
| Remembered provider identifiers | `WebClient/Pages/AcademicPerformance/ProviderIdentifiers.ts` |
| Database schema | Add a migration under `Service/Data/Migrations/Core` or `Providers` |

## Naming and boundaries

Use filenames matching their main C# type. Keep provider-specific models with their provider. A `Client` talks to an external service; a `Repository` finds or saves database entities; a `Synchronizer` reconciles collected data with stored records; a `Mapper` converts representations.

Keep local variables near their first use. Name them for their role, such as `researchButton` or `publicationSummaryCount`. Extract a file when it has a separate responsibility, rather than splitting a method just to meet a line limit.

The browser page owns orchestration. The grid reports selection changes through callbacks, and the summary-panel module renders provider details. This keeps those components understandable without relying on page-level global functions.

## Validate a change

```powershell
dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj
npm run typecheck
npm test
```

Integration tests use synthetic provider responses and an isolated SQL Server database. They use Windows LocalDB or `ACADEMIC_TEST_SQLSERVER`, never the application's database settings. Browser storage tests run with Node through `npm test`.

Follow [CONTRIBUTING.md](../CONTRIBUTING.md) to create a branch, open a PR, and check CI before merging.
