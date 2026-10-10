using Fdp.Toolkit.Tkb.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Presentation descriptor for a TKB entity.
    /// Drives projection of <c>VisualData</c> and <c>EntityInfo</c> by
    /// <c>PresentationTkbTranslator</c> on IG nodes.
    /// </summary>
    [TkbDescriptor("IG.VisualDef")]
    public record VisualDefinitionDto
    {
        /// <summary>MIL-STD-2525 symbol code (e.g., "SFGPUCIZ-------").</summary>
        public string SymbolCode { get; init; } = string.Empty;

        /// <summary>Path to 3D model file relative to the models directory.</summary>
        public string ModelPath { get; init; } = string.Empty;

        /// <summary>Base color in hex format (#RRGGBB).</summary>
        public string ColorHex { get; init; } = "#FFFFFF";

        /// <summary>Uniform scale factor for the 3D model.</summary>
        public float Scale { get; init; } = 1.0f;

        /// <summary>Whether to show the label in the rendered view.</summary>
        public bool ShowLabel { get; init; } = true;

        /// <summary>Optional explicit name of the 2-D map shape. Null uses the symbol renderer default.</summary>
        public string? MapShapeName { get; init; }

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — the entity icon's name: <c>Assets/EntityIcons/&lt;IconName&gt;.png</c>, keyed
        /// <c>entity/&lt;IconName&gt;</c> by the entity icon library. Null ⇒ the picker falls back to a generic glyph for the
        /// DIS kind/domain. 📄 docs/DESIGN_Add_Entity_Picker.md D3.
        /// </summary>
        public string? IconName { get; init; }
    }
}
