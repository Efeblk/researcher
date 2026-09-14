# Codebase guide

This application collects academic profiles and publications, keeps provider observations, reconciles canonical works, and lets a researcher choose which legacy publication summaries appear on a school website. It also stores immutable article evidence, computes versioned saved-data metrics, and supports authorization-scoped HR and faculty workflows.

Open `AcademicCollectorDemo.sln` to work with both the collector and the independent `ResearcherAnalysisService`.
The latter accepts snapshot-based JSON APIs for researcher reports, article summaries, specialist reviews, evaluations, and faculty assistance, keeps a SQL Gemini-usage ledger, and has no collector project reference.
The collector builds bounded requests from its saved data, calls that service, and persists product-specific results in SQL Server.
The ID-only `AnalyzeResearcher` and `GetResearcherAnalysis` endpoints generate/save and retrieve
reports respectively. They are a separate legacy compatibility route: generation is opt-in and follows the independent
`Ai:Provider`/`Ai:Model` settings, which default to local Qwen. Current article products, including the faculty-assistant
flow, do not call that route; their Gemini configuration defaults to exact `gemini-3.8-flash`. Both applications reference
the DTO library `ResearcherAnalysis.Contracts/`.
See [Researcher analysis](RESEARCHER_ANALYSIS.md) for its contract, setup, and current integration boundary.
See [Provider status](PROVIDER_STATUS.md) for compact health and verified quota reporting, including
the collector-to-analysis-service Gemini check.

## Run the collector and analysis service

Current article summaries, specialist reviews, evaluations, and faculty assistance cross the
collector-to-analysis-service HTTP boundary. Create the SQL database and start the collector once
so its FluentMigrator migrations create the application schemas, including
`analysis.GeminiUsageAttempts`. Configure the analysis service's
`ConnectionStrings:UsageDatabase` to that migrated database and set its `Gemini:ApiKey`. When
`Service:ApiKey` is configured on the analysis service, the collector's `AnalysisService:ApiKey`
must contain the same secret; `AnalysisService:BaseUrl` must point to the analysis service.

