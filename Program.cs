using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using System.Text;

public static class Program
{
    private const string TestOrcid = "0000-0003-2812-9917";
    private const string TestGoogleScholarId = "dYpPMQEAAAAJ";
    private const string ClearDatabaseArgument = "--clear-db";
    private const string DatabaseInfoArgument = "--db-info";
    private const int WorkCount = 10;

    public static async Task Main(string[] args)
    {
        Researcher? researcher = null;
        HttpClient? httpClient = null;
        IConfigurationRoot? configuration = null;

        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            configuration = ApplicationConfiguration.Create();

            if (await HandleDatabaseCommandAsync(args, configuration))
            {
                return;
            }

            researcher = CreateResearcher(args);
            httpClient = CreateHttpClient();

            await CollectOpenAlexAsync(researcher, httpClient);
            await CollectGoogleScholarAsync(researcher, httpClient, configuration);
            await SaveResearcherAsync(researcher, configuration);

            PrintAuthor(researcher);
            PrintWorks(researcher);
            PrintGoogleScholarWorks(researcher);
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"Geçersiz argüman: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Beklenmeyen hata: {exception.Message}");
        }
        finally
        {
            httpClient?.Dispose();
        }
    }

    private static async Task<bool> HandleDatabaseCommandAsync(
        string[] args,
        IConfiguration configuration)
    {
        DatabaseMaintenance? databaseMaintenance = null;

        if (args.Length != 1)
        {
            return false;
        }

        databaseMaintenance = new DatabaseMaintenance(configuration);

        if (args[0] == ClearDatabaseArgument)
        {
            await databaseMaintenance.ClearSqliteAsync();
            return true;
        }

        if (args[0] == DatabaseInfoArgument)
        {
            await databaseMaintenance.PrintSummaryAsync();
            return true;
        }

        return false;
    }

    private static Researcher CreateResearcher(string[] args)
    {
        Researcher? researcher = null;
        int index = 0;
        string? argument = null;

        researcher = new Researcher();

        if (args.Length == 0)
        {
            researcher.Orcid = TestOrcid;
            researcher.GoogleScholarId = TestGoogleScholarId;

            return researcher;
        }

        for (index = 0; index < args.Length; index++)
        {
            argument = args[index];

            if (argument == "--orcid" && index + 1 < args.Length)
            {
                researcher.Orcid = args[index + 1];
                index++;
                continue;
            }

            if (argument == "--scholar" && index + 1 < args.Length)
            {
                researcher.GoogleScholarId = args[index + 1];
                index++;
                continue;
            }

            throw new ArgumentException($"Bilinmeyen veya eksik argüman: {argument}");
        }

        return researcher;
    }

    private static async Task CollectOpenAlexAsync(Researcher researcher, HttpClient httpClient)
    {
        OpenAlexClient? openAlexClient = null;

        if (string.IsNullOrWhiteSpace(researcher.Orcid))
        {
            Console.WriteLine("ORCID bulunmadığı için OpenAlex sorgusu yapılmadı.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"ORCID sorgulanıyor: {researcher.Orcid}");
        Console.WriteLine();

        try
        {
            openAlexClient = new OpenAlexClient(httpClient);
            await openAlexClient.FillResearcherAsync(researcher, WorkCount);
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"Geçersiz ORCID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"OpenAlex'e bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"OpenAlex hatası: {exception.Message}");
        }
    }

    private static async Task CollectGoogleScholarAsync(
        Researcher researcher,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        GoogleScholarClient? googleScholarClient = null;
        string? serpApiKey = null;

        if (string.IsNullOrWhiteSpace(researcher.GoogleScholarId))
        {
            Console.WriteLine("Google Scholar ID bulunmadığı için sorgu yapılmadı.");
            Console.WriteLine();
            return;
        }

        serpApiKey = configuration["SerpApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(serpApiKey))
        {
            Console.WriteLine("SerpAPI anahtarı bulunmadığı için Google Scholar sorgusu yapılmadı.");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"Google Scholar ID sorgulanıyor: {researcher.GoogleScholarId}");
        Console.WriteLine();

        try
        {
            googleScholarClient = new GoogleScholarClient(httpClient, configuration);
            researcher.GoogleScholar = await googleScholarClient.GetAuthorAsync(
                researcher.GoogleScholarId,
                WorkCount);
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"Geçersiz Google Scholar ID: {exception.Message}");
        }
        catch (HttpRequestException exception)
        {
            Console.WriteLine($"Google Scholar'a bağlanılamadı: {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Google Scholar hatası: {exception.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient? httpClient = null;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");

        return httpClient;
    }

    private static async Task SaveResearcherAsync(
        Researcher researcher,
        IConfiguration configuration)
    {
        string? provider = null;
        DbContextOptionsBuilder<AcademicDbContext>? optionsBuilder = null;
        AcademicDbContext? dbContext = null;
        ResearcherRepository? researcherRepository = null;

        try
        {
            optionsBuilder = new DbContextOptionsBuilder<AcademicDbContext>();
            provider = DatabaseConfiguration.Configure(optionsBuilder, configuration);

            dbContext = new AcademicDbContext(optionsBuilder.Options);
            await dbContext.Database.EnsureCreatedAsync();

            researcherRepository = new ResearcherRepository(dbContext);

            await researcherRepository.SaveAsync(researcher);

            Console.WriteLine($"Akademisyen {provider} veritabanına kaydedildi. Kayıt ID: {researcher.Id}");
            Console.WriteLine();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Veritabanı kayıt hatası: {exception.Message}");
            Console.WriteLine();
        }
        finally
        {
            if (dbContext is not null)
            {
                await dbContext.DisposeAsync();
            }
        }
    }

    private static void PrintAuthor(Researcher researcher)
    {
        if (researcher.OpenAlex is null)
        {
            return;
        }

        Console.WriteLine($"Akademisyen : {researcher.OpenAlex?.DisplayName}");
        Console.WriteLine($"ORCID       : {researcher.Orcid}");
        Console.WriteLine($"OpenAlex ID : {researcher.OpenAlex?.AuthorId}");
        Console.WriteLine($"Yayın sayısı: {researcher.OpenAlex?.WorksCount}");
        Console.WriteLine();
    }

    private static void PrintWorks(Researcher researcher)
    {
        int index = 0;
        OpenAlexWork? work = null;
        string? doi = null;

        if (researcher.OpenAlex?.Works is null)
        {
            return;
        }

        Console.WriteLine("Son yayınlar:");

        for (index = 0; index < researcher.OpenAlex.Works.Count; index++)
        {
            work = researcher.OpenAlex.Works[index];
            doi = work.Doi ?? "DOI bulunamadı";

            Console.WriteLine($"{index + 1}. {work.Title}");
            Console.WriteLine($"   Yıl: {work.PublicationYear} | Tür: {work.Type} | Atıf: {work.CitedByCount}");
            Console.WriteLine($"   {doi}");
        }
    }

    private static void PrintGoogleScholarWorks(Researcher researcher)
    {
        int index = 0;
        GoogleScholarWork? work = null;
        string? publicationSummary = null;

        Console.WriteLine();
        Console.WriteLine($"Google Scholar ID: {researcher.GoogleScholarId}");
        Console.WriteLine($"Google Scholar akademisyen: {researcher.GoogleScholar?.Name}");
        Console.WriteLine($"Kurum: {researcher.GoogleScholar?.Affiliations}");
        Console.WriteLine("Google Scholar sonuçları:");

        if (researcher.GoogleScholar?.Works is null)
        {
            Console.WriteLine("Google Scholar verisi bulunamadı.");
            return;
        }

        for (index = 0; index < researcher.GoogleScholar.Works.Count; index++)
        {
            work = researcher.GoogleScholar.Works[index];
            publicationSummary = work.Publication ?? "Yayın bilgisi bulunamadı";

            Console.WriteLine($"{index + 1}. {work.Title}");
            Console.WriteLine($"   Yazarlar: {work.Authors}");
            Console.WriteLine($"   {publicationSummary}");
            Console.WriteLine($"   Yıl: {work.Year} | Atıf: {work.CitedByCount}");
            Console.WriteLine($"   {work.Link}");
        }
    }
}
