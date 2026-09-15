# Repository Guidelines

## Project Structure & Module Organization

This repository has two independently runnable .NET 10 services. `AcademicCollectorDemo.csproj` is the Serenity collector; `Program.cs` configures its host and host-only services live in `Host/`. `Modules/AcademicPerformance/Service/` owns collection, provider integrations, researchers, normalized works, canonical reconciliation, publication selection, and bulk processing. `WebClient/` contains pages, Serenity publication metadata, and UI-only adapters; `Background/` contains only the durable bulk collection worker. Provider HTTP pacing lives in `Service/Integrations/RateLimiting/`. Keep namespaces aligned with responsibility folders such as `Researchers/Collection`, `Researchers/Models`, `Works/Processing`, and `Works/Models`.

`ResearcherAnalysisService/` owns all analysis products: researcher and article analysis, summaries, reviews, metrics, knowledge/graph, evaluations, HR dossiers, faculty workflows, their persistence, APIs, and workers. Stateless controllers remain under `Api/V1`; persistent product controllers and workflows live under `Products/`; provider adapters under `Integrations/`; private collector-source mappings under `SourceData/`; and analysis workers under `Background/`. Shared stateless request/response types live in `ResearcherAnalysis.Contracts/`. Collector HTTP examples live in `Requests/AcademicCollector/`; all persistent and stateless analysis examples live in `ResearcherAnalysisService/Requests/`; `Requests/README.md` is only the two-surface index.

Both services use SQL Server only and point to the same database: collector `ConnectionStrings:AcademicDatabase` and analysis `ConnectionStrings:UsageDatabase`. Local defaults target the same SQL Server LocalDB database; production values must come from secure configuration. Each service applies only its own migrations at startup and may start first or concurrently. Collector migrations remain in `Modules/AcademicPerformance/Service/Data/Migrations/{Core,Providers}` with `dbo.VersionInfo` and own collector `core`, provider, `bulk`, and `integrations` objects. Analysis migrations live in `ResearcherAnalysisService/Data/Migrations` with `dbo.ResearcherAnalysisVersionInfo` and own `analysis`, `hr`, and `faculty` objects. Never add cross-service foreign keys or bootstrap collector tables from Analysis. Analysis uses private read-only source models over collector tables in the shared database; its `AnalysisDbContext` rejects writes to those models. Public collector DTOs are under `Service/Api/V1/Contracts`; persistent analysis product DTOs/controllers are under `ResearcherAnalysisService/Products/Api`; YÖKSİS code remains under `Service/Integrations/Yoksis`.

See `docs/CODEBASE_GUIDE.md` for the request flow and folder map. Tests are grouped into `Unit/`, `Integration/`, and shared `Infrastructure/`. Prefer filenames matching their main type, and declare local variables near their first use.

## Build, Test, and Development Commands

- `dotnet restore` restores NuGet dependencies.
- `npm install` installs Serenity front-end build dependencies.
- `make build` or `dotnet build AcademicCollectorDemo.sln` compiles both services and front-end assets.
- `make build-collector` and `make build-analysis` compile one service independently.
- `make run-collector` starts the collector on `http://localhost:5001`; the existing `make run` alias does the same.
- `make run-analysis` starts Researcher Analysis Service on `http://localhost:5011`.
- `make health-collector` and `make health-analysis` check the corresponding service; the existing `make health` alias checks collector.
- `make collect PERSONEL_ID="P-1001" ID="0000-0001-8560-7482"` collects data for one person using one or more space-separated identifiers.
- `make clean` removes .NET build outputs and never deletes the SQL Server database.

Run `dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj`, `dotnet test ResearcherAnalysisService.Tests/ResearcherAnalysisService.Tests.csproj`, `npm run typecheck`, and `npm test`. Scope checks to the affected service when appropriate and verify affected endpoints. SQL tests use LocalDB on Windows or `ACADEMIC_TEST_SQLSERVER`; they never read the applications' database configuration.

