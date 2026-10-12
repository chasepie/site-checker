namespace SiteChecker.Scraper.IntegrationTests;

using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using NSubstitute;
using static SiteChecker.Scraper.IntegrationTests.ScriptTesting;

[TestClass]
public sealed class ScriptCompilerTests
{
    // Uses only the fixed global usings: Linq, Regex and logging need no using directives.
    private const string HelloScript = """
        namespace Tests;

        public sealed class Hello : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
            {
                var words = new[] { "hello", "world" }.Where(w => Regex.IsMatch(w, "^h"));
                ctx.Logger.LogInformation("Running for {Site}", ctx.Site.Name);
                return Task.FromResult<ScriptOutcome>(string.Join(",", words) + " from " + ctx.Site.Name);
            }
        }
        """;

    [TestMethod]
    public async Task Compile_ValidScript_LoadsAScriptThatRuns()
    {
        using var script = CompileOrFail(HelloScript, "Hello.cs");

        var outcome = await script.CreateInstance().RunAsync(Context(Substitute.For<IPage>()));

        Assert.IsFalse(outcome.IsKnownFailure);
        Assert.AreEqual("hello from Test Site", outcome.Content);
    }

    [TestMethod]
    public void Compile_ReportsErrors_WithTheirFileAndLine()
    {
        const string source = """
            public sealed class Broken : IScript
            {
                public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                {
                    return Task.FromResult<ScriptOutcome>(undefinedVariable);
                }
            }
            """;

        var result = Compiler.Compile(source, "Broken.cs");

        Assert.IsFalse(result.Succeeded);
        var error = Assert.ContainsSingle(result.Errors);
        Assert.AreEqual("Broken.cs", error.FileName);
        Assert.AreEqual(5, error.Line);
        Assert.AreEqual("CS0103", error.Id);
        Assert.Contains("undefinedVariable", error.Message);
    }

    [TestMethod]
    public void Validate_ReturnsTheSameErrorsAsCompile_AndNoneForAValidScript()
    {
        const string source = "public sealed class Broken : IScript { }";

        var validated = Compiler.Validate(source, "Broken.cs");

        Assert.IsNotEmpty(validated);
        CollectionAssert.AreEqual(Compiler.Compile(source, "Broken.cs").Errors.ToList(), validated.ToList());
        Assert.IsEmpty(Compiler.Validate(HelloScript, "Hello.cs"));
    }

    [TestMethod]
    public void Compile_RejectsAFileWithoutAScriptClass()
    {
        const string source = """
            public sealed class NotAScript
            {
            }

            public abstract class AbstractScript : IScript
            {
                public abstract Task<ScriptOutcome> RunAsync(ScriptContext ctx);
            }
            """;

        var result = Compiler.Compile(source, "None.cs");

        var error = Assert.ContainsSingle(result.Errors);
        Assert.AreEqual("SC0001", error.Id);
    }

    [TestMethod]
    public void Compile_RejectsAFileWithTwoScriptClasses_AtEachClass()
    {
        const string source = """
            public sealed class First : IScript
            {
                public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("1");
            }

            public sealed class Second : IScript
            {
                public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("2");
            }
            """;

        var result = Compiler.Compile(source, "Two.cs");

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Errors.All(e => e.Id == "SC0002"));
        Assert.AreEqual("1,6", string.Join(",", result.Errors.Select(e => e.Line)));
    }

    [TestMethod]
    public void Compile_RejectsAScriptClassWithoutAPublicParameterlessConstructor()
    {
        const string source = """
            public sealed class NeedsArgs(string name) : IScript
            {
                public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>(name);
            }
            """;

        var result = Compiler.Compile(source, "NeedsArgs.cs");

        var error = Assert.ContainsSingle(result.Errors);
        Assert.AreEqual("SC0004", error.Id);
    }

    [TestMethod]
    public async Task ScriptExceptions_PointAtTheScriptsSourceLine()
    {
        const string source = """
            public sealed class Thrower : IScript
            {
                public Task<ScriptOutcome> RunAsync(ScriptContext ctx)
                {
                    throw new InvalidOperationException("boom");
                }
            }
            """;
        using var script = CompileOrFail(source, "Thrower.cs");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => script.CreateInstance().RunAsync(Context(Substitute.For<IPage>())));

        Assert.Contains("Thrower.cs:line 5", ex.StackTrace!);
    }

    [TestMethod]
    public async Task Dispose_UnloadsTheLoadContext_AfterTheScriptRan()
    {
        var loadContext = await RunThenDisposeAsync();

        Assert.IsTrue(Unloaded(loadContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> RunThenDisposeAsync()
    {
        var script = CompileOrFail(HelloScript, "Hello.cs");
        await script.CreateInstance().RunAsync(Context(Substitute.For<IPage>()));
        var loadContext = LoadContextOf(script);
        script.Dispose();
        return loadContext;
    }
}
