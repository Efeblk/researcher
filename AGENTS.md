# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 ASP.NET Core service. `Program.cs` configures the host and academic-performance module. Keep feature code under `Modules/AcademicPerformance/`, grouped by responsibility:

- `Endpoints/` exposes HTTP endpoints.
- `Researchers/` contains collection workflows and researcher models.
- `Works/` contains publication models, categorization, synchronization, and link handling.
- `Integrations/Orcid/` contains the active official ORCID API client and models.
- `Data/` contains Entity Framework Core configuration and database initialization.

Store manual HTTP requests in `Requests/`. Runtime output belongs in the ignored `Storage/` directory; build artifacts belong in `bin/` and `obj/`. Do not commit the local `academic.db` files.

## Build, Test, and Development Commands

- `dotnet restore` restores NuGet dependencies.
- `make build` (or `dotnet build`) compiles the project and reports warnings.
- `make run` starts the API, normally at `http://localhost:5000`.
- `make health` checks the root health endpoint; use `HOST=http://...` to target another instance.
- `make collect ID="<ORCID>"` runs a collection request against a running server.
- `make clean` or `.\collect.ps1 clean` removes the local SQLite database and `Storage/`. Treat this as destructive.

## Coding Style & Naming Conventions

Use four-space indentation and standard C# formatting. Nullable reference types and implicit usings are enabled. Use `PascalCase` for types, methods, and public members; use `camelCase` for parameters and locals. Match existing suffixes such as `Client`, `Repository`, `Service`, `Handler`, `Options`, and `Response`. Keep provider-specific transport models beside their integration client. Prefer asynchronous APIs and append `Async` to asynchronous method names.

## Testing Guidelines

No automated test project currently exists. For every change, run `dotnet build` and exercise affected endpoints with the Make targets or `Requests/AcademicPerformance.http`. New non-trivial logic should include a dedicated test project (for example, `AcademicCollectorDemo.Tests`) with files named `<TypeName>Tests.cs`. Avoid live network dependencies in unit tests; mock external provider clients.

## Commit & Pull Request Guidelines

Recent commits use short, lowercase, imperative summaries such as `web server` and `update on feedback`. Keep each commit focused and describe the resulting change clearly. Pull requests should include a concise summary, validation commands and results, related issue links, and sample request/response output for API changes. Call out configuration, schema, storage, or external-provider behavior changes explicitly.

## Security & Configuration

Keep defaults in `appsettings.json` and `academicsettings.json`, but store API keys with .NET user secrets or environment variables. Never commit credentials, downloaded PDFs, or local database files.
