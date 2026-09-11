# Article summaries

Start the collector and the independent analysis service. Researcher reports continue to use the independent `Ai:Provider` and `Ai:Model` settings. Article summaries use `Ai:ArticleProvider` (`Gemini` by default), `Ai:ArticleModel` (`gemini-3.8-flash`), a conservative 131,072-token application budget, and 8,192-token summary and verifier output reservations. Configure the hosted credential outside source control:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY" --project ResearcherAnalysisService
dotnet user-secrets set "ConnectionStrings:UsageDatabase" "Server=(localdb)\MSSQLLocalDB;Database=AcademicCollectorDemo;Integrated Security=true;TrustServerCertificate=true" --project ResearcherAnalysisService
```

`ConnectionStrings:UsageDatabase` has no default or fallback. Point it explicitly at the same SQL
Server database that the collector migrates. Start the collector first so migration `202609110001`
creates `GeminiUsageAttempts`, then start or restart `ResearcherAnalysisService`. A Gemini generation
request is not sent when its Pending usage-attempt row cannot be inserted.

Gemini requests use structured JSON output, temperature zero, and high thinking for both summary generation and claim verification. The API key is sent in the `x-goog-api-key` header and is never included in the request URL. To use local Ollama explicitly, set `Ai:ArticleProvider` to `Ollama`, set `Ai:ArticleModel` (and optionally `Ai:ArticleVerifierModel`) to installed local models, and run Ollama 0.33.3 or newer. This does not change the provider used for researcher reports.

Each `generateContent` HTTP attempt is recorded in SQL before it is sent and completed afterward,
including rejected, timed-out, cancelled, network-failed, malformed, and successful responses. The
ledger stores timestamps, model identifiers, outcome, HTTP status, token counts, pricing version,
and the estimate only. It never stores prompts, source content, keys, request URLs, researcher IDs,
or DOIs. A completion-write failure intentionally leaves the durable row Pending so its cost remains
unknown. The metadata-only Gemini provider-status request and Ollama calls are not usage attempts.

Call `SummarizeArticle` with `PersonelID`, `AcademicWorkId`, and optional language (`tr` by default). The work must belong to that researcher. The collector never accepts a fetch URL from the request. `AcademicWork.Link` is the canonical article landing page and `FullTextUrl` is the preferred PDF candidate. Additional provider and previously stored URLs are retained with their origin in `core.AcademicWorkSources`. Candidates from the selected work and same-researcher works with the exact normalized DOI are tried with PDF candidates first. A successful final PDF URL is saved back as the preferred candidate. The collector downloads only public HTTP(S) content, extracts layout-aware page text, and stores that exact source snapshot and its SHA-256 hash with the report.

The workflow first recovers abstracts already present in the selected or exact-DOI provider payloads. It then tries saved PDF and landing-page candidates. HTML is accepted as `sourceKind: html` only when a semantic article body passes the extraction checks; its actual final URL is saved as an HTML source and is not written to `FullTextUrl` or labeled `ResolvedPdf`. PDF pages without extractable text can use bounded Tesseract OCR and report `pdf_ocr`; native PDFs report `pdf_text`. Unread or OCR-limited pages make the snapshot partial and remain visible in its scope reason.

If saved sources fail, a DOI metadata lookup queries the configured OpenAlex, Crossref, and Unpaywall integrations once through their cache and tries newly discovered unique candidates within the remaining source budget. A semantic HTML abstract can still be retained when the page is not credible full text. When no full text can be used, the recovered abstract is summarized with `sourceKind: abstract`, partial coverage, and an explicit reason. Metadata without usable article text returns an explicit unavailable result and never creates findings. Login/menu pages are not treated as article text. Source acquisition uses only part of the total timeout so an already recovered abstract and the model request retain time. Limits are configured under `ArticleSummary` and `ArticleMetadataEnrichment`.

Landing-page discovery recognizes citation PDF metadata, PDF link elements, `.pdf` anchors including query strings, and DergiPark article-file download links. `MaximumSourceRequests` is one shared HTTP request budget across all saved and enriched candidates, redirects, and landing-page PDF hops for a summary attempt; metadata provider requests have their own limits. Redirect depth, landing-page depth, response bytes, per-address connection time, and total fetch time are also bounded. Every redirect and discovered URL is revalidated, DNS connections are pinned only to public addresses, and failed candidates do not prevent another candidate from being tried while budget remains. Diagnostics identify the candidate origin and a bounded cause without exposing saved URLs, query strings, or raw transport exception details.

The analysis service first sends the complete extracted source. It reserves `Ai:ArticleMaxOutputTokens` inside `Ai:ArticleContextTokens` and conservatively checks the complete UTF-8 request size before sending. Gemini token-limit responses and Ollama context overflows retry with immutable source-span chunks; a fallback chunk is subdivided again only between spans. `Ai:ArticleFallbackChunkBytes` bounds those chunks by UTF-8 bytes. No source character is silently removed. Safety blocks, empty responses, malformed JSON, and unexpected finish reasons fail closed rather than being accepted.

Gemini's response schema does not enumerate every source ID because large source-ID enums are rejected by Gemini before generation. The IDs remain in the model input, and the Gemini adapter rejects every returned source ID that is not an exact member of the current source-span set. Ollama continues to receive the enumerated source-ID schema.

Each source span has a deterministic ID derived from its canonical page, offsets, and exact text. The summary model returns claims and source IDs only. The service rejects unknown IDs or IDs outside the current chunk, then resolves quotes, page numbers, and offsets from the stored source itself. The model cannot alter quoted evidence.

A separate automatic model pass checks every candidate claim against its cited spans and neighboring context. It checks quantities, conditions, comparisons, negation, causality, and whether language is an author assertion or an established result. Missing, duplicate, or unknown verdicts fail closed. Unsupported and uncertain claims are omitted and counted with reasons. Verification batches split automatically on context or output limits; a singleton that still exhausts its budget is conservatively omitted as budget-unverified rather than accepted. If no supported claims remain, the report status is `insufficient_evidence` and the collector does not persist it as a successful summary. `Ai:ArticleVerifierModel` may select another model compatible with the chosen article provider; when unset it uses `Ai:ArticleModel`. Automatic checking does not guarantee correctness, and using the same model family can produce correlated errors.

`GetArticleSummary` reads the latest persisted report without contacting an AI provider. Failed or timed-out generations do not replace earlier successful reports. See `Requests/ArticleSummary.http` for both calls.
