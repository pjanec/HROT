using System.Reflection;
using Hrot.Editor.AiShared;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>One reflected <c>[BlueprintEvent]</c> payload field → a pin (name + pin TypeId).</summary>
public sealed record DiscoveredEventField(string Name, string TypeId);

/// <summary>
/// A discovered custom event: its carrier FQN + picker metadata + reflected payload fields + the optional
/// <c>[EventTarget]</c> field name (recipient, drives the Self/Any filter). This is the shape the editor bakes
/// onto <c>PublishEvent</c> / <c>EventEntry</c> nodes and lists in the picker.
/// </summary>
public sealed record DiscoveredBlueprintEvent(
    string EventTypeFqn,
    string DisplayName,
    string Category,
    IReadOnlyList<DiscoveredEventField> Fields,
    string? TargetFieldName);

/// <summary>
/// Architect Q#14 — editor-only reflection discovery of <c>[BlueprintEvent]</c> structs (the 2a C# hand-authored
/// path). Mirrors <see cref="BlueprintCallablePaletteEntries"/>: scans the loaded Hrot/Fdp game assemblies,
/// reflects each event struct's public instance fields into <c>(Name, TypeId)</c> pins, and records the
/// <c>[EventTarget]</c> field. The compiler never reflects — the editor bakes these strings onto the node.
/// </summary>
public static class BlueprintEventDiscovery
{
    /// <summary>Discovers all <c>[BlueprintEvent]</c> structs in loaded Hrot/Fdp assemblies. Per-assembly/type failures are skipped.</summary>
    public static IEnumerable<DiscoveredBlueprintEvent> Discover()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
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
                if (type is null || !type.IsValueType) continue;

                BlueprintEventAttribute? attr;
                try { attr = type.GetCustomAttribute<BlueprintEventAttribute>(); }
                catch { continue; }
                if (attr is null) continue;

                var ev = ToDiscovered(type, attr);
                if (ev != null) yield return ev;
            }
        }
    }

    private static DiscoveredBlueprintEvent? ToDiscovered(Type type, BlueprintEventAttribute attr)
    {
        var fqn = type.FullName;
        if (string.IsNullOrEmpty(fqn)) return null;

        var fields = new List<DiscoveredEventField>();
        string? targetFieldName = null;

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            fields.Add(new DiscoveredEventField(f.Name, ToPinTypeId(f.FieldType)));
            if (targetFieldName is null && f.IsDefined(typeof(EventTargetAttribute), inherit: false))
                targetFieldName = f.Name;
        }

        var display = string.IsNullOrEmpty(attr.DisplayName) ? type.Name : attr.DisplayName!;
        return new DiscoveredBlueprintEvent(fqn, display, attr.Category, fields, targetFieldName);
    }

    /// <summary>
    /// Pin TypeId for a reflected field type. Enums carry the AN2 <c>"global::"</c> sentinel that
    /// <c>StaticTypeRegistry</c> accepts as an unmanaged enum; everything else uses the plain FQN.
    /// </summary>
    private static string ToPinTypeId(Type t)
    {
        var fqn = t.FullName ?? t.Name;
        return t.IsEnum ? "global::" + fqn : fqn;
    }
}
