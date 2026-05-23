using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Hsm.Editor.Host;

/// <summary>
/// Stub type system for the HSM canvas.
/// HSM states have no typed pins, so all queries return negative/default answers.
/// </summary>
internal sealed class HsmTypeSystem : ITypeSystem
{
    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        info = default!;
        return false;
    }

    public Vector4 GetPinColor(TypeKey key) => Vector4.Zero;

    public PinShape GetPinShape(TypeKey key, ContainerKind container) => PinShape.Circle;

    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;

    public bool AreCompatible(TypeKey from, TypeKey to) => false;

    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
