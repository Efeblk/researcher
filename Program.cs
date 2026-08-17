using System.Text;

public static class Program
{
    private const string TestOrcid = "0000-0002-4028-3522";
    private const int WorkCount = 10;

    public static async Task Main(string[] args)
    {
        string? orcid = null;
        OpenAlexAuthor? author = null;
        List<OpenAlexWork>? works = null;
        HttpClient? httpClient = null;
        OpenAlexClient? openAlexClient = null;

        Console.OutputEncoding = Encoding.UTF8;
        orcid = GetOrcid(args);

        Console.WriteLine($"ORCID sorgulanıyor: {orcid}");
        Console.WriteLine();

        try
        {
            httpClient = CreateHttpClient();
            openAlexClient = new OpenAlexClient(httpClient);

            author = await openAlexClient.GetAuthorAsync(orcid);
            works = await openAlexClient.GetLatestWorksAsync(author.Id, WorkCount);

            PrintAuthor(author, orcid);
            PrintWorks(works);
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
            Console.WriteLine($"Beklenmeyen hata: {exception.Message}");
        }
        finally
        {
            httpClient?.Dispose();
        }
    }

    private static string GetOrcid(string[] args)
    {
        if (args.Length > 0)
        {
            return args[0];
        }

        return TestOrcid;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient? httpClient = null;

        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AcademicCollectorDemo/0.1");

        return httpClient;
    }

    private static void PrintAuthor(OpenAlexAuthor author, string orcid)
    {
        Console.WriteLine($"Akademisyen : {author.DisplayName}");
        Console.WriteLine($"ORCID       : {orcid}");
        Console.WriteLine($"Yayın sayısı: {author.WorksCount}");
        Console.WriteLine();
    }

    private static void PrintWorks(List<OpenAlexWork> works)
    {
        int index = 0;
        OpenAlexWork? work = null;
        string? doi = null;

        Console.WriteLine("Son yayınlar:");

        for (index = 0; index < works.Count; index++)
        {
            work = works[index];
            doi = work.Doi ?? "DOI bulunamadı";

            Console.WriteLine($"{index + 1}. {work.Title}");
            Console.WriteLine($"   Yıl: {work.PublicationYear} | Tür: {work.Type} | Atıf: {work.CitedByCount}");
            Console.WriteLine($"   {doi}");
        }
    }
}
