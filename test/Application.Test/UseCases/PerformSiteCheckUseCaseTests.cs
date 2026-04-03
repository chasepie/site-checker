using NSubstitute;
using SiteChecker.Application.UseCases;
using SiteChecker.Domain;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Ports;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Application.Test.UseCases;

[TestClass]
public class PerformSiteCheckUseCaseTests
{
    public TestContext TestContext { get; set; } = null!;

    private static Site MakeSite(int id = 1, bool useVpn = false) => new()
    {
        Id = id,
        Name = "Test Site",
        Url = new Uri("https://example.com"),
        ScraperId = "test-scraper",
        UseVpn = useVpn,
    };

    private static PerformSiteCheckUseCase MakeUseCase(
        ISiteRepository? siteRepo = null,
        ISiteCheckRepository? siteCheckRepo = null,
        IVpnService? vpnService = null,
        IScrapingService? scrapingService = null) =>
        new(
            siteRepo ?? Substitute.For<ISiteRepository>(),
            siteCheckRepo ?? Substitute.For<ISiteCheckRepository>(),
            vpnService ?? Substitute.For<IVpnService>(),
            scrapingService ?? Substitute.For<IScrapingService>());

    [TestMethod]
    public async Task ExecuteAsync_SiteCheckNotFound_Throws()
    {
        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(null));

        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(99, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ExecuteAsync_SiteNotFound_Throws()
    {
        var siteCheck = new SiteCheck(1);

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(null));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task ExecuteAsync_NoVpn_SetsCheckingStatus_ThenCompletes()
    {
        var site = MakeSite(useVpn: false);
        var siteCheck = new SiteCheck(site.Id);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var scrapingService = Substitute.For<IScrapingService>();
        scrapingService.ScrapeAsync(Arg.Any<Site>(), Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" }));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, scrapingService: scrapingService);

        await useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken);

        Assert.AreEqual(CheckStatus.Done, siteCheck.Status);
        Assert.AreEqual("ok", siteCheck.Value);
    }

    [TestMethod]
    public async Task ExecuteAsync_NoVpn_SetsNoVpnLocationId()
    {
        var site = MakeSite(useVpn: false);
        var siteCheck = new SiteCheck(site.Id);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var vpnService = Substitute.For<IVpnService>();

        var scrapingService = Substitute.For<IScrapingService>();
        scrapingService.ScrapeAsync(Arg.Any<Site>(), Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" }));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, vpnService: vpnService, scrapingService: scrapingService);

        await useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken);

        Assert.AreEqual(VpnLocation.NoVpn.Id, siteCheck.VpnLocationId);
        await vpnService.DidNotReceive().GetCurrentLocationAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithVpn_CallsVpnService_And_SetsLocationId()
    {
        var site = MakeSite(useVpn: true);
        var siteCheck = new SiteCheck(site.Id);
        var vpnLocation = new VpnLocation("us-eastlantic", "US East");

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var vpnService = Substitute.For<IVpnService>();
        vpnService.GetCurrentLocationAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(vpnLocation));

        var scrapingService = Substitute.For<IScrapingService>();
        scrapingService.ScrapeAsync(Arg.Any<Site>(), Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" }));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, vpnService: vpnService, scrapingService: scrapingService);

        await useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken);

        Assert.AreEqual("us-eastlantic", siteCheck.VpnLocationId);
    }

    [TestMethod]
    public async Task ExecuteAsync_CallsUpdateTwice_InterimAndFinal()
    {
        var site = MakeSite();
        var siteCheck = new SiteCheck(site.Id);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var scrapingService = Substitute.For<IScrapingService>();
        scrapingService.ScrapeAsync(Arg.Any<Site>(), Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" }));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, scrapingService: scrapingService);

        await useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken);

        await siteCheckRepo.Received(2).UpdateAsync(siteCheck, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithScreenshot_SavesScreenshot()
    {
        var site = MakeSite();
        var siteCheck = new SiteCheck(site.Id);
        var screenshotData = new byte[] { 1, 2, 3 };
        var result = new SuccessScrapeResult { Content = "ok", Screenshot = screenshotData };

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var scrapingService = Substitute.For<IScrapingService>();
        scrapingService.ScrapeAsync(Arg.Any<Site>(), Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IScrapeResult>(result));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, scrapingService: scrapingService);

        await useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken);

        await siteCheckRepo.Received(1).AddScreenshotAsync(
            Arg.Is<SiteCheckScreenshot>(s => s.Data == screenshotData),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutScreenshot_DoesNotSaveScreenshot()
    {
        var site = MakeSite();
        var siteCheck = new SiteCheck(site.Id);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var scrapingService = Substitute.For<IScrapingService>();
        scrapingService.ScrapeAsync(Arg.Any<Site>(), Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IScrapeResult>(new SuccessScrapeResult { Content = "ok" }));

        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, scrapingService: scrapingService);

        await useCase.ExecuteAsync(siteCheck.Id, TestContext.CancellationToken);

        await siteCheckRepo.DidNotReceive().AddScreenshotAsync(
            Arg.Any<SiteCheckScreenshot>(), Arg.Any<CancellationToken>());
    }
}
