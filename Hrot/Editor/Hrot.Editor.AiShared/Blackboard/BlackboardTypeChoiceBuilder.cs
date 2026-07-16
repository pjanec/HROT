using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fbt.Kernel;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Builds the ordered, de-duplicated list of <see cref="VariableTypeChoice"/> offered by the
/// Add-Variable dropdown in the Variables panel (<see cref="VariablesPanelControl"/>).
/// <para>
/// The choice set is the union of:
/// </para>
/// <list type="number">
///   <item>The existing primitive/vector set (<see cref="BlackboardTypeHelper.DefaultKnownTypeNames"/>).</item>
///   <item>
///   Structs decorated with <see cref="BlackboardDtoStructAttribute"/>, discovered by scanning
///   every assembly currently loaded into <see cref="AppDomain.CurrentDomain"/> -- the same
///   predicate <see cref="BlackboardFieldClassifier"/> uses to recognize Category-1 shared-struct
///   fields.
///   </item>
///   <item>
///   DTO struct types the action-schema exporter auto-detects (a struct used as the first
///   <c>ref</c> parameter of a registered action/condition/guard), when an
///   <see cref="IActionSchemaExporter"/> is supplied.
///   </item>
/// </list>
/// Entries are de-duplicated by CLR <see cref="Type"/> (not by display name, since two structs
/// from different namespaces can share a short name); primitives are listed first, followed by
/// structs sorted by display name for a stable, predictable combo order.
/// </summary>
public static class BlackboardTypeChoiceBuilder
{
    /// <summary>
    /// Builds the default choice list. <paramref name="actionSchemaExporter"/> may be null when
    /// the caller has no schema exporter in scope (e.g. the Blueprint Variables window); in that
    /// case the result still contains primitives + <c>[BlackboardDtoStruct]</c> types.
    /// </summary>
    public static IReadOnlyList<VariableTypeChoice> BuildDefault(IActionSchemaExporter? actionSchemaExporter = null)
    {
        var seen = new HashSet<Type>();
        var primitives = new List<VariableTypeChoice>();
        var structs = new List<VariableTypeChoice>();

        foreach (var name in BlackboardTypeHelper.DefaultKnownTypeNames)
        {
            var t = BlackboardTypeHelper.GetPrimitiveType(name);
            if (t == null || !seen.Add(t)) continue;
            primitives.Add(new VariableTypeChoice(BlackboardTypeHelper.GetDisplayName(t), t));
        }

        foreach (var t in DiscoverBlackboardDtoStructTypes())
        {
            if (!seen.Add(t)) continue;
            structs.Add(new VariableTypeChoice(BlackboardTypeHelper.GetDisplayName(t), t));
        }

        if (actionSchemaExporter != null)
        {
            foreach (var entry in actionSchemaExporter.All.Values)
            {
                var t = entry.DtoType;
                if (t == null || !seen.Add(t)) continue;
                structs.Add(new VariableTypeChoice(BlackboardTypeHelper.GetDisplayName(t), t));
            }
        }

        structs.Sort((a, b) => string.CompareOrdinal(a.Display, b.Display));

        var result = new List<VariableTypeChoice>(primitives.Count + structs.Count);
        result.AddRange(primitives);
        result.AddRange(structs);
        return result;
    }

    /// <summary>
    /// Scans every assembly currently loaded into <see cref="AppDomain.CurrentDomain"/> for value
    /// types decorated with <see cref="BlackboardDtoStructAttribute"/>. Same predicate as
    /// <see cref="BlackboardFieldClassifier"/>'s <c>IsKnownType</c> check.
    /// </summary>
    private static IReadOnlyList<Type> DiscoverBlackboardDtoStructTypes()
    {
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Partial load: keep whichever types did load rather than skip the assembly.
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                // Dynamic or otherwise unintrospectable assembly -- skip it.
                continue;
            }

            foreach (var t in types)
            {
                if (!t.IsValueType) continue;
                if (!t.IsDefined(typeof(BlackboardDtoStructAttribute), inherit: false)) continue;
                result.Add(t);
            }
        }

        return result;
    }
}
