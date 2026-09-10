using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.ArticleSummaries;

public sealed partial class SafeArticleFetcher
{
    private readonly Func<Uri, CancellationToken, Task<HttpClient>>? _clientFactory;
    private readonly IOptions<ArticleSummaryOptions> options;

    public SafeArticleFetcher(IOptions<ArticleSummaryOptions> options) : this(options, null) { }
    public SafeArticleFetcher(IOptions<ArticleSummaryOptions> options, Func<Uri, CancellationToken, Task<HttpClient>>? clientFactory)
    {
        this.options = options;
        _clientFactory = clientFactory;
    }
    public async Task<(byte[] Bytes, Uri FinalUri)> FetchPdfAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        using CancellationTokenSource fetch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        fetch.CancelAfter(TimeSpan.FromSeconds(options.Value.FetchTimeoutSeconds));
        cancellationToken = fetch.Token;
        ValidateUri(initialUri);
        Uri current = initialUri;
        for (int redirect = 0; redirect <= options.Value.MaximumRedirects; redirect++)
        {
            using HttpClient client = _clientFactory is null ? await CreatePinnedClientAsync(current, cancellationToken) : await _clientFactory(current, cancellationToken);
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirect == options.Value.MaximumRedirects || response.Headers.Location is null)
                    throw new ArticleSourceException("The article source exceeded the redirect limit.");
                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                ValidateUri(current);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new ArticleSourceException($"The source returned HTTP status {(int)response.StatusCode}.");
            byte[] body = await ReadBoundedAsync(response.Content, cancellationToken);
            string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) || body.AsSpan().StartsWith("%PDF-"u8))
                return (body, current);
            if (mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                string html = System.Text.Encoding.UTF8.GetString(body);
                Match match = PdfLinkRegex().Matches(html).Cast<Match>().FirstOrDefault(m =>
                    m.Groups[1].Value.Contains("citation_pdf_url", StringComparison.OrdinalIgnoreCase) ||
                    m.Groups[2].Value.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) ?? Match.Empty;
                if (match.Success && redirect < options.Value.MaximumRedirects)
                {
                    string link = WebUtility.HtmlDecode(match.Groups[2].Value);
                    current = new Uri(current, link);
                    ValidateUri(current);
                    continue;
                }
                throw new ArticleSourceException("The saved URL is a landing page without an accessible PDF link.");
            }
            throw new ArticleSourceException("The saved URL did not return a PDF.");
        }
        throw new ArticleSourceException("The article source exceeded the redirect limit.");
    }

    private async Task<HttpClient> CreatePinnedClientAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        IPAddress address = addresses.FirstOrDefault(IsPublic) ?? throw new ArticleSourceException("The article source resolves to a blocked network address.");
        SocketsHttpHandler handler = new() { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(options.Value.FetchTimeoutSeconds) };
        handler.ConnectCallback = async (context, token) =>
        {
            Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, token); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(options.Value.FetchTimeoutSeconds) };
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > options.Value.MaximumDownloadBytes)
            throw new ArticleSourceException("The article download exceeds the configured limit.");
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream target = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (target.Length + read > options.Value.MaximumDownloadBytes)
                throw new ArticleSourceException("The article download exceeds the configured limit.");
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }

    public static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            string.IsNullOrWhiteSpace(uri.Host) || IPAddress.TryParse(uri.Host, out IPAddress? ip) && !IsPublic(ip))
            throw new ArticleSourceException("The saved article URL is not a permitted public HTTP(S) URL.");
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !(b[0] is 0 or 10 or 127 ||
                b[0] == 100 && b[1] is >= 64 and <= 127 ||
                b[0] == 169 && b[1] == 254 ||
                b[0] == 172 && b[1] is >= 16 and <= 31 ||
                b[0] == 192 && (b[1] == 0 && b[2] is 0 or 2 || b[1] == 168 || b[1] == 88 && b[2] == 99) ||
                b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) ||
                b[0] == 203 && b[1] == 0 && b[2] == 113 ||
                b[0] >= 224);
        // Permit global unicast only, excluding IANA special-purpose and transition/documentation prefixes.
        return (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xfe) == 0 ||
              b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8 ||
              b[0] == 0x20 && b[1] == 0x02 ||
              b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0);
    }

    [GeneratedRegex("<(?:meta\\s+name=[\\\"'](citation_pdf_url)[\\\"'][^>]*content|a[^>]*href)=[\\\"']([^\\\"']+)[\\\"']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PdfLinkRegex();
}
