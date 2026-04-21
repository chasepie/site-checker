using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SiteChecker.Domain.Ports;

namespace SiteChecker.PlaywrightServer;

public class PlaywrightBrowserServer : IBrowserServer
{
    private readonly ILogger _logger;
    private readonly Process _process;
    private bool disposedValue;
    private bool _isRunning;

    public PlaywrightBrowserServer(ILogger<PlaywrightBrowserServer> logger)
    {
        _logger = logger;

        var serverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "typescript");
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "npm",
            Arguments = "start",
            WorkingDirectory = serverPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = processStartInfo };
        _process.OutputDataReceived += (sender, args) => _logger.LogInformation("Playwright Server: {Output}", args.Data);
        _process.ErrorDataReceived += (sender, args) => _logger.LogError("Playwright Server: {Error}", args.Data);
    }

    public void StartServer()
    {
        if (_isRunning)
        {
            _logger.LogWarning("Playwright server is already running.");
            return;
        }

        _isRunning = true;
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void StopServer()
    {
        if (!_isRunning)
        {
            _logger.LogWarning("Playwright server is not running.");
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Playwright server.");
        }
        finally
        {
            _isRunning = false;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // dispose managed state (managed objects)
                if (_isRunning)
                {
                    StopServer();
                }
                _process.Dispose();
            }

            // free unmanaged resources (unmanaged objects) and override finalizer
            // set large fields to null
            disposedValue = true;
        }
    }

    // override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~PlaywrightServer()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
