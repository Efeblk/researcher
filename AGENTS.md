# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 10 application built with Serenity and Entity Framework Core. `Program.cs` configures the host and dependency injection. Collection code lives under `Modules/AcademicPerformance/`: `Endpoints/` exposes HTTP services, `Researchers/` coordinates records, `Integrations/` contains provider clients and models, `Works/` normalizes publications and files, and `Data/` defines persistence. UI code is in `Modules/AcademicPerformance/UI/`, shared Razor views in `Views/`, browser assets in `wwwroot/esm/`, and request examples in `Requests/AcademicPerformance.http`.

Local runtime data is written to `academic.db` and `Storage/`; neither should be committed. YÖKSİS SOAP code is under `Integrations/Yoksis/` and its HTTP entry point is `Endpoints/YoksisEndpoint.cs`. There is currently no separate test project.

## Build, Test, and Development Commands

- `dotnet restore` restores NuGet dependencies.
- `npm install` installs Serenity front-end build dependencies.
- `make build` or `dotnet build` compiles server and front-end assets.
- `make run` or `dotnet run` starts the service on `http://localhost:5001`.
- `make health` checks whether the server is responding.
- `make collect ID="0000-0001-8560-7482"` collects data for one or more comma-separated identifiers.
- `make clean` deletes SQLite and downloaded files. Stop the server and database tools first.

Run `dotnet test` after tests are added. Also verify affected endpoints.

## Coding Style & Naming Conventions

Use four-space indentation. Use PascalCase for types, methods, and public properties; camelCase for parameters and locals; `_camelCase` for private fields. Keep fields and properties at the top of each class. Initialize nullable reference members with `null` when no value exists. Keep provider DTOs and clients in their integration folder, exposing normalized data through shared researcher/work models. Run `dotnet format` before broad formatting changes.

## Testing Guidelines

Add tests in a future `AcademicCollectorDemo.Tests` project using xUnit. Name files after the tested class, such as `ResearcherIdentifierParserTests.cs`, and methods as `Method_Condition_ExpectedResult`. Cover identifier parsing, provider response mapping, caching, work deduplication, and download safety. Avoid live paid API calls in automated tests; use captured, sanitized JSON fixtures.

## Commit & Pull Request Guidelines

History uses short subjects such as `readme ve settings` and `update on feedback`. Keep commits focused and state the change. Pull requests should explain behavior, list validation commands, mention schema or configuration changes, and link issues. Include screenshots for UI changes and sample output for endpoints.

## Security & Configuration

Store API keys and YÖKSİS credentials with `dotnet user-secrets`; never commit credentials, T.C. identity numbers, raw secrets, downloaded PDFs, or personal database files. Keep non-secret defaults in `academicsettings.json` and database configuration in `appsettings.json`.
