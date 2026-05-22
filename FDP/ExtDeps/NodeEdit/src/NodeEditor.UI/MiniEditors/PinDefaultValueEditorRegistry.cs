using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Concrete implementation of <see cref="IPinDefaultValueEditorRegistry"/>.
/// Call <see cref="CreateWithBuiltins"/> to get a pre-populated instance.
/// Hosts may override any entry or add new ones after construction.
/// </summary>
public sealed class PinDefaultValueEditorRegistry : IPinDefaultValueEditorRegistry
{
    private readonly Dictionary<TypeKey, IPinDefaultValueEditor> _byType = [];
    private IPinDefaultValueEditor? _fallback;

    /// <inheritdoc/>
    public void Register(TypeKey type, IPinDefaultValueEditor editor)
        => _byType[type] = editor;

    /// <inheritdoc/>
    public void RegisterFallback(IPinDefaultValueEditor editor)
        => _fallback = editor;

    /// <inheritdoc/>
    public IPinDefaultValueEditor? GetEditor(TypeKey type)
        => _byType.TryGetValue(type, out var ed) ? ed : _fallback;

    /// <summary>
    /// Create a registry pre-populated with the built-in type editors:
    /// <c>bool</c>, <c>int</c>, <c>float</c>, <c>string</c>,
    /// <c>Vector2/3/4</c>, <c>Quaternion</c>, <c>Color</c>, and <c>Guid</c>.
    /// </summary>
    public static PinDefaultValueEditorRegistry CreateWithBuiltins()
    {
        var r = new PinDefaultValueEditorRegistry();

        r.Register(new TypeKey("System.Boolean"),           new BoolPinEditor());
        r.Register(new TypeKey("System.Int32"),             new IntPinEditor());
        r.Register(new TypeKey("System.Single"),            new FloatPinEditor());
        r.Register(new TypeKey("System.String"),            new StringPinEditor());
        r.Register(new TypeKey("System.Numerics.Vector2"),  new VectorPinEditor(2));
        r.Register(new TypeKey("System.Numerics.Vector3"),  new VectorPinEditor(3));
        r.Register(new TypeKey("System.Numerics.Vector4"),  new VectorPinEditor(4));
        r.Register(new TypeKey("System.Numerics.Quaternion"), new QuaternionPinEditor());
        r.Register(new TypeKey("NodeEditor.Color"),         new ColorPinEditor());
        r.Register(new TypeKey("System.Guid"),              new GuidPinEditor());

        return r;
    }
}
