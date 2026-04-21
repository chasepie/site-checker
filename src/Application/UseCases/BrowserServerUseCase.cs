using SiteChecker.Domain.Ports;

namespace SiteChecker.Application.UseCases;

public class BrowserServerUseCase(IBrowserServer browserServer)
{
    private readonly IBrowserServer _browserServer = browserServer;

    public void StartServer()
    {
        _browserServer.StartServer();
    }

    public void StopServer()
    {
        _browserServer.StopServer();
    }
}