Run `dotnet run --project AcademicCollectorDemo.csproj` and
`dotnet run --project ResearcherAnalysisService/ResearcherAnalysisService.csproj --launch-profile http`
in separate terminals. The default health URLs are `http://localhost:5001/` and
`http://localhost:5011/health`. Health does not prove that Gemini credentials, the usage database,
or product authorization are usable. See the repository [README](../README.md#gereksinimler-ve-çalıştırma)
for User Secrets commands and [Academic AI products](ACADEMIC_AI_PRODUCTS.md) for the fail-closed
deployment access seam and product routes.

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
    ArticleReviews/                Manual specialist review, cache, and evidence persistence
    Bulk/                          Persistent batch queue and configurable SQL import
    FacultyAssistant/              Private context and durable three-stage faculty-assistant workflow
    GraphProjection/               Rebuildable evidence graph export
    HrDossiers/                    Immutable HR evidence dossiers and review actions
    Knowledge/                     Owner-scoped, zero-AI deterministic saved-evidence retrieval
    Metrics/                       Versioned metrics and reference populations
    ProductAccess/                 Fail-closed BYS authorization seam
    Researchers/
      Collection/                  Parse identifiers and collect provider data
      Models/                      Shared researcher data
      Persistence/                 Find and save researchers
    Integrations/
      Orcid/                       ORCID client, profile, and work types
      GoogleScholar/               Google Scholar integration
      OpenAlex/                    Provider profile and raw works
      TrDizin/                     Exact-ORCID author/publication collection
      Crossref/                    DOI-only enrichment and positive/negative cache
      SemanticScholar/             Paper enrichment and saved citation context
      WebOfScience/                Web of Science integration
      Yoksis/
        Collection/                SOAP operation catalog and collection workflow
        Persistence/               Save records and normalize YÖKSİS works
    Works/
      Models/                      Normalized and canonical works, summaries, and approvals
      Processing/                  Categorization, legacy summary deduplication, canonical reconciliation
    Data/                          EF context, registration, and SQL migrations
  WebClient/
    Contracts/                     Browser and UI-adapter request/response types
    Endpoints/                     Serenity UI adapters
    Pages/AcademicPerformance/     Page markup, orchestration, and summary panels
    Publications/                  Publication grid and Serenity metadata
  Background/                      Durable bulk, summary, metrics, evaluation, and faculty workers
AcademicCollectorDemo.Tests/
  Unit/                            Identifier parsing and browser storage tests
  Integration/                     Provider, persistence, and endpoint tests
  Infrastructure/                  Shared SQL fixture, host, and fake HTTP handler
Requests/                          Example HTTP requests
docs/                              Architecture, setup, and workflow notes
wwwroot/esm/                       Generated browser bundles; edit TypeScript sources
```

## Follow a collection request

The V1 endpoint accepts an `AcademicDataCollectRequest` and calls the application service. The application service normalizes narrowly recognized ORCID, Google Scholar, and Web of Science export forms without changing the request object, then converts the valid subset into the internal collection request. Invalid optional fields and unsupported Scopus IDs are returned as safe `Warnings`; when no supported identifier remains, collection stops before provider calls.

`ResearcherCollectionHandler` parses identifiers, finds an existing researcher if one matches, and asks `ResearcherCollectionService` to collect provider data. Provider integrations handle HTTP responses and caching. The handler then saves the researcher, synchronizes normalized and canonical works, and rebuilds publication summaries inside a database transaction. Durable bulk jobs call the same application service, so individual and bulk collection produce the same canonical links.

`AcademicPerformanceDtoMapper` turns the saved models into public response DTOs. It only maps data; database queries and workflow decisions stay in the application service.

YÖKSİS has its own collection handler and SOAP operation catalog under `Integrations/Yoksis`. Its persistence code converts supported records into the same shared work model.

## Understand the data layers

| Type of data | Purpose |
| --- | --- |
| Provider profiles and works | Preserve provider-specific fields and provenance. Each provider owns its types. |
| `AcademicWork` | Represent publications from supported providers in a common format. |
| `PublicationSummary` | Present one publication after deduplication, using DOI or normalized title and year. |
| `PublicationDisplayApproval` | Store the researcher's choice to display a summary on the school website. |
| `CanonicalWork` | Give DOI-backed publications a global identity and retain source-scoped identities for unresolved works. |
| Canonical article evidence | Preserve extracted-text snapshots, validated source spans, analysis runs, and sectioned claims for successful article summaries. |
| Canonical article reviews | Preserve manual specialist-review runs, supported findings, and exact span evidence linked to a base analysis. |

OpenAlex provider profile and raw works remain stored separately, while normalized OpenAlex works enter the shared publication list and DOI/title-year deduplication. Public V1 DTOs are separate from EF entities and UI-only contracts.

Provider metrics are also materialized as nullable columns on `core.Researchers` for simple reporting by `PersonelID`. The provider profile tables remain the source of truth. For example:

```sql
SELECT PersonelID, WosCitationCount, WosHIndex,
       OpenAlexCitationCount, OpenAlexHIndex, OpenAlexI10Index,
       ScholarCitationCount, ScholarHIndex, ScholarI10Index
FROM [core].[Researchers]
WHERE PersonelID = @PersonelID;
```

SQL Server tables are grouped by responsibility: shared researcher and publication data in
`core`; provider data in `orcid`, `googlescholar`, `openalex`, `wos`, `yoksis`, `trdizin`,
`crossref`, and `semanticscholar`; saved AI results in `analysis`; queue data in `bulk`; and
provider coordination state in `integrations`. The `dbo` schema is reserved for migration
bookkeeping. Migration `202609110001` creates the Gemini usage ledger, then migration `202609110002` transfers all 28 application tables without recreating them.

## Find the right file for a change

| What you want to change | Start here |
| --- | --- |
| Accepted researcher identifiers | `Service/Researchers/Collection/ResearcherIdentifierParser.cs` |
| A provider's HTTP request or response parsing | `Service/Integrations/<Provider>/<Provider>Client.cs` |
| A provider's profile or publication fields | The type-named profile/work files in that provider folder |
| YÖKSİS operations to collect | `Service/Integrations/Yoksis/Collection/YoksisOperationCatalog.cs` |
| Publication categories | `Service/Works/Processing/AcademicWorkCategorizer.cs` |
| Publication deduplication | `Service/Works/Processing/PublicationSummarySynchronizer.cs` |
| Global publication identity and provenance links | `Service/Works/Processing/CanonicalWorkSynchronizer.cs` and [Canonical academic data](CANONICAL_ACADEMIC_DATA.md) |
| Article queue, source snapshots, and normalized claim evidence | `Service/ArticleSummaries/` and [Article summaries](ARTICLE_SUMMARIES.md) |
| Manual evidence-bound specialist review | `Service/ArticleReviews/` and [Specialist article reviews](ARTICLE_REVIEWS.md) |
| Saved evidence retrieval and graph export | `Service/Knowledge/`, `Service/GraphProjection/`, and [Deterministic data and knowledge layer](DATA_KNOWLEDGE_LAYER.md) |
| HR dossiers and faculty assistant | `Service/HrDossiers/`, `Service/FacultyAssistant/`, and [Academic AI products](ACADEMIC_AI_PRODUCTS.md) |
| Product authorization and BYS adapter | `Service/ProductAccess/AcademicProductAccess.cs` |
| A public API field | `Service/Api/V1/Contracts/`, then `AcademicPerformanceDtoMapper.cs` |
| Form submission and loading states | `WebClient/Pages/AcademicPerformance/AcademicPerformancePage.ts` |
| Profile and comparison panel rendering | `WebClient/Pages/AcademicPerformance/ResearcherSummaryPanels.ts` |
| Grid columns, checkboxes, and selection loading | `WebClient/Publications/PublicationSummaryGrid.ts` |
| Remembered provider identifiers | `WebClient/Pages/AcademicPerformance/ProviderIdentifiers.ts` |
| Database schema | Add a migration under `Service/Data/Migrations/Core` or `Providers` |

Faculty evidence retrieval uses the pinned `academic-evidence-search-v4` catalog. It keeps the authorized person and
optional work scopes fixed while deterministically recognizing bounded Turkish inflections, expanding detected intents
to bounded English source terms, and reserving condition and limitation diversity for spans with explicit matching
evidence. `QueryHash` identifies the normalized original query and `QueryPlanHash` pins its normalized and expanded plan.
This is nonsemantic bounded recall and makes no AI call.

The analysis service runs `faculty-evidence-assistant-v10` and checks each of its at most six candidates independently
through `faculty-evidence-assistant-verification-v7`, using only that candidate's one or two cited spans. A known,
durably attributed output limit or invalid response leaves only that candidate explicitly unverified and omitted; it is
not retried. One `faculty-evidence-assistant-repair-v2` pass may address the first two source-checked unsupported or
uncertain slots without changing supported items, and each proposed replacement is checked independently without
recursion. `faculty-request-coverage-v2` then checks the retained final items. Coverage references them with one-based
`ItemIndexes`. No retained item produces deterministic `unanswered` coverage without a coverage call, while a
coverage-only failure preserves supported items as `partial` with `unavailable` coverage. Fresh service responses require
source-check, repair, and request-coverage audits; null remains readable only for legacy stored reports. These checks do
not prove scientific correctness, exhaustive corpus coverage, complete recall, or the absence of other omissions.

`Ai:FacultyAssistantGenerationThinkingLevel` controls faculty generation independently and defaults to `medium`;
`Ai:FacultyAssistantVerifierThinkingLevel` separately defaults singleton source checks to `medium`. The default path can
make one generation request, up to six initial checks, one repair request, up to two replacement checks, and one coverage
request. If generation is explicitly configured to start at `high`, one exact-model, priced,
attributable, durably recorded output limit may trigger one identical request at `medium`; no other generation failure
is retried. Fresh reports include ordered generation-attempt provenance, and the collector validates either one success
or the high-output-limit/medium-success shape. The default path uses at most 11 provider dispatches and the configured
high-generation recovery path at most 12. The per-call provider timeout is 180 seconds and the outer
`FacultyAssistant:RequestTimeoutSeconds` defaults to 1,800 seconds. This is a total bound rather than a promise that every
maximum call duration can elapse; deployments must keep the two bounded settings coherent.

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

Analysis-service test hosts clear application configuration and user-secret sources before startup and replace provider HTTP traffic with fake handlers. Collector integration hosts use isolated SQL and test-only worker settings. These gates verify contracts and orchestration without spending provider credits. They do not show that a real Gemini review now completes, approve a scientific reference population, prove deployment-specific BYS identity mapping, or benchmark Neo4j when its runtime is unavailable.

Follow [CONTRIBUTING.md](../CONTRIBUTING.md) to create a branch, open a PR, and check CI before merging.

For SQL query imports, bulk jobs, and provider pacing, see [Bulk collection](BULK_COLLECTION.md).
For global DOI identity, current provider observations, rebuild behavior, and API routes, see [Canonical academic data](CANONICAL_ACADEMIC_DATA.md).
For extracted-text snapshots, verified claim evidence, and article summary read routes, see [Article summaries](ARTICLE_SUMMARIES.md).
