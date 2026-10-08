namespace SiteChecker.Backend.UnitTests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiteChecker.Backend.Models;
using SiteChecker.Backend.Services.Sites;
using SiteChecker.Database.Model;
using SiteChecker.Scraper;
using SiteChecker.Scraper.Scripts;

[TestClass]
public sealed class SiteValidatorTests
{
    private static readonly ScriptDiagnostic CompileError = new("Broken.cs", 3, 5, "CS0103", "The name 'x' does not exist in the current context");

    /// <summary>
    /// Reports <see cref="CompileError"/> for any source containing "broken".
    /// </summary>
    private sealed class StubCompiler : IScriptCompiler
    {
        public ScriptCompileResult Compile(string source, string fileName) => throw new NotSupportedException();

        public IReadOnlyList<ScriptDiagnostic> Validate(string source, string fileName)
            => source.Contains("broken", StringComparison.Ordinal) ? [CompileError] : [];
    }

    private static SiteValidator CreateValidator()
    {
        // BROWSERLESS_TIMEOUT of 60 s leaves 50 s for a scrape.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ScrapeTimeouts.ScrapeTimeoutKey] = "30",
                [ScrapeTimeouts.BrowserlessTimeoutKey] = "60000",
            })
            .Build();
        return new SiteValidator(new StubCompiler(), new ScrapeTimeouts(config, NullLogger<ScrapeTimeouts>.Instance));
    }

    private static SiteRequest Request(ScriptUpload? script = null, int? timeoutSeconds = null, ScraperKind kind = ScraperKind.Script) => new()
    {
        Name = "Site",
        Url = new Uri("https://example.com/"),
        TimeoutSeconds = timeoutSeconds,
        Scraper = new ScraperRequest { Kind = kind, Script = script },
    };

    private static ScriptUpload Script(string source = "public sealed class Ok : IScript { }", string fileName = "Ok.cs")
        => new() { FileName = fileName, Source = source };

    private static Site ExistingSiteWithScript() => new()
    {
        Name = "Site",
        Url = new Uri("https://example.com/"),
        SiteScript = new SiteScript { Source = "// current" },
    };

    [TestMethod]
    public void ValidSite_IsAccepted()
    {
        var result = CreateValidator().ValidateSite(Request(Script(), timeoutSeconds: 50), existing: null);

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void NewSite_WithoutAScript_IsRejected()
    {
        var result = CreateValidator().ValidateSite(Request(script: null), existing: null);

        Assert.AreEqual("A Script Scraper needs a script file.", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    public void Update_WithoutAScript_KeepsTheCurrentOne()
    {
        var result = CreateValidator().ValidateSite(Request(script: null), ExistingSiteWithScript());

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void UnsupportedScraperKind_IsRejected()
    {
        var result = CreateValidator().ValidateSite(Request(Script(), kind: (ScraperKind)99), existing: null);

        Assert.AreEqual("Unsupported Scraper kind '99'.", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(51)]
    public void Timeout_ThatDoesNotFitUnderBrowserless_IsRejected(int timeoutSeconds)
    {
        var result = CreateValidator().ValidateSite(Request(Script(), timeoutSeconds), existing: null);

        Assert.Contains("between 1 and 50 seconds", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    public void ScriptThatDoesNotCompile_IsRejected_WithItsDiagnostics()
    {
        var result = CreateValidator().ValidateSite(Request(Script("broken")), existing: null);

        Assert.AreEqual(CompileError, Assert.ContainsSingle(result.Diagnostics));
    }

    [TestMethod]
    public void ScriptThatIsNotACsFile_OrIsEmpty_IsRejected()
    {
        var validator = CreateValidator();

        Assert.AreEqual("The script must be a .cs file.", Assert.ContainsSingle(validator.ValidateSite(Request(Script(fileName: "Ok.txt")), null).Errors));
        Assert.AreEqual("The script file is empty.", Assert.ContainsSingle(validator.ValidateSite(Request(Script(source: " ")), null).Errors));
    }

    [TestMethod]
    public void TestRun_AlwaysNeedsAScript()
    {
        var request = new TestRunRequest
        {
            TestRunId = "run",
            ConnectionId = "connection",
            Url = new Uri("https://example.com/"),
            Scraper = new ScraperRequest { Script = null },
        };

        var result = CreateValidator().ValidateTestRun(request);

        Assert.AreEqual("A Script Scraper needs a script file.", Assert.ContainsSingle(result.Errors));
    }
}
