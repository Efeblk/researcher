# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 application built with Serenity and Entity Framework Core. `Program.cs` configures the host; host-only services live in `Host/`. `Modules/AcademicPerformance/` has three main areas: `Service/` contains V1 API contracts/endpoints, application workflows, persistence, provider integrations, researchers, and normalized works; `WebClient/` contains pages, Serenity publication metadata, and UI-only endpoint adapters; `Background/` is reserved for scheduled jobs. Keep namespaces aligned with the responsibility folders such as `Researchers/Collection`, `Researchers/Models`, `Works/Processing`, and `Works/Models`. FluentMigrator changes live in `Service/Data/Migrations/Core` or `Providers` and run at startup. Technical notes are in `docs/`, generated browser assets in `wwwroot/esm/`, and request examples in `Requests/AcademicPerformance.http`.

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

## Coding Style & Naming Conventions

Use four-space indentation. Use PascalCase for types, methods, and public properties; camelCase for parameters and locals; `_camelCase` for private fields. Keep fields and properties at the top of each class. Initialize nullable reference members with `null` when no value exists. Keep provider DTOs and clients in their integration folder, exposing normalized data through shared researcher/work models. Run `dotnet format` before broad formatting changes.

## Testing Guidelines

Add tests in `AcademicCollectorDemo.Tests` using xUnit. Name files after the tested class, such as `ResearcherIdentifierParserTests.cs`, and methods as `Method_Condition_ExpectedResult`. Cover identifier parsing, provider response mapping, caching, and work deduplication. Avoid live paid API calls in automated tests; use synthetic or captured, sanitized JSON fixtures.

## Commit & Pull Request Guidelines

History uses short subjects such as `readme ve settings` and `update on feedback`. Keep commits focused and state the change. Pull requests should explain behavior, list validation commands, mention schema or configuration changes, and link issues. Include screenshots for UI changes and sample output for endpoints.

## Security & Configuration

Store API keys and YÖKSİS credentials with `dotnet user-secrets`; never commit credentials, T.C. identity numbers, raw secrets, or personal database files. Keep non-secret defaults in `academicsettings.json` and database configuration in `appsettings.json`. `DevelopmentPermissionService` deliberately allows all requests in this standalone host and must be replaced by BYS authorization before production deployment.
