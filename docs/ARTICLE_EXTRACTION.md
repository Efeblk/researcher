# Article extraction

`SafeArticleFetcher.FetchSourceAsync` follows bounded redirects and PDF links while applying the same public-address validation, DNS pinning, response-size limit, and request limit as `FetchPdfAsync`. A PDF wins when any discovered candidate succeeds. If all candidates fail, the original HTML response is returned for local parsing. HTML parsing never loads scripts or makes network requests.

`ArticleHtmlExtractor` accepts only explicit semantic article-body containers. It removes navigation, scripts, forms, references, related content, and similar non-article sections, then requires multiple substantial paragraphs and a section heading. Abstract metadata is available separately through `TryExtractAbstract`; an abstract, paywall, login page, or generic `main`/`body` element is not treated as full text.

`ArticlePdfExtractor.Extract` remains the native PdfPig path. `ExtractAsync` adds OCR for missing pages when enabled. PDFtoImage renders at bounded DPI and page count, and Tesseract is launched directly with `ProcessStartInfo.ArgumentList`. Each OCR process has a timeout and is killed with its process tree on timeout or cancellation. Temporary page images are deleted in a `finally` block. Missing executables or language data are reported in scope metadata when native text remains readable; they do not affect text-only PDFs.

The PDFium renderer is an in-process native call. Cancellation is observed by the library but cannot guarantee immediate interruption inside every native operation. Page count, DPI, image bytes, per-page OCR time, and total OCR time bound normal operation. Deployments should install Tesseract and its language data and configure `ArticleSummary:TesseractPath`; language tags `en` and `tr` map to Tesseract `eng` and `tur`.
