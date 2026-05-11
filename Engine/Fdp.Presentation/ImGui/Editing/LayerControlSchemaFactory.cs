using StructEdit.Core;

namespace Fdp.Presentation.ImGui.Editing;

public static class LayerControlSchemaFactory
{
    public static EditDocument BuildLayerControlDocument(Type targetType)
    {
        var baseNode = new EditNode(
            new EditNodeId(1), "BaseLayer", "$.BaseLayer",
            EditNodeKind.Boolean, typeof(bool),
            isReadOnly: false,
            binding: new BoolBinding(true));

        var unitsNode = new EditNode(
            new EditNodeId(2), "UnitsLayer", "$.UnitsLayer",
            EditNodeKind.Boolean, typeof(bool),
            isReadOnly: false,
            binding: new BoolBinding(true));

        var sensorsNode = new EditNode(
            new EditNodeId(3), "SensorsLayer", "$.SensorsLayer",
            EditNodeKind.Boolean, typeof(bool),
            isReadOnly: false,
            binding: new BoolBinding(true));

        var root = new EditNode(
            new EditNodeId(0), "LayerControl", "$",
            EditNodeKind.Struct, targetType,
            children: new[] { baseNode, unitsNode, sensorsNode });

        return new EditDocument(root, targetType, EditScope.WholeComponent);
    }

    private sealed class BoolBinding : IValueBinding
    {
        private bool _value;

        public Type ValueType => typeof(bool);

        public BoolBinding(bool initialValue)
        {
            _value = initialValue;
        }

        public object? GetBoxed() => _value;

        public void SetBoxed(object? value)
        {
            if (value is bool b) _value = b;
        }

        public bool TryGetSpan(out Span<byte> bytes)
        {
            bytes = default;
            return false;
        }
    }
}
