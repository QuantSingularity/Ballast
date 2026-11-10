using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ballast.TestKit;

/// <summary>
/// Starts the Python model service (code/ai_models) once per test process and tears it down on exit.
/// The interpreter comes from BALLAST_AI_PYTHON, else the project's .venv, else python3 on the PATH.
/// </summary>
public static class AiServiceProcess
{
    private static readonly Lazy<string> LazyUrl = new(Start);

    public static string Url => LazyUrl.Value;

    private static string FindAiModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "code", "ai_models", "ballast_ai");
            if (Directory.Exists(candidate)) return Path.Combine(dir.FullName, "code", "ai_models");
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate code/ai_models from " + AppContext.BaseDirectory);
    }

    private static string FindPython(string aiModels)
    {
        var configured = Environment.GetEnvironmentVariable("BALLAST_AI_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var unix = Path.Combine(aiModels, ".venv", "bin", "python");
        if (File.Exists(unix)) return unix;
        var windows = Path.Combine(aiModels, ".venv", "Scripts", "python.exe");
        if (File.Exists(windows)) return windows;
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string Start()
    {
        var external = Environment.GetEnvironmentVariable("BALLAST_TEST_AI_URL");
        if (!string.IsNullOrWhiteSpace(external)) return external.TrimEnd('/');

        var aiModels = FindAiModelsDirectory();
        var python = FindPython(aiModels);
        int port = FreePort();
        var output = new StringBuilder();
        var psi = new ProcessStartInfo(python, $"-m ballast_ai.service --host 127.0.0.1 --port {port}")
        {
            WorkingDirectory = aiModels,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start the Python model service with '{python}'. Run scripts/setup_environment.sh or set BALLAST_AI_PYTHON. {ex.Message}");
        }
        process.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        };

        var url = $"http://127.0.0.1:{port}";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                lock (output)
                    throw new InvalidOperationException($"The Python model service exited with code {process.ExitCode}. Output:\n{output}");
            }
            try
            {
                if (http.GetAsync(url + "/health").GetAwaiter().GetResult().IsSuccessStatusCode) return url;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            Thread.Sleep(200);
        }
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        lock (output)
            throw new InvalidOperationException($"The Python model service did not become healthy within 90 seconds. Output:\n{output}");
    }
}
