using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace AcademicCollectorDemo.Tests;

public sealed class HostProcess : IDisposable
{
    private readonly Process _process;
    public HttpClient Client { get; }

    public HostProcess(string connectionString)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AcademicCollectorDemo.csproj")))
            root = root.Parent;

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root?.FullName ?? throw new InvalidOperationException("Project root not found."),
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--urls=" + Client.BaseAddress);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ConnectionStrings__AcademicDatabase"] = connectionString;
        _process = Process.Start(start) ?? throw new InvalidOperationException("Host did not start.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task WaitUntilReadyAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Host exited with code {_process.ExitCode}.");
            try
            {
                using var response = await Client.GetAsync("/");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        throw new TimeoutException("Test host did not become ready.");
    }

    public void Dispose()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10000);
        }
        _process.Dispose();
    }
}
