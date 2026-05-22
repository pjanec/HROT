using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>
/// Fake type system with bool, int, float, string, Vector2, Vector3, Vector4, Color.
/// </summary>
public sealed class FakeTypeSystem : ITypeSystem
{
    private static readonly Dictionary<string, (Vector4 Color, string Name)> _types = new()
    {
        ["System.Boolean"]          = (new Vector4(0.60f, 0.00f, 0.00f, 1f), "Boolean"),
        ["System.Int32"]            = (new Vector4(0.03f, 0.41f, 0.18f, 1f), "Integer"),
        ["System.Single"]           = (new Vector4(0.15f, 0.63f, 0.90f, 1f), "Float"),
        ["System.String"]           = (new Vector4(0.87f, 0.35f, 0.11f, 1f), "String"),
        ["System.Numerics.Vector2"] = (new Vector4(1.00f, 0.90f, 0.10f, 1f), "Vector2"),
        ["System.Numerics.Vector3"] = (new Vector4(1.00f, 0.90f, 0.10f, 1f), "Vector3"),
        ["System.Numerics.Vector4"] = (new Vector4(0.90f, 0.70f, 0.10f, 1f), "Vector4"),
        ["NodeEditor.Color"]        = (new Vector4(0.20f, 0.85f, 0.70f, 1f), "Color"),
    };

    public bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info)
    {
        if (_types.TryGetValue(key.Id, out var t))
        {
            info = new TypeDisplayInfo(t.Name, null, null);
            return true;
        }
        info = default!;
        return false;
    }

    public Vector4 GetPinColor(TypeKey key)
        => _types.TryGetValue(key.Id, out var t) ? t.Color : new Vector4(0.8f, 0.8f, 0.8f, 1f);

    public PinShape GetPinShape(TypeKey key, ContainerKind container)
        => container == ContainerKind.Array ? PinShape.Diamond : PinShape.Circle;

    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key) => null;

    public bool AreCompatible(TypeKey from, TypeKey to) => from == to;
    public bool IsImplicitCast(TypeKey from, TypeKey to) => false;
}
