# Pull request review instructions

Review changes for correctness, security, data integrity, and maintainability. Report only actionable problems introduced by the pull request. For every finding, identify the affected file and explain a concrete failure scenario.

Apply these project-specific checks:

- The application supports SQL Server only. Flag SQLite assumptions, providers, connection strings, or migrations.
- Never allow API keys, YOKSIS credentials, T.C. identity numbers, raw secrets, personal database files, or sensitive provider responses into source control or logs.
- Keep public V1 contracts under `Service/Api/V1/Contracts` and supported HTTP entry points under `Service/Api/V1/Endpoints`. Flag unintended breaking changes to existing API contracts.
- Keep provider-specific DTOs and clients inside their integration folders. Shared workflows should consume normalized researcher and work models.
- Check provider mappings for missing fields, null values, unsuccessful responses, pagination, cancellation, and partial data.
- Check work deduplication and caching changes for unstable keys, duplicate records, stale data, and accidental cross-researcher data reuse.
- Inspect FluentMigrator changes for unique version numbers, SQL Server compatibility, safe upgrades of existing databases, and consistency with the application models.
- Do not treat generated files in `wwwroot/esm/` as source changes.
- Expect validation evidence for affected endpoints and important workflows. Avoid requesting tests that only repeat implementation details.

Do not approve pull requests. Leave findings as review comments for the author to evaluate and resolve.
