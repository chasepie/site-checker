using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using SiteChecker.Scripting;

namespace SiteChecker.Scraper.Scripts;

/// <summary>
/// A compile error in a script, located in the uploaded file.
/// </summary>
/// <param name="FileName">The file the error is in.</param>
/// <param name="Line">The 1-based line of the error.</param>
/// <param name="Column">The 1-based column of the error.</param>
/// <param name="Id">The diagnostic id, such as <c>CS0103</c>.</param>
/// <param name="Message">What is wrong.</param>
public sealed record ScriptDiagnostic(string FileName, int Line, int Column, string Id, string Message);

/// <summary>
/// The outcome of compiling a script: the loaded script, or the errors that stopped it.
/// </summary>
public sealed class ScriptCompileResult
{
    private ScriptCompileResult(CompiledScript? script, IReadOnlyList<ScriptDiagnostic> errors)
    {
        Script = script;
        Errors = errors;
    }

    /// <summary>
    /// The loaded script, when compilation succeeded.
    /// </summary>
    public CompiledScript? Script { get; }

    /// <summary>
    /// Why compilation failed. Empty when it succeeded.
    /// </summary>
    public IReadOnlyList<ScriptDiagnostic> Errors { get; }

    [MemberNotNullWhen(true, nameof(Script))]
    public bool Succeeded => Script is not null;

    internal static ScriptCompileResult Success(CompiledScript script) => new(script, []);

    internal static ScriptCompileResult Failure(IReadOnlyList<ScriptDiagnostic> errors) => new(null, errors);
}

/// <summary>
/// A compiled script, loaded into its own collectible <see cref="AssemblyLoadContext"/>.
/// Disposing it unloads the context, which completes once nothing references the script's types.
/// </summary>
public sealed class CompiledScript : IDisposable
{
    private AssemblyLoadContext? _loadContext;
    private Type? _scriptType;

    internal CompiledScript(AssemblyLoadContext loadContext, Type scriptType)
    {
        _loadContext = loadContext;
        _scriptType = scriptType;
    }

    /// <summary>
    /// The load context, for tests that check it unloads.
    /// </summary>
    internal AssemblyLoadContext? LoadContext => _loadContext;

    /// <summary>
    /// Creates a new instance of the script's <see cref="IScript"/> class, one per run.
    /// </summary>
    public IScript CreateInstance()
    {
        var scriptType = _scriptType;
        ObjectDisposedException.ThrowIf(scriptType is null, this);
        return (IScript)Activator.CreateInstance(scriptType)!;
    }

    public void Dispose()
    {
        _scriptType = null;
        Interlocked.Exchange(ref _loadContext, null)?.Unload();
    }
}
