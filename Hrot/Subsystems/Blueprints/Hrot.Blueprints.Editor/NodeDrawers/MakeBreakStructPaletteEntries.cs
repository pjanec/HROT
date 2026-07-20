using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Q#14 Option B — Add-Node palette entries for Make/Break struct nodes, one pair per discovered
/// <c>[BlackboardDtoStruct]</c> value type (via <see cref="ISharedStructTypeProvider"/>). Each descriptor
/// drops a <see cref="MakeStructNode"/>/<see cref="BreakStructNode"/> pre-baked with the struct's FQN and
/// its reflected fields (name + TypeId), so the node has its per-field pins immediately (the compiler
/// consumes the baked <see cref="StructFieldDecl"/>s; it never reflects). Mirrors
/// <see cref="BlueprintEventPaletteEntries"/>.
/// </summary>
public static class MakeBreakStructPaletteEntries
{
    public static IEnumerable<NodeKindDescriptor> Entries(ISharedStructTypeProvider provider)
    {
        if (provider is null) yield break;
        foreach (var fqn in provider.GetSharedStructTypeFqns())
        {
            var reflected = SharedStructFieldReflector.TryReflect(fqn);
            if (reflected is null || reflected.Count == 0) continue;
            var shortName = ShortName(fqn);
            yield return MakeDescriptor(fqn, shortName, reflected);
            yield return BreakDescriptor(fqn, shortName, reflected);
        }
    }

    private static List<StructFieldDecl> ToStructFields(IReadOnlyList<SharedFieldDecl> reflected)
        => reflected.Select(f => new StructFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList();

    private static NodeKindDescriptor MakeDescriptor(string fqn, string shortName, IReadOnlyList<SharedFieldDecl> reflected) => new()
    {
        Kind        = $"Struct.Make.{fqn}",
        DisplayName = $"Make {shortName}",
        Category    = "Structs",
        Tooltip     = $"Construct a {shortName} value from its fields.",
        Icon        = "bp/function",
        CreateInstance = () => new MakeStructNode { Id = Guid.NewGuid(), StructTypeId = fqn, Fields = ToStructFields(reflected) },
    };

    private static NodeKindDescriptor BreakDescriptor(string fqn, string shortName, IReadOnlyList<SharedFieldDecl> reflected) => new()
    {
        Kind        = $"Struct.Break.{fqn}",
        DisplayName = $"Break {shortName}",
        Category    = "Structs",
        Tooltip     = $"Split a {shortName} value into its fields.",
        Icon        = "bp/function",
        CreateInstance = () => new BreakStructNode { Id = Guid.NewGuid(), StructTypeId = fqn, Fields = ToStructFields(reflected) },
    };

    private static string ShortName(string fqn)
    {
        int cut = fqn.LastIndexOfAny(new[] { '.', '+' });
        return cut >= 0 && cut < fqn.Length - 1 ? fqn[(cut + 1)..] : fqn;
    }
}
