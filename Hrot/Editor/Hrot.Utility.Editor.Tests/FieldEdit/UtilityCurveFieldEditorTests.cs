using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.FieldEdit;
using StructEdit.Core;
using StructEdit.Core.Plugins;
using Xunit;

namespace Hrot.Utility.Editor.Tests.FieldEdit
{
    public class UtilityCurveFieldEditorTests
    {
        // Minimal IValueBinding stub: returns default UtilityCurve, ignores writes.
        private sealed class StubBinding : IValueBinding
        {
            public Type ValueType => typeof(UtilityCurve);
            public object? GetBoxed() => default(UtilityCurve);
            public void SetBoxed(object? value) { }
            public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
        }

        [Fact]
        public void UtilityCurveFieldEditor_TargetType_IsUtilityCurve()
        {
            Assert.Equal(typeof(UtilityCurve), new UtilityCurveFieldEditor().TargetType);
        }

        [Fact]
        public void UtilityCurveFieldEditor_CreateNode_KindIsCustom()
        {
            var binding = new StubBinding();
            var node = new UtilityCurveFieldEditor().CreateNode(
                default, "Curve", "curve", binding, EditNodeMetadata.Empty);
            Assert.NotNull(node);
            Assert.Equal(EditNodeKind.Custom, node!.Kind);
        }

        [Fact]
        public void UtilityCurveFieldEditor_CreateNode_TypeIsUtilityCurve()
        {
            var binding = new StubBinding();
            var node = new UtilityCurveFieldEditor().CreateNode(
                default, "Curve", "curve", binding, EditNodeMetadata.Empty);
            Assert.NotNull(node);
            Assert.Equal(typeof(UtilityCurve), node!.ClrType);
        }

        [Fact]
        public void UtilityCurveFieldDrawer_TargetType_IsUtilityCurve()
        {
            // DrawInput is intentionally not tested here -- it requires an ImGui frame.
            Assert.Equal(typeof(UtilityCurve), new UtilityCurveFieldDrawer().TargetType);
        }
    }
}
