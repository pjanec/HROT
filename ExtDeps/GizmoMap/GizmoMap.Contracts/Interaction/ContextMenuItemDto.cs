using System.Text.Json.Serialization;

// C# 9 init-only setters require IsExternalInit. In net8.0 it is provided by the runtime;
// netstandard2.1 needs a polyfill shim defined locally.
#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif

namespace Fdp.Toolkit.Diagnostics.Gizmos.Interaction
{
    /// <summary>
    /// Data-transfer object for a single entry in a context menu definition JSON array.
    ///
    /// Serialise an array of these with <c>System.Text.Json.JsonSerializer.Serialize</c>
    /// to produce the JSON string expected by <c>ContextMenuAdapter</c> and stored in
    /// <c>StringInternMap</c> for gizmo-stream context menus.
    ///
    /// Only non-default properties are written to JSON (via <see cref="JsonIgnoreCondition"/>),
    /// keeping the on-wire payload compact.
    /// </summary>
    public sealed class ContextMenuItemDto
    {
        /// <summary>
        /// Opaque integer action ID sent back to the SimHost when the user clicks this item.
        /// Zero for separator rows and submenu headers that have no direct action.
        /// </summary>
        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Id { get; init; }

        /// <summary>Human-readable label shown in the menu row.</summary>
        [JsonPropertyName("label")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Label { get; init; }

        /// <summary>Optional icon name (atlas key). Ignored when null.</summary>
        [JsonPropertyName("icon")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Icon { get; init; }

        /// <summary>
        /// Whether the menu item is interactive. Omit (leave <c>null</c>) for the default
        /// enabled state. Set to <c>false</c> to render the item greyed out.
        /// </summary>
        [JsonPropertyName("enabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Enabled { get; init; }

        /// <summary>Optional visual style hint (e.g. <c>"destructive"</c>). Ignored when null.</summary>
        [JsonPropertyName("style")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Style { get; init; }

        /// <summary>Optional keyboard shortcut label shown right-aligned. Ignored when null.</summary>
        [JsonPropertyName("shortcut")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Shortcut { get; init; }

        /// <summary>Optional tooltip shown on hover. Ignored when null.</summary>
        [JsonPropertyName("tooltip")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Tooltip { get; init; }

        /// <summary>
        /// When <c>true</c>, this entry renders as a horizontal separator line.
        /// All other properties are ignored for separator rows.
        /// </summary>
        [JsonPropertyName("separator")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsSeparator { get; init; }

        /// <summary>
        /// Nested child items. When non-null and non-empty this entry renders as
        /// a submenu header. The <see cref="Id"/> is not used for submenu headers.
        /// </summary>
        [JsonPropertyName("children")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ContextMenuItemDto[]? Children { get; init; }
    }
}
