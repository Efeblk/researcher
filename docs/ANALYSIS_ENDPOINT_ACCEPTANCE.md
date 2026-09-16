# Analysis endpoint acceptance

This acceptance record covers the Analysis Service route surface at commit `4d7acee` plus the
route-wide regression test added by this audit. Tests use in-process HTTP hosts, synthetic provider
handlers, and disposable SQL Server databases. They clear application configuration and do not read
developer secrets, call paid providers, or use a personal application database.

## Route inventory and HTTP coverage

The application registers 24 `/api/v1/*` controller routes and one `GET /health` route.
`ServiceEndpointBoundaryTests.AnalysisApiRoutes_NoServiceKey_InNonDevelopmentHostAreReachable`
discovers the runtime endpoint table and sends requests through the real ASP.NET Core pipeline. It
verifies an ordinary endpoint is reachable without a service key in a non-Development host and fixes
the expected inventory at 24 routes, including all 22 product actions.

| Surface | Registered routes | HTTP acceptance evidence |
| --- | ---: | --- |
| Gemini provider status | 1 | Configuration states, sanitized upstream failures and timeouts, spending availability, cache policy, successful health response without a service key |
| Evaluation profile discovery | 1 | Reachability without a service key and the versioned profile contract |
| Product operations | 22 | Route inventory; service and isolated-SQL tests cover the workflows, including specialist subject authorization |
| Health | 1 | HTTP 200 without collector schema, database availability, or AI credentials |

The 22 product actions are `/api/v1/researchers/analysis/generate`, `/api/v1/researchers/analysis`,
`/api/v1/articles/summary/generate`, `/api/v1/articles/summary`, `/api/v1/articles/analysis`,
`/api/v1/articles/review/generate`, `/api/v1/researchers/metrics`,
`/api/v1/researchers/metrics/refresh`, `/api/v1/knowledge/search`, `/api/v1/knowledge/reference-population`,
`/api/v1/knowledge/reference-population/import`, `/api/v1/knowledge/graph/export`, `/api/v1/evaluations/start`,
`/api/v1/evaluations/status`, `/api/v1/hr/dossiers/create`, `/api/v1/hr/dossiers`,
`/api/v1/hr/dossiers/actions/append`, `/api/v1/hr/dossiers/actions`, `/api/v1/faculty/context/save`,
`/api/v1/faculty/context`, `/api/v1/faculty/assistant/start`, and `/api/v1/faculty/assistant/run`.

Knowledge/graph, evaluation, HR, and faculty routes also have tests for the separate,
fail-closed subject authorization layer. These cover anonymous callers, an unconfigured trusted
identity adapter, uniform cross-subject denial, and authorization before database or provider use.

## Verification

Baseline full suite: 527 tests discovered, 526 passed, 1 skipped, 0 failed. The skipped
`ServiceStartupBoundarySmokeTests.Hosts_MigrateSharedDatabaseInEitherOrderAndConcurrently` test is
an explicit opt-in process smoke test requiring `RUN_SERVICE_BOUNDARY_SMOKE=true` and prebuilt
service executables.

After the route-wide regression was added: 528 tests discovered, 527 passed, 1 skipped, 0 failed.

The opt-in startup test was then run separately after a clean Release solution build:
`ServiceStartupBoundarySmokeTests.Hosts_MigrateSharedDatabaseInEitherOrderAndConcurrently` passed
(1 passed, 0 skipped, 0 failed). It starts both compiled service processes against disposable SQL
fixtures and checks analysis/collector schema inventory and either-order/concurrent startup.

No live-provider acceptance was attempted. Provider behavior is bounded by synthetic HTTP handlers,
so external DNS, TLS, vendor availability, account quota, and current vendor response compatibility
remain deployment checks. The two-process startup smoke test passes when explicitly enabled, but
remains outside the default suite.
