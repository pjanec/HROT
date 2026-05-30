using System.Reflection;

namespace Hrot.Utility.Editor.Catalog;

/// <summary>
/// Discovers Utility AI input accessors (In.* methods) from loaded assemblies by reflection.
/// Populates the input-picker catalog in the Utility Decision editor.
/// </summary>
public static class InputCatalogBrowser
{
    /// <summary>
    /// Reflects over <paramref name="assemblies"/> and returns one InputCatalogEntry per
    /// unique In.* method found.  Results are sorted by Name (Ordinal).
    /// When multiple overloads of the same name exist, the one with the most non-InputContext
    /// parameters is preferred (richer overload wins).  Across assemblies the first assembly
    /// that defines a name wins on equal richness.
    /// </summary>
    public static IReadOnlyList<InputCatalogEntry> Discover(params Assembly[] assemblies)
    {
        if (assemblies == null || assemblies.Length == 0)
            return Array.Empty<InputCatalogEntry>();

        // name -> best method (most non-context params wins; first assembly breaks ties)
        var best = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        foreach (var asm in assemblies)
        {
            if (asm == null) continue;
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.Name != "In") continue;

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (method.ReturnType.Name != "InputRef") continue;

                        string name   = method.Name;
                        int    nonCtx = CountNonContextParams(method);

                        if (!best.TryGetValue(name, out var current) ||
                            nonCtx > CountNonContextParams(current))
                        {
                            best[name] = method;
                        }
                    }
                }
            }
            catch
            {
                // Skip assemblies that fail reflection
            }
        }

        return best
            .Select(kvp => new InputCatalogEntry(
                kvp.Key,
                GetCategory(kvp.Value),
                GetParamKind(kvp.Value)))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    // ---- Helpers ----

    private static int CountNonContextParams(MethodInfo method) =>
        method.GetParameters().Count(p => p.ParameterType.Name != "InputContext");

    private static string GetCategory(MethodInfo method)
    {
        foreach (var attr in method.GetCustomAttributesData())
        {
            if (attr.AttributeType.Name == "UtilityInputAttribute" &&
                attr.ConstructorArguments.Count > 0)
            {
                return attr.ConstructorArguments[0].Value as string ?? "Standard";
            }
        }
        return "Standard";
    }

    private static InputParamKind GetParamKind(MethodInfo method)
    {
        var nonCtxParams = method.GetParameters()
            .Where(p => p.ParameterType.Name != "InputContext")
            .ToArray();

        if (nonCtxParams.Length == 0)
            return InputParamKind.None;

        return nonCtxParams[0].ParameterType.FullName switch
        {
            "System.String" => InputParamKind.String,
            "System.Single" => InputParamKind.Float,
            "System.Int32"  => InputParamKind.Int,
            _               => InputParamKind.None,
        };
    }
}
