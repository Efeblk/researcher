using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using AcademicCollectorDemo.Modules.AcademicPerformance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.Works.Files;

public sealed class AcademicPdfDownloader
{
    private const int BufferSize = 81920;
    private const int PdfSignatureLength = 5;

    private readonly AcademicDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly PdfSourceExtractor _sourceExtractor;
    private readonly ILogger<AcademicPdfDownloader> _logger;

    public AcademicPdfDownloader(
        AcademicDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        PdfSourceExtractor sourceExtractor,
        ILogger<AcademicPdfDownloader> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _sourceExtractor = sourceExtractor;
        _logger = logger;
    }

    public async Task DownloadAvailableAsync(
        int researcherId,
        List<string> messages)
    {
        PdfDownloadOptions? options = null;
        List<AcademicWork>? works = null;
        List<List<Uri>>? workCandidates = null;
        List<Uri>? candidates = null;
        AcademicWork? work = null;
        AcademicWorkFile? workFile = null;
        string? storageRoot = null;
        string? relativePath = null;
        string? absolutePath = null;
        string? errorMessage = null;
        DownloadedPdf? downloadedPdf = null;
        int workIndex = 0;
        int candidateIndex = 0;
        int candidateWorkCount = 0;
        int candidateWorkIndex = 0;
        int openAlexCandidateCount = 0;
        int googleScholarCandidateCount = 0;
        int downloadedCount = 0;
        int existingCount = 0;
        int failedCount = 0;

        options = PdfDownloadOptions.Create(_configuration);

        if (!options.Enabled)
        {
            messages.Add("[ATLANDI] PDF dosyaları: indirme ayarı kapalı.");
            messages.Add(string.Empty);
            return;
        }

        storageRoot = GetStorageRoot(options);
        works = await _dbContext.AcademicWorks
            .Include(item => item.PdfFile)
            .Where(item => item.ResearcherId == researcherId)
            .ToListAsync();
        workCandidates = [];

        for (workIndex = 0; workIndex < works.Count; workIndex++)
        {
            candidates = _sourceExtractor.GetCandidates(works[workIndex]);
            workCandidates.Add(candidates);

            if (candidates.Count > 0)
            {
                candidateWorkCount++;

                if (works[workIndex].Provider == AcademicWorkProvider.OpenAlex)
                {
                    openAlexCandidateCount++;
                }

                if (works[workIndex].Provider == AcademicWorkProvider.GoogleScholar)
                {
                    googleScholarCandidateCount++;
                }
            }
        }

        _logger.LogInformation(
            "PDF taraması başladı. Akademisyen: {ResearcherId}, " +
            "çalışma: {WorkCount}, PDF adayı olan çalışma: {CandidateWorkCount}",
            researcherId,
            works.Count,
            candidateWorkCount);

        for (workIndex = 0; workIndex < works.Count; workIndex++)
        {
            work = works[workIndex];
            candidates = workCandidates[workIndex];

            if (candidates.Count == 0)
            {
                continue;
            }

            candidateWorkIndex++;
            _logger.LogInformation(
                "PDF [{Current}/{Total}] kontrol ediliyor: {Title}",
                candidateWorkIndex,
                candidateWorkCount,
                work.Title ?? work.ProviderWorkId ?? "Başlıksız çalışma");
            relativePath = GetRelativePath(researcherId, work.Id);
            absolutePath = GetAbsolutePath(storageRoot, relativePath);
            workFile = work.PdfFile;

            if (workFile?.Status == AcademicWorkFileStatus.Downloaded &&
                File.Exists(absolutePath))
            {
                existingCount++;
                _logger.LogInformation(
                    "PDF [{Current}/{Total}] zaten var: {RelativePath}",
                    candidateWorkIndex,
                    candidateWorkCount,
                    relativePath);
                continue;
            }

            workFile ??= CreateWorkFile(work);
            workFile.Status = AcademicWorkFileStatus.Pending;
            workFile.ErrorMessage = null;
            errorMessage = null;
            downloadedPdf = null;
            await _dbContext.SaveChangesAsync();

            for (candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                workFile.SourceUrl = candidates[candidateIndex].AbsoluteUri;
                workFile.LastAttemptedAt = DateTime.UtcNow;

                try
                {
                    downloadedPdf = await DownloadAsync(
                        candidates[candidateIndex],
                        absolutePath,
                        options);
                    break;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or
                    IOException or
                    InvalidDataException or
                    OperationCanceledException)
                {
                    errorMessage = AddError(
                        errorMessage,
                        candidates[candidateIndex],
                        exception.Message);
                }
            }

            if (downloadedPdf is null)
            {
                workFile.Status = AcademicWorkFileStatus.Failed;
                workFile.ErrorMessage = LimitText(errorMessage, 2000);
                failedCount++;
                await _dbContext.SaveChangesAsync();
                _logger.LogWarning(
                    "PDF [{Current}/{Total}] indirilemedi: {Title}. {Error}",
                    candidateWorkIndex,
                    candidateWorkCount,
                    work.Title ?? work.ProviderWorkId ?? "Başlıksız çalışma",
                    workFile.ErrorMessage);
                continue;
            }

            workFile.RelativePath = relativePath;
            workFile.FileName = Path.GetFileName(absolutePath);
            workFile.MimeType = downloadedPdf.MimeType;
            workFile.FileSizeBytes = downloadedPdf.FileSizeBytes;
            workFile.Sha256 = downloadedPdf.Sha256;
            workFile.DownloadedAt = DateTime.UtcNow;
            workFile.Status = AcademicWorkFileStatus.Downloaded;
            workFile.ErrorMessage = null;
            downloadedCount++;
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation(
                "PDF [{Current}/{Total}] indirildi: {RelativePath} ({FileSizeBytes} bayt)",
                candidateWorkIndex,
                candidateWorkCount,
                relativePath,
                downloadedPdf.FileSizeBytes);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "PDF taraması tamamlandı. İndirilen: {Downloaded}, zaten var: {Existing}, " +
            "başarısız: {Failed}",
            downloadedCount,
            existingCount,
            failedCount);

        messages.Add(
            $"[BİLGİ] PDF taraması: {works.Count} sağlayıcı çalışma kaydı incelendi.");
        messages.Add(
            candidateWorkCount > 0
                ? $"[BİLGİ] PDF kaynağı: {candidateWorkCount} kayıtta bulundu " +
                  $"(OpenAlex {openAlexCandidateCount}, Google Scholar " +
                  $"{googleScholarCandidateCount}); " +
                  $"{works.Count - candidateWorkCount} kayıtta bulunamadı."
                : $"[EKSİK] PDF kaynağı: {works.Count} kaydın hiçbirinde bulunamadı.");

        if (downloadedCount > 0 || existingCount > 0)
        {
            messages.Add(
                $"[OK] PDF dosyaları: {downloadedCount} indirildi, " +
                $"{existingCount} zaten vardı.");
        }

        if (failedCount > 0)
        {
            messages.Add(
                $"[EKSİK] PDF dosyaları: {failedCount} indirilemedi; " +
                "nedenler AcademicWorkFiles.ErrorMessage alanında.");
        }

        messages.Add(string.Empty);
    }