Changes limited to `.md`, `.txt`, and `.rst` documentation text or common ignore files (`.gitignore`, `.dockerignore`, `.npmignore`, `.ignore`, `.prettierignore`, and `.eslintignore`) do not require application builds or tests. Changes to code, other configuration, request examples, SQL, JSON, YAML, or other file types require the relevant checks.

## Coding Style & Naming Conventions

Use four-space indentation. Use PascalCase for types, methods, and public properties; camelCase for parameters and locals; `_camelCase` for private fields. Keep fields and properties at the top of each class. Initialize nullable reference members with `null` when no value exists. Keep provider DTOs and clients in their integration folder, exposing normalized data through shared researcher/work models. Run `dotnet format` before broad formatting changes.

## Testing Guidelines

Add collector tests in `AcademicCollectorDemo.Tests` and analysis tests in `ResearcherAnalysisService.Tests` using xUnit. Name files after the tested class, such as `ResearcherIdentifierParserTests.cs`, and methods as `Method_Condition_ExpectedResult`. Cover identifier parsing, provider response mapping, caching, work deduplication, analysis contract validation, and migration ownership as relevant. Avoid live paid API calls in automated tests; use synthetic or captured, sanitized JSON fixtures.

## Commit & Pull Request Guidelines

History uses short subjects such as `readme ve settings` and `update on feedback`. Keep commits focused and state the change. Pull requests should explain behavior, list validation commands, mention schema or configuration changes, and link issues. Include screenshots for UI changes and sample output for endpoints.

## Security & Configuration

Store API keys and YÖKSİS credentials with each owning project's `dotnet user-secrets`; never commit credentials, T.C. identity numbers, raw secrets, or personal database files. Keep collector non-secret defaults in `academicsettings.json` and `appsettings.json`, and analysis defaults in `ResearcherAnalysisService/appsettings.json`. `DevelopmentPermissionService` supplies permissive Serenity/demo permission checks in the standalone collector host. Analysis API routes except `/health` use `X-Analysis-Key` configured by `Service:ApiKey`. Protected knowledge/graph, evaluation, HR, and faculty product routes additionally use the fail-closed `IAcademicProductAccessService` and require a trusted identity/scope adapter for deployment; the service key never substitutes for subject authorization.

## Development Database Policy

This project is currently in development. Treat the application database as disposable and use reset plus recollection as the normal workflow after schema or identity changes. Do not add legacy-data preservation, backfills, dual-read compatibility, or migration-only repair endpoints unless the user explicitly requests them. Reuse existing endpoints and do not add endpoints solely for a transition. This policy does not authorize deleting an actual database during ordinary code changes or production work.

## Agent Workflow

Use GPT-6 Astra to coordinate scope and design, write a short acceptance plan, and delegate bounded implementation, debugging, testing, and documentation work to GPT-5.6 Sol by default. Astra reviews Sol's concise diff and test summary, resolves material architectural ambiguities, and avoids duplicating Sol's work.

Give Sol a minimal, self-contained handoff containing the objective, worktree and branch, allowed files, constraints, and acceptance checks or tests. Do not include the full conversation transcript by default. Prefer one Sol agent for each cohesive task. Use parallel agents only for independent tasks with separate ownership and worktrees.

Before editing, verify the current directory, Git status, and branch. Never allow concurrent writers in one checkout or switch a shared checkout's branch. Keep the coordination checkout on the main branch and make no direct edits there.

Batch related reads, keep command output concise, and reuse established findings. Scale testing to the change while honoring required gates; do not repeat tests unless the relevant risk changed.

If model routing is unavailable, state briefly that an agent cannot switch its own model or change a T3 binding through this file, then provide the handoff for manual routing. Never claim delegation occurred when it did not. Existing authorization continues to govern writes, pushes, and merges; this workflow adds no approval requirements.
