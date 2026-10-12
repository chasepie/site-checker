namespace SiteChecker.Scraper.UnitTests;

using Microsoft.Playwright;
using NSubstitute;
using SiteChecker.Scripting;

[TestClass]
public sealed class ScriptContractTests
{
    [TestMethod]
    public void String_ConvertsToASucceededOutcome()
    {
        ScriptOutcome outcome = "content";

        Assert.IsFalse(outcome.IsKnownFailure);
        Assert.AreEqual("content", outcome.Content);
        Assert.IsEmpty(outcome.RequestedActions);
    }

    [TestMethod]
    public void NullString_Throws_SoItIsAnUnexpectedFailureNotEmptyContent()
    {
        string? content = null;

        Assert.ThrowsExactly<ArgumentNullException>(() => { ScriptOutcome _ = content!; });
    }

    [TestMethod]
    public void KnownFailure_KeepsItsMessage_AndEachRequestedActionOnce()
    {
        var outcome = ScriptOutcome.KnownFailure("Blocked", RequestedAction.Retry, RequestedAction.ChangeVpnLocation, RequestedAction.Retry);

        Assert.IsTrue(outcome.IsKnownFailure);
        Assert.AreEqual("Blocked", outcome.KnownFailureMessage);
        Assert.IsNull(outcome.Content);
        CollectionAssert.AreEqual(new[] { RequestedAction.Retry, RequestedAction.ChangeVpnLocation }, outcome.RequestedActions.ToArray());
    }

    [TestMethod]
    public void KnownFailure_RequiresAMessage()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ScriptOutcome.KnownFailure(" "));
    }

    [TestMethod]
    [DataRow(200)]
    [DataRow(204)]
    public void EnsureSucceeded_Passes_ForASuccessfulResponse(int status)
    {
        var navigation = NavigationResult.FromResponse(Response(status));

        Assert.IsTrue(navigation.Succeeded);
        navigation.EnsureSucceeded();
    }

    [TestMethod]
    public void EnsureSucceeded_Passes_WhenNavigationHadNoResponse()
    {
        var navigation = NavigationResult.FromResponse(null);

        Assert.IsTrue(navigation.Succeeded);
        navigation.EnsureSucceeded();
    }

    [TestMethod]
    public void EnsureSucceeded_Throws_ForAnErrorStatus()
    {
        var navigation = NavigationResult.FromResponse(Response(429, "Too Many Requests"));

        Assert.IsFalse(navigation.Succeeded);
        var ex = Assert.ThrowsExactly<NavigationFailedException>(navigation.EnsureSucceeded);
        Assert.AreEqual("Navigation to https://example.com/ returned HTTP 429 Too Many Requests", ex.Message);
    }

    [TestMethod]
    public void EnsureSucceeded_Throws_WithTheNavigationError()
    {
        var error = new InvalidOperationException("net::ERR_CONNECTION_RESET");
        var navigation = NavigationResult.FromError(error);

        Assert.IsFalse(navigation.Succeeded);
        var ex = Assert.ThrowsExactly<NavigationFailedException>(navigation.EnsureSucceeded);
        Assert.AreSame(error, ex.InnerException);
        Assert.AreEqual("Navigation failed: net::ERR_CONNECTION_RESET", ex.Message);
    }

    private static IResponse Response(int status, string statusText = "")
    {
        var response = Substitute.For<IResponse>();
        response.Status.Returns(status);
        response.Ok.Returns(status is >= 200 and < 300);
        response.Url.Returns("https://example.com/");
        response.StatusText.Returns(statusText);
        return response;
    }
}
