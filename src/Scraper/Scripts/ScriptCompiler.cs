using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SiteChecker.Scripting;

namespace SiteChecker.Scraper.Scripts;

/// <summary>
/// Compiles Script Scrapers. The runtime compilation is the source of truth for what a valid
/// script is, so authoring projects mirror its settings.
/// </summary>
public interface IScriptCompiler
{
    /// <summary>
    /// Compiles a script and loads it into its own collectible load context. The caller owns the
    /// result's <see cref="ScriptCompileResult.Script"/> and disposes it to unload.
    /// </summary>
    ScriptCompileResult Compile(string source, string fileName);

    /// <summary>
    /// Compiles a script only to check it. Nothing is loaded.
    /// </summary>
    /// <returns>The errors that would stop the script compiling; empty when it's valid.</returns>
    IReadOnlyList<ScriptDiagnostic> Validate(string source, string fileName);
}

/// <summary>
/// Compiles a single C# file with <see cref="CSharpCompilation"/> against the runtime's own
/// framework assemblies, Playwright, logging and <c>SiteChecker.Scripting</c>, with a fixed set of
/// global usings and nullable enabled. A valid script holds exactly one non-abstract, non-generic
/// class that implements <see cref="IScript"/> and has a public parameterless constructor.
/// </summary>
public sealed class ScriptCompiler : IScriptCompiler
{
    /// <summary>
    /// The namespaces every script can use without a <c>using</c> directive. Authoring projects
    /// turn off <c>ImplicitUsings</c> and declare these as <c>&lt;Using&gt;</c> items.
    /// </summary>
    public static IReadOnlyList<string> GlobalUsings { get; } =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Text.RegularExpressions",
        "System.Threading",
        "System.Threading.Tasks",
        "Microsoft.Extensions.Logging",
        "Microsoft.Playwright",
        "SiteChecker.Scripting",
    ];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        optimizationLevel: OptimizationLevel.Release,
        nullableContextOptions: NullableContextOptions.Enable);

    private static readonly EmitOptions EmitOptions = new(debugInformationFormat: DebugInformationFormat.PortablePdb);

    // Needs an encoding, like every source, so debug information can be emitted.
    private static readonly SyntaxTree GlobalUsingsTree = CSharpSyntaxTree.ParseText(
        SourceText.From(string.Concat(GlobalUsings.Select(u => $"global using {u};\n")), Encoding.UTF8),
        ParseOptions,
        "GlobalUsings.g.cs");

    /// <summary>
    /// Built once and shared by every compile; there are a few hundred framework assemblies.
    /// </summary>
    private readonly ImmutableArray<MetadataReference> _references = BuildReferences();

    public ScriptCompileResult Compile(string source, string fileName)
    {
        var build = Build(source, fileName);
        if (build.Errors.Count > 0)
        {
            return ScriptCompileResult.Failure(build.Errors);
        }

        var loadContext = new AssemblyLoadContext($"SiteChecker.Script: {fileName}", isCollectible: true);
        using var pe = new MemoryStream(build.Pe);
        using var pdb = new MemoryStream(build.Pdb);
        var assembly = loadContext.LoadFromStream(pe, pdb);
        var scriptType = assembly.GetType(build.ScriptTypeName, throwOnError: true)!;
        return ScriptCompileResult.Success(new CompiledScript(loadContext, scriptType));
    }

    public IReadOnlyList<ScriptDiagnostic> Validate(string source, string fileName)
        => Build(source, fileName).Errors;

    private sealed record BuildOutput(byte[] Pe, byte[] Pdb, string ScriptTypeName, IReadOnlyList<ScriptDiagnostic> Errors)
    {
        public static BuildOutput Failed(IReadOnlyList<ScriptDiagnostic> errors) => new([], [], string.Empty, errors);
    }

    private BuildOutput Build(string source, string fileName)
    {
        var text = SourceText.From(source, Encoding.UTF8);
        var tree = CSharpSyntaxTree.ParseText(text, ParseOptions, fileName);
        var compilation = CSharpCompilation.Create(
            $"SiteChecker.Script.{Guid.NewGuid():N}",
            [GlobalUsingsTree, tree],
            _references,
            CompilationOptions);

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emitted = compilation.Emit(
            pe,
            pdb,
            options: EmitOptions,
            embeddedTexts: [EmbeddedText.FromSource(fileName, text)]);

        var errors = emitted.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => ToScriptDiagnostic(d, fileName))
            .ToList();
        if (errors.Count > 0)
        {
            return BuildOutput.Failed(errors);
        }

        var scriptTypes = FindScriptTypes(compilation);
        var typeErrors = CheckScriptTypes(scriptTypes, fileName);
        if (typeErrors.Count > 0)
        {
            return BuildOutput.Failed(typeErrors);
        }

        return new BuildOutput(pe.ToArray(), pdb.ToArray(), MetadataName(scriptTypes[0]), []);
    }

    private static List<INamedTypeSymbol> FindScriptTypes(CSharpCompilation compilation)
    {
        var scriptInterface = compilation.GetTypeByMetadataName(typeof(IScript).FullName!)
            ?? throw new InvalidOperationException($"{typeof(IScript).FullName} is missing from the script references.");

        var found = new List<INamedTypeSymbol>();
        Visit(compilation.Assembly.GlobalNamespace);
        return found;

        void Visit(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    Visit(ns);
                }
                else if (member is INamedTypeSymbol type)
                {
                    if (type.TypeKind == TypeKind.Class
                        && !type.IsAbstract
                        && type.AllInterfaces.Contains(scriptInterface, SymbolEqualityComparer.Default))
                    {
                        found.Add(type);
                    }
                    Visit(type);
                }
            }
        }
    }

    private static List<ScriptDiagnostic> CheckScriptTypes(List<INamedTypeSymbol> scriptTypes, string fileName)
    {
        if (scriptTypes.Count == 0)
        {
            return [new(fileName, 1, 1, "SC0001", $"The script must contain a non-abstract class that implements {nameof(IScript)}.")];
        }

        if (scriptTypes.Count > 1)
        {
            var names = string.Join(", ", scriptTypes.Select(t => t.Name));
            return scriptTypes
                .Select(t => At(t, fileName, "SC0002",
                    $"The script must contain exactly one class that implements {nameof(IScript)}, but it has {scriptTypes.Count}: {names}."))
                .ToList();
        }

        var scriptType = scriptTypes[0];
        var errors = new List<ScriptDiagnostic>();
        if (IsGeneric(scriptType))
        {
            errors.Add(At(scriptType, fileName, "SC0003", $"The {nameof(IScript)} class '{scriptType.Name}' must not be generic."));
        }

        var hasPublicParameterlessConstructor = scriptType.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        if (!hasPublicParameterlessConstructor)
        {
            errors.Add(At(scriptType, fileName, "SC0004", $"The {nameof(IScript)} class '{scriptType.Name}' must have a public parameterless constructor."));
        }

        return errors;
    }

    private static bool IsGeneric(INamedTypeSymbol type)
        => type.IsGenericType || (type.ContainingType is { } containing && IsGeneric(containing));

    private static ScriptDiagnostic At(INamedTypeSymbol type, string fileName, string id, string message)
    {
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is null)
        {
            return new(fileName, 1, 1, id, message);
        }

        var span = location.GetLineSpan();
        return new(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, id, message);
    }

    private static ScriptDiagnostic ToScriptDiagnostic(Diagnostic diagnostic, string fileName)
    {
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        if (!diagnostic.Location.IsInSource)
        {
            return new(fileName, 1, 1, diagnostic.Id, message);
        }

        var span = diagnostic.Location.GetMappedLineSpan();
        return new(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, diagnostic.Id, message);
    }

    /// <summary>
    /// The name reflection uses for the type: namespaces joined by '.', nested types by '+'.
    /// </summary>
    private static string MetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } containing)
        {
            return $"{MetadataName(containing)}+{type.MetadataName}";
        }

        return type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? $"{ns.ToDisplayString()}.{type.MetadataName}"
            : type.MetadataName;
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // The trusted platform assemblies in the framework's own directory are exactly the
        // managed framework assemblies; the directory also holds native libraries.
        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var frameworkAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?
            .Split(Path.PathSeparator)
            .Where(path => string.Equals(Path.GetDirectoryName(path), frameworkDirectory, StringComparison.Ordinal))
            ?? [];

        // Plus the script-facing libraries and what they depend on outside the framework. Playwright
        // targets netstandard2.0, so its public types reference packages such as
        // Microsoft.Bcl.AsyncInterfaces.
        var libraries = DependencyClosure(
            [typeof(ILogger).Assembly, typeof(IPage).Assembly, typeof(IScript).Assembly],
            frameworkDirectory);

        return
        [
            .. frameworkAssemblies.Concat(libraries)
                .Distinct(StringComparer.Ordinal)
                .Select(path => MetadataReference.CreateFromFile(path)),
        ];
    }

    private static List<string> DependencyClosure(IEnumerable<Assembly> roots, string? frameworkDirectory)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(roots);
        while (pending.TryDequeue(out var assembly))
        {
            if (string.IsNullOrEmpty(assembly.Location) || !seen.Add(assembly.Location))
            {
                continue;
            }

            if (string.Equals(Path.GetDirectoryName(assembly.Location), frameworkDirectory, StringComparison.Ordinal))
            {
                // Already referenced, along with everything it depends on.
                continue;
            }

            paths.Add(assembly.Location);
            foreach (var name in assembly.GetReferencedAssemblies())
            {
                try
                {
                    pending.Enqueue(Assembly.Load(name));
                }
                catch (FileNotFoundException)
                {
                    // An optional dependency the app doesn't ship; scripts can't use it either.
                }
            }
        }
        return paths;
    }
}

/// <summary>
/// Identifies a script's source, so a cached compile can be reused while the source is unchanged.
/// </summary>
public static class ScriptSource
{
    /// <summary>
    /// The lowercase hex SHA-256 of the source's UTF-8 bytes.
    /// </summary>
    public static string Hash(string source)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}
