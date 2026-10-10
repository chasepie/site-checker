namespace SiteChecker.Scraper.IntegrationTests;

using System.Runtime.CompilerServices;
using SiteChecker.Scraper.Scripts;
using static SiteChecker.Scraper.IntegrationTests.ScriptTesting;

[TestClass]
public sealed class ScriptCacheTests
{
    private const string Version1 = """
        public sealed class Versioned : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("v1");
        }
        """;

    private const string Version2 = """
        public sealed class Versioned : IScript
        {
            public Task<ScriptOutcome> RunAsync(ScriptContext ctx) => Task.FromResult<ScriptOutcome>("v2");
        }
        """;

    [TestMethod]
    public void GetOrCompile_ReusesTheCompiledScript_WhileTheSourceHashIsUnchanged()
    {
        var compiler = new CountingCompiler();
        using var cache = new ScriptCache(compiler);

        var first = cache.GetOrCompile(1, ScriptSource.Hash(Version1), Version1, "Versioned.cs");
        var second = cache.GetOrCompile(1, ScriptSource.Hash(Version1), Version1, "Versioned.cs");

        Assert.AreSame(first.Script, second.Script);
        Assert.AreEqual(1, compiler.Compiles);
    }

    [TestMethod]
    public void GetOrCompile_KeepsEachSitesScriptSeparately()
    {
        var compiler = new CountingCompiler();
        using var cache = new ScriptCache(compiler);

        var site1 = cache.GetOrCompile(1, ScriptSource.Hash(Version1), Version1, "Versioned.cs");
        var site2 = cache.GetOrCompile(2, ScriptSource.Hash(Version1), Version1, "Versioned.cs");

        Assert.AreNotSame(site1.Script, site2.Script);
        Assert.AreEqual(2, compiler.Compiles);
    }

    [TestMethod]
    public void GetOrCompile_WithANewSourceHash_UnloadsTheOldScript()
    {
        using var cache = new ScriptCache(new CountingCompiler());
        var oldLoadContext = CacheAndGetLoadContext(cache, Version1);

        var replaced = cache.GetOrCompile(1, ScriptSource.Hash(Version2), Version2, "Versioned.cs");

        Assert.IsTrue(replaced.Succeeded);
        Assert.IsTrue(Unloaded(oldLoadContext));
    }

    [TestMethod]
    public void Evict_UnloadsTheSitesScript()
    {
        using var cache = new ScriptCache(new CountingCompiler());
        var loadContext = CacheAndGetLoadContext(cache, Version1);

        cache.Evict(1);

        Assert.IsTrue(Unloaded(loadContext));
    }

    [TestMethod]
    public void GetOrCompile_DoesNotCacheAFailedCompile()
    {
        const string broken = "public sealed class Broken : IScript { }";
        var compiler = new CountingCompiler();
        using var cache = new ScriptCache(compiler);

        var first = cache.GetOrCompile(1, ScriptSource.Hash(broken), broken, "Broken.cs");
        var second = cache.GetOrCompile(1, ScriptSource.Hash(broken), broken, "Broken.cs");

        Assert.IsFalse(first.Succeeded);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(2, compiler.Compiles);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CacheAndGetLoadContext(ScriptCache cache, string source)
    {
        var result = cache.GetOrCompile(1, ScriptSource.Hash(source), source, "Versioned.cs");
        return LoadContextOf(result.Script!);
    }
}
