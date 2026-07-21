using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Architect Q#12 — editor-only discovery of CLR helpers marked <c>[BlueprintCallable]</c>. Reflection-scans
/// the loaded game assemblies for <c>public static</c> methods bearing the attribute and yields one
/// <see cref="NodeKindDescriptor"/> per method (grouped by the attribute's <c>Category</c>, tooltip from the
/// method's XML-doc <c>&lt;summary&gt;</c> via <see cref="ClrXmlDocSource"/>). Each descriptor drops a
/// <see cref="FunctionCallNode"/> pre-baked with <c>TargetTypeId</c>/<c>MethodName</c> — the compiler resolves
/// it exactly as any FunctionCall (it never sees the attribute).
///
/// <para>
/// This is the attribute-driven analogue of the hand-written <see cref="BlueprintMathPaletteEntries"/> — the
/// designer picks from the curated, filterable Add-Node picker and never types an FQN.
/// </para>
/// </summary>
public static class BlueprintCallablePaletteEntries
{
    /// <summary>
    /// Discovers all <c>[BlueprintCallable]</c> public-static methods in the loaded Hrot/Fdp game
    /// assemblies and projects them to palette descriptors. Failures per-assembly/type are skipped.
    /// </summary>
    public static IEnumerable<NodeKindDescriptor> Discover()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Only the game assemblies host curated helpers — skip BCL/system to keep the scan cheap.
            var name = asm.GetName().Name ?? string.Empty;
            if (!name.StartsWith("Hrot", StringComparison.Ordinal) &&
                !name.StartsWith("Fdp", StringComparison.Ordinal))
                continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null) continue;
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static); }
                catch { continue; }

                foreach (var method in methods)
                {
                    BlueprintCallableAttribute? attr;
                    try { attr = method.GetCustomAttribute<BlueprintCallableAttribute>(); }
                    catch { continue; }
                    if (attr == null) continue;

                    var descriptor = ToDescriptor(type, method, attr);
                    if (descriptor != null)
                        yield return descriptor;
                }
            }
        }
    }

    private static NodeKindDescriptor? ToDescriptor(Type type, MethodInfo method, BlueprintCallableAttribute attr)
    {
        var typeId = type.FullName;
        if (string.IsNullOrEmpty(typeId)) return null;

        var methodName  = method.Name;
        var displayName = string.IsNullOrEmpty(attr.DisplayName) ? methodName : attr.DisplayName!;
        var tooltip     = ClrXmlDocSource.GetSummary(method) ?? string.Empty;
        var isPure      = attr.IsPure;

        return new NodeKindDescriptor
        {
            Kind        = $"Clr.{typeId}.{methodName}",
            DisplayName = displayName,
            Category    = attr.Category,
            Tooltip     = tooltip,
            Icon        = isPure ? "bp/pure" : "bp/function",
            CreateInstance = () => new FunctionCallNode
            {
                Id           = Guid.NewGuid(),
                TargetTypeId = typeId,
                MethodName   = methodName,
                IsPure       = isPure,
            },
        };
    }
}
