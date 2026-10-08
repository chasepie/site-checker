namespace SiteChecker.Scraper.Scripts;

/// <summary>
/// Compiled scripts, one per Site, keyed by source hash. Replacing or evicting a Site's script
/// unloads its old load context, so edits don't leak assemblies. After a restart, each script
/// compiles again the first time its Site is checked. Test Runs don't use the cache.
/// </summary>
public sealed class ScriptCache(IScriptCompiler compiler) : IDisposable
{
    private readonly IScriptCompiler _compiler = compiler;
    private readonly Dictionary<int, (string SourceHash, CompiledScript Script)> _scripts = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Returns the Site's compiled script, compiling it unless the cache already holds one for
    /// <paramref name="sourceHash"/>. A cached script with another hash is unloaded first. A failed
    /// compile isn't cached. The cache owns the returned script; don't dispose it.
    /// </summary>
    public ScriptCompileResult GetOrCompile(int siteId, string sourceHash, string source, string fileName)
    {
        lock (_lock)
        {
            if (_scripts.TryGetValue(siteId, out var cached))
            {
                if (string.Equals(cached.SourceHash, sourceHash, StringComparison.Ordinal))
                {
                    return ScriptCompileResult.Success(cached.Script);
                }

                _scripts.Remove(siteId);
                cached.Script.Dispose();
            }

            var result = _compiler.Compile(source, fileName);
            if (result.Succeeded)
            {
                _scripts[siteId] = (sourceHash, result.Script);
            }
            return result;
        }
    }

    /// <summary>
    /// Unloads the Site's compiled script, if any. Call after the Site's script is replaced or the
    /// Site is deleted. A run already using the script finishes first; unloading completes after.
    /// </summary>
    public void Evict(int siteId)
    {
        lock (_lock)
        {
            if (_scripts.Remove(siteId, out var cached))
            {
                cached.Script.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, script) in _scripts.Values)
            {
                script.Dispose();
            }
            _scripts.Clear();
        }
    }
}
