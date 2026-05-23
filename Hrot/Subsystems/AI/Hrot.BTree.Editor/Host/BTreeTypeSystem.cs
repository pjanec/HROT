using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Minimal NodeEditor type system for the BTree host.
/// BTree edges carry a single implicit execution type; there is no data-flow.
/// </summary>
public sealed class BTreeTypeSystem : ITypeSystem
{
    /// <summary>The single type key used for all BTree tree-edges.</summary>
    public static readonly TypeKey ExecKey = new("bt.exec");

    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        if (key == ExecKey)
        {
            info = new TypeDisplayInfo("execution", "Tree edge", null);
            return true;
        }
        info = default!;
        return false;
    }

    // White for the implicit exec edge.
    public Vector4 GetPinColor(TypeKey key) => Vector4.One;

    public PinShape GetPinShape(TypeKey key, ContainerKind container) => PinShape.Triangle;

    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;

    // The only valid link is bt.exec -> bt.exec.
    public bool AreCompatible(TypeKey from, TypeKey to) => from == to;

    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