    private AcademicWorkFile CreateWorkFile(AcademicWork work)
    {
        AcademicWorkFile? workFile = null;

        workFile = new AcademicWorkFile();
        workFile.AcademicWorkId = work.Id;
        workFile.AcademicWork = work;
        workFile.Status = AcademicWorkFileStatus.Pending;
        work.PdfFile = workFile;
        _dbContext.AcademicWorkFiles.Add(workFile);

        return workFile;
    }

    private async Task<DownloadedPdf> DownloadAsync(
        Uri sourceUri,
        string absolutePath,
        PdfDownloadOptions options)
    {
        HttpClient? httpClient = null;
        HttpResponseMessage? response = null;
        Uri? currentUri = null;
        Uri? redirectUri = null;
        string? temporaryPath = null;
        string? mimeType = null;
        long maximumFileSizeBytes = 0;
        long fileSizeBytes = 0;
        string? sha256 = null;
        int redirectCount = 0;

        httpClient = _httpClientFactory.CreateClient(PdfDownloadOptions.HttpClientName);
        currentUri = sourceUri;
        maximumFileSizeBytes = (long)options.MaxFileSizeMb * 1024 * 1024;

        using (CancellationTokenSource cancellationTokenSource =
               new(TimeSpan.FromSeconds(options.RequestTimeoutSeconds)))
        {
            while (true)
            {
                await EnsurePublicHttpAddressAsync(currentUri);
                response = await httpClient.GetAsync(
                    currentUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationTokenSource.Token);

                if (!IsRedirect(response.StatusCode))
                {
                    break;
                }

                if (redirectCount >= options.MaxRedirects)
                {
                    response.Dispose();
                    throw new HttpRequestException("En fazla yönlendirme sayısı aşıldı.");
                }

                redirectUri = GetRedirectUri(currentUri, response.Headers.Location);
                response.Dispose();
                response = null;
                currentUri = redirectUri;
                redirectCount++;
            }

            using (response)
            {
                response.EnsureSuccessStatusCode();
                ValidateResponseHeaders(response.Content.Headers, maximumFileSizeBytes);
                mimeType = response.Content.Headers.ContentType?.MediaType;
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                temporaryPath = absolutePath + ".part-" + Guid.NewGuid().ToString("N");

                try
                {
                    (fileSizeBytes, sha256) = await WriteToTemporaryFileAsync(
                        response,
                        temporaryPath,
                        maximumFileSizeBytes,
                        cancellationTokenSource.Token);
                    await EnsurePdfSignatureAsync(temporaryPath, cancellationTokenSource.Token);
                    File.Move(temporaryPath, absolutePath, true);
                    temporaryPath = null;
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(temporaryPath) &&
                        File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
        }

        return new DownloadedPdf
        {
            FileSizeBytes = fileSizeBytes,
            MimeType = string.IsNullOrWhiteSpace(mimeType)
                ? "application/pdf"
                : mimeType,
            Sha256 = sha256
        };
    }

    private static async Task<(long FileSizeBytes, string Sha256)>
        WriteToTemporaryFileAsync(
            HttpResponseMessage response,
            string temporaryPath,
            long maximumFileSizeBytes,
            CancellationToken cancellationToken)
    {
        byte[]? buffer = null;
        Stream? sourceStream = null;
        FileStream? targetStream = null;
        IncrementalHash? hash = null;
        int bytesRead = 0;
        long totalBytesRead = 0;
        string? sha256 = null;

        buffer = new byte[BufferSize];

        await using (sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (targetStream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            while ((bytesRead = await sourceStream.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                totalBytesRead += bytesRead;

                if (totalBytesRead > maximumFileSizeBytes)
                {
                    throw new InvalidDataException("PDF izin verilen boyutu aşıyor.");
                }

                hash.AppendData(buffer, 0, bytesRead);
                await targetStream.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }

            sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        return (totalBytesRead, sha256);
    }

    private static async Task EnsurePdfSignatureAsync(
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        byte[]? signature = null;
        int bytesRead = 0;

        signature = new byte[PdfSignatureLength];

        await using (FileStream stream = new(
            temporaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            PdfSignatureLength,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            bytesRead = await stream.ReadAsync(signature, cancellationToken);
        }

        if (bytesRead != PdfSignatureLength ||
            signature[0] != (byte)'%' ||
            signature[1] != (byte)'P' ||
            signature[2] != (byte)'D' ||
            signature[3] != (byte)'F' ||
            signature[4] != (byte)'-')
        {
            throw new InvalidDataException("Bağlantı geçerli bir PDF dosyası döndürmedi.");
        }
    }

    private static void ValidateResponseHeaders(
        HttpContentHeaders headers,
        long maximumFileSizeBytes)
    {
        string? mimeType = null;

        if (headers.ContentLength > maximumFileSizeBytes)
        {
            throw new InvalidDataException("PDF izin verilen boyutu aşıyor.");
        }

        mimeType = headers.ContentType?.MediaType;

        if (string.Equals(mimeType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Bağlantı PDF yerine HTML sayfası döndürdü.");
        }
    }

    private static async Task EnsurePublicHttpAddressAsync(Uri uri)
    {
        IPAddress[]? addresses = null;
        int index = 0;

        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.IsLoopback)
        {
            throw new HttpRequestException("Yalnızca genel HTTP/HTTPS adresleri kullanılabilir.");
        }

        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost);
        }
        catch (Exception exception) when (
            exception is System.Net.Sockets.SocketException or ArgumentException)
        {
            throw new HttpRequestException("PDF sunucusunun adresi çözümlenemedi.", exception);
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException("PDF sunucusunun adresi çözümlenemedi.");
        }

        for (index = 0; index < addresses.Length; index++)
        {
            if (!IsPublicAddress(addresses[index]))
            {
                throw new HttpRequestException(
                    "Yerel veya özel ağ adreslerinden PDF indirilemez.");
            }
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        byte[]? bytes = null;

        if (IPAddress.IsLoopback(address) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast ||
            address.IsIPv6SiteLocal)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        bytes = address.GetAddressBytes();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return (bytes[0] & 0xFE) != 0xFC;
        }

        return bytes[0] != 0 &&
               bytes[0] != 10 &&
               bytes[0] != 127 &&
               !(bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) &&
               !(bytes[0] == 169 && bytes[1] == 254) &&
               !(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) &&
               !(bytes[0] == 192 && bytes[1] == 168) &&
               bytes[0] < 224;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.MovedPermanently ||
               statusCode == HttpStatusCode.Found ||
               statusCode == HttpStatusCode.SeeOther ||
               statusCode == HttpStatusCode.TemporaryRedirect ||
               (int)statusCode == 308;
    }

    private static Uri GetRedirectUri(Uri currentUri, Uri? location)
    {
        if (location is null)
        {
            throw new HttpRequestException("Yönlendirme adresi bulunamadı.");
        }

        return location.IsAbsoluteUri
            ? location
            : new Uri(currentUri, location);
    }

    private string GetStorageRoot(PdfDownloadOptions options)
    {
        string? configuredRoot = null;

        configuredRoot = options.StorageRoot!;

        return Path.GetFullPath(
            Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(_hostEnvironment.ContentRootPath, configuredRoot));
    }

    private static string GetRelativePath(int researcherId, int academicWorkId)
    {
        return Path.Combine(
                "Pdfs",
                researcherId.ToString(CultureInfo.InvariantCulture),
                academicWorkId.ToString(CultureInfo.InvariantCulture) + ".pdf")
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string GetAbsolutePath(
        string storageRoot,
        string relativePath)
    {
        string? absolutePath = null;
        string? storagePrefix = null;

        absolutePath = Path.GetFullPath(Path.Combine(
            storageRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        storagePrefix = storageRoot.TrimEnd(Path.DirectorySeparatorChar) +
                        Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(storagePrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("PDF depolama yolu geçersiz.");
        }

        return absolutePath;
    }

    private static string AddError(
        string? existingError,
        Uri sourceUri,
        string newError)
    {
        string? sourceError = null;

        sourceError = $"{sourceUri.Host}: {newError}";

        return string.IsNullOrWhiteSpace(existingError)
            ? sourceError
            : existingError + " | " + sourceError;
    }

    private static string? LimitText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength];
    }

    private sealed class DownloadedPdf
    {
        public long FileSizeBytes { get; set; }
        public string? MimeType { get; set; } = null;
        public string? Sha256 { get; set; } = null;
    }
}
