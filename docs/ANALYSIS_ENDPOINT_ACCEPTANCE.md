# Analysis endpoint acceptance

This acceptance record covers the Analysis Service route surface at commit `4d7acee` plus the
route-wide regression test added by this audit. Tests use in-process HTTP hosts, synthetic provider
handlers, and disposable SQL Server databases. They clear application configuration and do not read
developer secrets, call paid providers, or use a personal application database.

## Route inventory and HTTP coverage

The application registers 36 protected `/api/v1/*` controller routes and one public `GET /health`
route. `ServiceEndpointBoundaryTests.AnalysisApiRoutes_ExceptHealth_RejectMissingOrWrongServiceKey`
discovers the runtime endpoint table and sends requests through the real ASP.NET Core pipeline. It
asserts that every protected route returns HTTP 401 for both a missing and incorrect
`X-Analysis-Key`. It also fixes the expected inventory at 36 routes, including all 25 persistent
product actions, so a newly exposed route cannot silently omit the service-key check.

| Surface | Registered routes | HTTP acceptance evidence |
| --- | ---: | --- |
| Stateless researcher analysis | 1 | Authentication, model validation, payload size, provider failure sanitization, successful and partial reports |
| Stateless article summary and review | 6 | Authentication, request/source validation, timeout and provider failure mapping, staged and complete successful flows |
| Stateless evaluation | 2 | Authentication, profile contract, request validation, provider telemetry, successful calibration and review flows |
| Stateless faculty assistant | 1 | Authentication, validation, unavailable-provider behavior, verified and partial successful reports |
| Gemini provider status | 1 | Authentication before upstream use, configuration states, sanitized upstream failures and timeouts, spending availability, cache policy, successful health response |
| Persistent products | 25 | Missing/wrong service-key rejection through HTTP on every route; a mix of HTTP-host, endpoint-method, service, and isolated-SQL tests covers the product workflows |
| Health | 1 | Public HTTP 200 without collector schema, database availability, or AI credentials |

The 25 persistent actions are `AnalyzeResearcher`, `GetResearcherAnalysis`,
`GetResearcherSourceCoverage`, `SummarizeArticle`, `GetArticleSummary`,
`GetArticleSummaryAutomationStatus`, `GetCanonicalArticleEvidence`, `ReviewCanonicalArticle`,
`GetCanonicalArticleReview`, `GetResearcherPublicationMetrics`,
`RefreshResearcherPublicationMetrics`, `SearchAcademicEvidence`, `GetReferencePopulation`,
`ImportReferencePopulation`, `ExportAcademicEvidenceGraph`, `StartArticleEvaluation`,
`GetArticleEvaluation`, `CreateHrEvidenceDossier`, `GetHrEvidenceDossier`,
`AppendHrDossierReviewAction`, `ListHrDossierReviewActions`, `SaveFacultyAssistantContext`,
`GetFacultyAssistantContext`, `StartFacultyAssistant`, and `GetFacultyAssistantRun`.

Protected knowledge/graph, evaluation, HR, and faculty routes also have tests for the separate,
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
