# Analysis API simplification

## Goal

Reduce the public analysis HTTP surface from 36 protected operations to 24, while keeping the analysis engines available to persistent product workflows in process. The retained surface consists of 22 product operations, the evaluation-profile discovery operation, and provider diagnostics; `/health` remains unauthenticated.

## Accepted design

- Remove the duplicate stateless researcher, article summary, article review, faculty assistant, and evaluation execution operations. Product workflows continue to call their engine clients directly.
- Expose product routes directly below `/api/v1`; remove the `/products` path segment without compatibility aliases.
- Combine researcher coverage and the latest saved report in `POST /api/v1/researchers/analysis`. A known researcher returns `200` with a nullable `Analysis`; only an unknown researcher returns `404`.
- Combine canonical article status, paged evidence, and the latest saved review in `POST /api/v1/articles/analysis`. A current researcher/work association returns `200` with nullable `Evidence` and `Review`; only a missing association returns `404`.
- Preserve source identity, freshness/staleness information, paging validation, product authorization, and read-only behavior. Compose database reads sequentially because the scoped EF context is not safe for concurrent queries.
- Keep article observation summaries at `/api/v1/articles/summary`, evaluation profiles at `/api/v1/evaluations/profiles`, and provider diagnostics at `/api/v1/internal/provider-status/gemini`.

## Acceptance checks

- Controller metadata exposes exactly 24 protected operations, including 22 product operations, and no retained product route contains `/products`.
- Removed URLs return `404`.
- Combined reads cover empty saved state, missing researcher/work association, validation, evidence paging and serialization, and do not invoke model providers.
- Existing consumers and HTTP examples use the combined response envelopes and the retained persistent workflows.
- The full analysis test project passes; affected pilot projects compile sequentially; HTTP examples contain valid JSON and only live retained routes.
