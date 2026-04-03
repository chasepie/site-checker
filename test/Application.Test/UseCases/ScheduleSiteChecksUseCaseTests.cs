using NSubstitute;
using SiteChecker.Application.UseCases;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.Ports;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Application.Test.UseCases;

[TestClass]
public class ScheduleSiteChecksUseCaseTests
{
    public TestContext TestContext { get; set; } = null!;

    // noon on a fixed date — used as "now" so schedule window is predictable
    private static readonly DateTime Noon = new(2024, 6, 15, 12, 0, 0, DateTimeKind.Local);

    private static Site MakeSiteWithActiveSchedule(int id = 1) => new()
    {
        Id = id,
        Name = "Site",
        Url = new Uri("https://example.com"),
        ScraperId = "sc",
        Schedule = new SiteSchedule
        {
            Enabled = true,
            Start = new TimeOnly(0, 0),
            End = new TimeOnly(23, 59),
            Interval = 60,
        },
    };

    private static Site MakeSiteWithDisabledSchedule(int id = 1) => new()
    {
        Id = id,
        Name = "Disabled",
        Url = new Uri("https://example.com"),
        ScraperId = "sc",
        Schedule = new SiteSchedule { Enabled = false },
    };

    [TestMethod]
    public async Task ExecuteAsync_NoSites_DoesNotAddAnyChecks()
    {
        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.DidNotReceive().AddAsync(Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_SiteWithInProgressCheck_SkipsSite()
    {
        var site = MakeSiteWithActiveSchedule();
        var inProgressCheck = new SiteCheck(site.Id);
        inProgressCheck.Status = CheckStatus.Checking; // not complete

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([site]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetLatestForSiteAsync(site.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(inProgressCheck));

        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.DidNotReceive().AddAsync(Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_SiteWithScheduleNotDue_DoesNotAddCheck()
    {
        var site = MakeSiteWithDisabledSchedule();

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([site]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetLatestForSiteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(null));

        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.DidNotReceive().AddAsync(Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_NoPreviousCheck_AndScheduleDue_AddsCheck()
    {
        var site = MakeSiteWithActiveSchedule();

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([site]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetLatestForSiteAsync(site.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(null));

        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.Received(1).AddAsync(
            Arg.Is<SiteCheck>(sc => sc.SiteId == site.Id),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_LastCheckExceededInterval_AddsCheck()
    {
        var site = MakeSiteWithActiveSchedule();
        var lastCheck = new SiteCheck(site.Id);
        lastCheck.Status = CheckStatus.Done;
        lastCheck.StartDate = Noon.ToUniversalTime().AddMinutes(-90);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([site]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetLatestForSiteAsync(site.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(lastCheck));

        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.Received(1).AddAsync(
            Arg.Is<SiteCheck>(sc => sc.SiteId == site.Id),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_LastCheckWithinInterval_DoesNotAddCheck()
    {
        var site = MakeSiteWithActiveSchedule();
        var lastCheck = new SiteCheck(site.Id);
        lastCheck.Status = CheckStatus.Done;
        lastCheck.StartDate = Noon.ToUniversalTime().AddMinutes(-30);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([site]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetLatestForSiteAsync(site.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(lastCheck));

        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.DidNotReceive().AddAsync(Arg.Any<SiteCheck>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_MultipleSites_OnlyAddsChecksForDueSites()
    {
        var dueSite = MakeSiteWithActiveSchedule(id: 1);
        var disabledSite = MakeSiteWithDisabledSchedule(id: 2);

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Site>>([dueSite, disabledSite]));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetLatestForSiteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(null));

        var useCase = new ScheduleSiteChecksUseCase(siteRepo, siteCheckRepo);

        await useCase.ExecuteAsync(Noon, TestContext.CancellationToken);

        await siteCheckRepo.Received(1).AddAsync(
            Arg.Is<SiteCheck>(sc => sc.SiteId == dueSite.Id),
            Arg.Any<CancellationToken>());
        await siteCheckRepo.DidNotReceive().AddAsync(
            Arg.Is<SiteCheck>(sc => sc.SiteId == disabledSite.Id),
            Arg.Any<CancellationToken>());
    }
}
