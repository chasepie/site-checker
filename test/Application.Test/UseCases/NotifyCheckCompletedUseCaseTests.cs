using NSubstitute;
using SiteChecker.Application.UseCases;
using SiteChecker.Domain;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Exceptions;
using SiteChecker.Domain.Ports;

namespace SiteChecker.Application.Test.UseCases;

[TestClass]
public class NotifyCheckCompletedUseCaseTests
{
    public TestContext TestContext { get; set; } = null!;

    private static Site MakeSite(int id = 1) => new()
    {
        Id = id,
        Name = "Test",
        Url = new Uri("https://example.com"),
        ScraperId = "sc",
    };

    private static NotifyCheckCompletedUseCase MakeUseCase(
        ISiteRepository? siteRepo = null,
        ISiteCheckRepository? siteCheckRepo = null,
        INotificationService? notificationService = null) =>
        new(
            siteRepo ?? Substitute.For<ISiteRepository>(),
            siteCheckRepo ?? Substitute.For<ISiteCheckRepository>(),
            notificationService ?? Substitute.For<INotificationService>());

    [TestMethod]
    public async Task ExecuteAsync_SiteCheckNotFound_DoesNotNotify()
    {
        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(null));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(99, false, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_WasAlreadyComplete_DoesNotNotify()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "ok" });

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: true, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_CheckNotComplete_DoesNotNotify()
    {
        var siteCheck = new SiteCheck(1); // Created status — IsComplete = false

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_DoneWithNoPreviousCheck_DoesNotNotify()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "new" });

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));
        siteCheckRepo.GetPreviousSuccessfulAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(null));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_DoneWithSameContent_DoesNotNotify()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "same content" });

        var previousCheck = new SiteCheck(1);
        previousCheck.CompleteWithResult(new SuccessScrapeResult { Content = "same content" });

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));
        siteCheckRepo.GetPreviousSuccessfulAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(previousCheck));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_DoneWithDifferentContent_SendsNotification()
    {
        var site = MakeSite();
        var siteCheck = new SiteCheck(site.Id);
        siteCheck.CompleteWithResult(new SuccessScrapeResult { Content = "new content" });

        var previousCheck = new SiteCheck(site.Id);
        previousCheck.CompleteWithResult(new SuccessScrapeResult { Content = "old content" });

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));
        siteCheckRepo.GetPreviousSuccessfulAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(previousCheck));
        siteCheckRepo.GetScreenshotAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheckScreenshot?>(null));

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.Received(1).NotifyAsync(
            siteCheck, site, Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_FailedWithKnownFailure_DoesNotNotify()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(FailureScrapeResult.FromException(new AccessDeniedScraperException()));

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_FailedWithUnknownException_SendsNotification()
    {
        var site = MakeSite();
        var siteCheck = new SiteCheck(site.Id);
        siteCheck.CompleteWithResult(new FailureScrapeResult { ErrorMessage = "unexpected error" });

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));
        siteCheckRepo.GetScreenshotAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheckScreenshot?>(null));

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(site));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.Received(1).NotifyAsync(
            siteCheck, site, Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExecuteAsync_SiteNotFound_DoesNotNotify()
    {
        var siteCheck = new SiteCheck(1);
        siteCheck.CompleteWithResult(new FailureScrapeResult { ErrorMessage = "failure" });

        var siteCheckRepo = Substitute.For<ISiteCheckRepository>();
        siteCheckRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheck?>(siteCheck));
        siteCheckRepo.GetScreenshotAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SiteCheckScreenshot?>(null));

        var siteRepo = Substitute.For<ISiteRepository>();
        siteRepo.GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Site?>(null));

        var notificationService = Substitute.For<INotificationService>();
        var useCase = MakeUseCase(siteRepo: siteRepo, siteCheckRepo: siteCheckRepo, notificationService: notificationService);

        await useCase.ExecuteAsync(siteCheck.Id, wasAlreadyComplete: false, TestContext.CancellationToken);

        await notificationService.DidNotReceive().NotifyAsync(
            Arg.Any<SiteCheck>(), Arg.Any<Site>(), Arg.Any<SiteCheckScreenshot?>(), Arg.Any<CancellationToken>());
    }
}
