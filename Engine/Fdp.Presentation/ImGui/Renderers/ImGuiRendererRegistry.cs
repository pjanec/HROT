using System.Reflection;

namespace Fdp.Toolkit.ImGui.Renderers;

/// <summary>
/// Auto-discovers and caches all <see cref="IImGuiRenderer"/> implementations annotated
/// with <see cref="ImGuiRendererAttribute"/> across all assemblies loaded into the
/// current <see cref="AppDomain"/>.
///
/// <para>Discovery is performed lazily on first use, with a lock, and cached thereafter.
/// New renderers in dynamically-loaded assemblies are NOT discovered after the first scan.
/// Call <see cref="Reset"/> (test helper) followed by re-use to re-trigger discovery.</para>
///
/// <para>Lookup order for <see cref="GetRenderer"/>:
/// context-specific match (targetType + contextType) → global match (targetType) → <c>null</c>.</para>
/// </summary>
public static class ImGuiRendererRegistry
{
    // (targetType, contextType-or-null) → renderer
    private static readonly Dictionary<(Type target, Type? context), IImGuiRenderer> _renderers = new();
    private static volatile bool _initialized;
    private static readonly object _lock = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the best-matching renderer for <paramref name="targetType"/> in an
    /// optional <paramref name="contextType"/> (the owning ECS component / outer object).
    /// Returns <c>null</c> when no renderer is registered.
    /// </summary>
    public static IImGuiRenderer? GetRenderer(Type targetType, Type? contextType = null)
    {
        EnsureInitialized();

        if (contextType != null &&
            _renderers.TryGetValue((targetType, contextType), out var ctx))
            return ctx;

        return _renderers.TryGetValue((targetType, null), out var global) ? global : null;
    }

    /// <summary>
    /// Manually registers a renderer. Useful for unit tests and hand-crafted registrations.
    /// Overwrites any previously registered renderer for the same (targetType, contextType) key.
    /// </summary>
    public static void Register(Type targetType, IImGuiRenderer renderer, Type? contextType = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(renderer);
        _renderers[(targetType, contextType)] = renderer;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>Forces a fresh assembly scan on next use. For testing only.</summary>
    internal static void Reset()
    {
        lock (_lock)
        {
            _renderers.Clear();
            _initialized = false;
        }
    }

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            ScanAllAssemblies();
            _initialized = true;
        }
    }

    // ── Discovery ─────────────────────────────────────────────────────────────

    private static void ScanAllAssemblies()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { ScanAssembly(assembly); }
            catch { /* skip non-reflectable assemblies (COM, dynamic, etc.) */ }
        }
    }

    private static void ScanAssembly(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(IImGuiRenderer).IsAssignableFrom(type) ||
                type.IsAbstract || type.IsInterface)
                continue;

            foreach (var attr in type.GetCustomAttributes<ImGuiRendererAttribute>(false))
            {
                try
                {
                    var instance = (IImGuiRenderer)Activator.CreateInstance(type)!;
                    _renderers[(attr.TargetType, attr.OnlyInsideType)] = instance;
                }
                catch { /* skip types without a public parameterless constructor */ }
            }
        }
    }
}
