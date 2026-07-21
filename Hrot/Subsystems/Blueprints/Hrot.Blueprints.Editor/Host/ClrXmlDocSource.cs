using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Editor punch-list #4 — reads the companion <c>.xml</c> documentation file that MSBuild
/// emits next to a compiled assembly (when <c>GenerateDocumentationFile</c> is set) and
/// resolves the <c>&lt;summary&gt;</c> prose for a reflected CLR member.
///
/// <para>
/// The file is located from <see cref="Assembly.Location"/> (<c>*.dll</c> → <c>*.xml</c>),
/// parsed once, and cached per-assembly.  This is a <b>disk artifact of the static build</b>
/// and is therefore completely independent of the editor's in-memory hot-reload path
/// (<c>InMemoryRoslynCompiler</c> compiles blueprint code with <c>DocumentationMode.None</c>,
/// emits no XML stream, and loads via <c>AssemblyLoadContext.LoadFromStream</c> — which yields
/// an assembly whose <see cref="Assembly.Location"/> is <b>empty</b>).  When the declaring
/// assembly has no on-disk location (any dynamic / in-memory / collectible-ALC assembly), this
/// class returns <c>null</c> and the caller degrades gracefully to reflection type/signature.
/// </para>
///
/// <para>
/// Member lookup uses the standard XML doc-comment id (<c>M:</c>/<c>T:</c>/<c>P:</c>/<c>F:</c>).
/// For methods, an exact-id lookup is attempted first, then a parameter-agnostic prefix match
/// (<c>M:Type.Method(</c> or <c>M:Type.Method</c>) so non-overloaded curated helpers resolve
/// without reproducing the finicky parameter-type encoding.
/// </para>
/// </summary>
internal static class ClrXmlDocSource
{
    // Assembly on-disk location → (member doc-id → collapsed summary text). Null = load attempted and failed/absent.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the <c>&lt;summary&gt;</c> text for <paramref name="method"/>, or <c>null</c> when no
    /// companion doc file exists, the declaring assembly is dynamic (empty <see cref="Assembly.Location"/>),
    /// or no matching member is documented.
    /// </summary>
    public static string? GetSummary(MethodInfo method)
    {
        var declaring = method.DeclaringType;
        if (declaring == null) return null;

        var docs = LoadFor(declaring.Assembly);
        if (docs == null) return null;

        var typeName = DocTypeName(declaring);
        var exactId  = "M:" + typeName + "." + method.Name + BuildParamSuffix(method);
        if (docs.TryGetValue(exactId, out var exact))
            return exact;

        // Parameter-agnostic fallback: first member whose id is M:Type.Method( or exactly M:Type.Method.
        var withParen = "M:" + typeName + "." + method.Name + "(";
        var noArg     = "M:" + typeName + "." + method.Name;
        foreach (var (id, summary) in docs)
        {
            if (id.StartsWith(withParen, StringComparison.Ordinal) ||
                string.Equals(id, noArg, StringComparison.Ordinal))
                return summary;
        }
        return null;
    }

    /// <summary>Returns the <c>&lt;summary&gt;</c> text for a type, or <c>null</c> (same rules as <see cref="GetSummary(MethodInfo)"/>).</summary>
    public static string? GetSummary(Type type)
    {
        var docs = LoadFor(type.Assembly);
        if (docs == null) return null;
        return docs.TryGetValue("T:" + DocTypeName(type), out var s) ? s : null;
    }

    // ── loading / parsing ──────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string>? LoadFor(Assembly asm)
    {
        // Dynamic / in-memory / collectible-ALC assemblies have an empty Location — no doc file to read.
        string location;
        try { location = asm.Location; }
        catch { return null; }
        if (string.IsNullOrEmpty(location)) return null;

        return _cache.GetOrAdd(location, static loc =>
        {
            try
            {
                var xmlPath = Path.ChangeExtension(loc, ".xml");
                if (!File.Exists(xmlPath)) return null;

                var doc = XDocument.Load(xmlPath);
                var members = doc.Root?.Element("members")?.Elements("member");
                if (members == null) return null;

                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var member in members)
                {
                    var name = member.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    var summary = member.Element("summary");
                    if (summary == null) continue;
                    var text = CollapseWhitespace(summary.Value);
                    if (text.Length > 0)
                        map[name] = text;
                }
                return map;
            }
            catch
            {
                return null; // malformed / unreadable doc file → treat as absent.
            }
        });
    }

    // ── doc-id helpers ──────────────────────────────────────────────────────────

    /// <summary>Full type name in doc-comment form: namespace-qualified, nested types joined with '.'.</summary>
    private static string DocTypeName(Type type)
    {
        var full = type.FullName ?? type.Name;
        // Reflection uses '+' for nested types; doc ids use '.'. Strip any generic arity backticks.
        return full.Replace('+', '.');
    }

    /// <summary>
    /// Builds the exact parameter suffix for a method doc id, e.g. <c>(System.Int32,Fdp.Core.Entity)</c>.
    /// Only the common cases are encoded precisely; anything unusual simply won't match the exact id and
    /// the caller falls back to the parameter-agnostic prefix match.
    /// </summary>
    private static string BuildParamSuffix(MethodInfo method)
    {
        var ps = method.GetParameters();
        if (ps.Length == 0) return string.Empty;

        var sb = new StringBuilder("(");
        for (int i = 0; i < ps.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var pt = ps[i].ParameterType;
            var byRef = pt.IsByRef;
            if (byRef) pt = pt.GetElementType() ?? pt;
            sb.Append((pt.FullName ?? pt.Name).Replace('+', '.'));
            if (byRef) sb.Append('@');
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string CollapseWhitespace(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        bool prevSpace = false;
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace && sb.Length > 0) sb.Append(' ');
                prevSpace = true;
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
