using System.Net;
using System.Net.Sockets;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed class SafeArticleFetcher
{
    private const int MaximumAddresses = 4;
    private readonly Func<Uri, CancellationToken, Task<HttpClient>>? _clientFactory;
    private readonly IOptions<ArticleSummaryOptions> options;

    public SafeArticleFetcher(IOptions<ArticleSummaryOptions> options) : this(options, null) { }
    public SafeArticleFetcher(IOptions<ArticleSummaryOptions> options, Func<Uri, CancellationToken, Task<HttpClient>>? clientFactory)
    { this.options = options; _clientFactory = clientFactory; }

    public async Task<(byte[] Bytes, Uri FinalUri)> FetchPdfAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        using CancellationTokenSource fetch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        fetch.CancelAfter(TimeSpan.FromSeconds(options.Value.FetchTimeoutSeconds));
        Queue<(Uri Uri, int Depth, int Redirects)> pending = new();
        HashSet<string> visited = new(StringComparer.Ordinal);
        HashSet<string> queued = new(StringComparer.Ordinal) { initialUri.AbsoluteUri };
        pending.Enqueue((initialUri, 0, 0));
        ArticleSourceException? lastFailure = null;
        int requests = 0;
        while (pending.Count != 0 && requests < options.Value.MaximumSourceRequests)
        {
            (Uri current, int depth, int redirects) = pending.Dequeue();
            try { ValidateUri(current); }
            catch (ArticleSourceException exception) { lastFailure = exception; continue; }
            if (!visited.Add(current.AbsoluteUri)) continue;
            requests++;
            try
            {
                using HttpClient client = _clientFactory is null ? await CreatePinnedClientAsync(current, fetch.Token) : await _clientFactory(current, fetch.Token);
                using HttpRequestMessage request = new(HttpMethod.Get, current);
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, fetch.Token);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (redirects >= options.Value.MaximumRedirects || response.Headers.Location is null)
                        throw new ArticleSourceException("The article source exceeded the redirect limit.");
                    Uri redirected = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                    ValidateUri(redirected);
                    if (queued.Add(redirected.AbsoluteUri)) pending.Enqueue((redirected, depth, redirects + 1));
                    continue;
                }
                if (!response.IsSuccessStatusCode) throw new ArticleSourceException($"The source returned HTTP status {(int)response.StatusCode}.");
                byte[] body = await ReadBoundedAsync(response.Content, fetch.Token);
                string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) || body.AsSpan().StartsWith("%PDF-"u8)) return (body, current);
                if (!mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                    throw new ArticleSourceException("The saved URL did not return a PDF.");
                if (depth >= options.Value.MaximumLandingDepth)
                    throw new ArticleSourceException("The saved URL is a landing page without an accessible PDF link.");
                foreach (Uri candidate in DiscoverPdfLinks(current, System.Text.Encoding.UTF8.GetString(body)))
                    if (queued.Count < options.Value.MaximumSourceRequests && queued.Add(candidate.AbsoluteUri))
                        pending.Enqueue((candidate, depth + 1, 0));
                if (pending.Count == 0) throw new ArticleSourceException("The saved URL is a landing page without an accessible PDF link.");
            }
            catch (ArticleSourceException exception) { lastFailure = exception; }
            catch (HttpRequestException) { lastFailure = new ArticleSourceException("The article source network request failed."); }
            catch (SocketException) { lastFailure = new ArticleSourceException("The article source network connection failed."); }
            catch (IOException) { lastFailure = new ArticleSourceException("The article source response could not be read."); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !fetch.IsCancellationRequested)
            { lastFailure = new ArticleSourceException("The article source request timed out."); }
        }
        throw lastFailure ?? new ArticleSourceException("The article source exceeded the request limit.");
    }

    public static IReadOnlyList<Uri> DiscoverPdfLinks(Uri landingPage, string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        List<string> values = [];
        values.AddRange(document.QuerySelectorAll("meta").Where(x => string.Equals(x.GetAttribute("name"), "citation_pdf_url", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.GetAttribute("content")).Where(x => !string.IsNullOrWhiteSpace(x))!);
        values.AddRange(document.QuerySelectorAll("link[type]").Where(x => x.GetAttribute("type")?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => x.GetAttribute("href")).Where(x => !string.IsNullOrWhiteSpace(x))!);
        values.AddRange(document.QuerySelectorAll("a[href]").Select(x => x.GetAttribute("href")).Where(x => !string.IsNullOrWhiteSpace(x) && IsPdfPath(x!))!);
        List<Uri> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!Uri.TryCreate(landingPage, value, out Uri? uri)) continue;
            try { ValidateUri(uri); } catch (ArticleSourceException) { continue; }
            if (seen.Add(uri.AbsoluteUri)) result.Add(uri);
        }
        return result;
    }

    private static bool IsPdfPath(string value) => Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? uri) &&
        ((uri.IsAbsoluteUri ? uri.AbsolutePath : value.Split('?', '#')[0]).EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("/download/article-file/", StringComparison.OrdinalIgnoreCase));

    private async Task<HttpClient> CreatePinnedClientAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = (await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken)).Where(IsPublic).Take(MaximumAddresses).ToArray();
        if (addresses.Length == 0) throw new ArticleSourceException("The article source resolves to a blocked network address.");
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(options.Value.FetchTimeoutSeconds)
        };
        handler.ConnectCallback = async (context, token) =>
        {
            Exception? failure = null;
            foreach (IPAddress address in addresses)
            {
                token.ThrowIfCancellationRequested();
                using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                attempt.CancelAfter(TimeSpan.FromSeconds(5));
                Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, attempt.Token); return new NetworkStream(socket, ownsSocket: true); }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException) { socket.Dispose(); failure = exception; }
            }
            throw failure ?? new SocketException((int)SocketError.HostUnreachable);
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(options.Value.FetchTimeoutSeconds) };
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > options.Value.MaximumDownloadBytes) throw new ArticleSourceException("The article download exceeds the configured limit.");
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream target = new(); byte[] buffer = new byte[81920]; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        { if (target.Length + read > options.Value.MaximumDownloadBytes) throw new ArticleSourceException("The article download exceeds the configured limit."); target.Write(buffer, 0, read); }
        return target.ToArray();
    }

    public static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.Host) ||
            IPAddress.TryParse(uri.Host, out IPAddress? ip) && !IsPublic(ip)) throw new ArticleSourceException("The saved article URL is not a permitted public HTTP(S) URL.");
    }
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork) return !(b[0] is 0 or 10 or 127 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && (b[1] == 0 && b[2] is 0 or 2 || b[1] == 168 || b[1] == 88 && b[2] == 99) || b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) || b[0] == 203 && b[1] == 0 && b[2] == 113 || b[0] >= 224);
        return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xfe) == 0 || b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8 || b[0] == 0x20 && b[1] == 0x02 || b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0);
    }
}
