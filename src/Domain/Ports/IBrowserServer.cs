using SiteChecker.Domain.Events;

namespace SiteChecker.Domain.Ports;

public interface IBrowserServer : IDisposable
{
    void StartServer();
    void StopServer();
}
