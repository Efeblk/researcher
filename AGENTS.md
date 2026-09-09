# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 application built with Serenity and Entity Framework Core. `Program.cs` configures the host; host-only services live in `Host/`. `Modules/AcademicPerformance/` has three main areas: `Service/` contains V1 API contracts/endpoints, application workflows, persistence, provider integrations, researchers, and normalized works; `WebClient/` contains pages, Serenity publication metadata, and UI-only endpoint adapters; `Background/` runs the durable bulk collection worker; `Service/Bulk/` owns queue processing and configurable SQL imports. Provider HTTP pacing lives in `Service/Integrations/RateLimiting/`. Keep namespaces aligned with the responsibility folders such as `Researchers/Collection`, `Researchers/Models`, `Works/Processing`, and `Works/Models`. FluentMigrator changes live in `Service/Data/Migrations/Core` or `Providers` and run at startup. Technical notes are in `docs/`, generated browser assets in `wwwroot/esm/`, and request examples in `Requests/AcademicPerformance.http`.

The application uses SQL Server only; local defaults target SQL Server LocalDB and production connection strings must come from secure configuration. Public DTOs are under `Service/Api/V1/Contracts`, supported HTTP entry points under `Service/Api/V1/Endpoints`, and YÖKSİS code under `Service/Integrations/Yoksis`. Tests live in `AcademicCollectorDemo.Tests/` and use synthetic provider responses and an isolated SQL Server database.

See `docs/CODEBASE_GUIDE.md` for the request flow and folder map. Tests are grouped into `Unit/`, `Integration/`, and shared `Infrastructure/`. Prefer filenames matching their main type, and declare local variables near their first use.

## Build, Test, and Development Commands

- `dotnet restore` restores NuGet dependencies.
- `npm install` installs Serenity front-end build dependencies.
- `make build` or `dotnet build` compiles server and front-end assets.
- `make run` or `dotnet run` starts the service on `http://localhost:5001`.
- `make health` checks whether the server is responding.
- `make collect ID="0000-0001-8560-7482"` collects data for one or more space-separated identifiers.
- `make clean` removes .NET build outputs and never deletes the SQL Server database.

Run `dotnet test AcademicCollectorDemo.Tests/AcademicCollectorDemo.Tests.csproj`, `npm run typecheck`, and `npm test`. Also verify affected endpoints. SQL tests use LocalDB on Windows or `ACADEMIC_TEST_SQLSERVER`; they never read the application's database configuration.

Changes limited to `.md`, `.txt`, and `.rst` documentation text or common ignore files (`.gitignore`, `.dockerignore`, `.npmignore`, `.ignore`, `.prettierignore`, and `.eslintignore`) do not require application builds or tests. Changes to code, other configuration, request examples, SQL, JSON, YAML, or other file types require the relevant checks.

## Coding Style & Naming Conventions

Use four-space indentation. Use PascalCase for types, methods, and public properties; camelCase for parameters and locals; `_camelCase` for private fields. Keep fields and properties at the top of each class. Initialize nullable reference members with `null` when no value exists. Keep provider DTOs and clients in their integration folder, exposing normalized data through shared researcher/work models. Run `dotnet format` before broad formatting changes.

## Testing Guidelines

Add tests in `AcademicCollectorDemo.Tests` using xUnit. Name files after the tested class, such as `ResearcherIdentifierParserTests.cs`, and methods as `Method_Condition_ExpectedResult`. Cover identifier parsing, provider response mapping, caching, and work deduplication. Avoid live paid API calls in automated tests; use synthetic or captured, sanitized JSON fixtures.

## Commit & Pull Request Guidelines

History uses short subjects such as `readme ve settings` and `update on feedback`. Keep commits focused and state the change. Pull requests should explain behavior, list validation commands, mention schema or configuration changes, and link issues. Include screenshots for UI changes and sample output for endpoints.

## Security & Configuration

Store API keys and YÖKSİS credentials with `dotnet user-secrets`; never commit credentials, T.C. identity numbers, raw secrets, or personal database files. Keep non-secret defaults in `academicsettings.json` and database configuration in `appsettings.json`. `DevelopmentPermissionService` deliberately allows all requests in this standalone host and must be replaced by BYS authorization before production deployment.

## Agent Workflow

Use GPT-6 Astra to coordinate scope and design, write a short acceptance plan, and delegate bounded implementation, debugging, testing, and documentation work to GPT-5.6 Sol by default. Astra reviews Sol's concise diff and test summary, resolves material architectural ambiguities, and avoids duplicating Sol's work.

Give Sol a minimal, self-contained handoff containing the objective, worktree and branch, allowed files, constraints, and acceptance checks or tests. Do not include the full conversation transcript by default. Prefer one Sol agent for each cohesive task. Use parallel agents only for independent tasks with separate ownership and worktrees.

Before editing, verify the current directory, Git status, and branch. Never allow concurrent writers in one checkout or switch a shared checkout's branch. Keep the coordination checkout on the main branch and make no direct edits there.

Batch related reads, keep command output concise, and reuse established findings. Scale testing to the change while honoring required gates; do not repeat tests unless the relevant risk changed.

If model routing is unavailable, state briefly that an agent cannot switch its own model or change a T3 binding through this file, then provide the handoff for manual routing. Never claim delegation occurred when it did not. Existing authorization continues to govern writes, pushes, and merges; this workflow adds no approval requirements.
