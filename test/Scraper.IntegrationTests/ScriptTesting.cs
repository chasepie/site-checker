namespace SiteChecker.Scraper.IntegrationTests;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using SiteChecker.Scraper.Scripts;
using SiteChecker.Scripting;

internal static class ScriptTesting
{
    /// <summary>
    /// Shared so the framework references are built once for the whole test run.
    /// </summary>
    public static ScriptCompiler Compiler { get; } = new();

    public static ScriptContext Context(IPage page, NavigationResult? navigation = null) => new()
    {
        Page = page,
        Navigation = navigation ?? NavigationResult.FromResponse(null),
        CancellationToken = CancellationToken.None,
        Site = new ScriptSite("Test Site", new Uri("https://example.com/"), UsesVpn: false),
        Logger = NullLogger.Instance,
    };

    public static CompiledScript CompileOrFail(string source, string fileName)
    {
        var result = Compiler.Compile(source, fileName);
        if (!result.Succeeded)
        {
            Assert.Fail(string.Join(Environment.NewLine, result.Errors.Select(e => $"{e.FileName}({e.Line},{e.Column}): {e.Id} {e.Message}")));
        }
        return result.Script!;
    }

    /// <summary>
    /// A weak reference to the script's load context. Taken in its own frame so the JIT keeps no
    /// hidden reference to the context alive in the caller.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference LoadContextOf(CompiledScript script) => new(script.LoadContext);

    /// <summary>
    /// Collects until the load context is gone; unloading completes over a few collections.
    /// </summary>
    public static bool Unloaded(WeakReference loadContext)
    {
        for (var i = 0; loadContext.IsAlive && i < 20; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !loadContext.IsAlive;
    }
}

/// <summary>
/// Counts compiles, so a test can tell a cache hit from a recompile.
/// </summary>
internal sealed class CountingCompiler : IScriptCompiler
{
    private int _compiles;

    public int Compiles => _compiles;

    public ScriptCompileResult Compile(string source, string fileName)
    {
        Interlocked.Increment(ref _compiles);
        return ScriptTesting.Compiler.Compile(source, fileName);
    }

    public IReadOnlyList<ScriptDiagnostic> Validate(string source, string fileName)
        => ScriptTesting.Compiler.Validate(source, fileName);
}
