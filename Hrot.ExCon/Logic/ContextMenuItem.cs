using Newtonsoft.Json;

namespace Hrot.ExCon.Logic;

/// <summary>
/// A single entry in a context menu definition.
/// Serialises to the JSON schema expected by the IG's
/// <c>ContextActionsUpdate.MenuDefinitionJson</c> field.
/// </summary>
public sealed class ContextMenuItem
{
    /// <summary>Unique integer action identifier echoed in <c>ContextActionInvoked.ActionId</c>.</summary>
    [JsonProperty("id")]
    public int Id { get; init; }

    /// <summary>Display text shown in the IG context menu.</summary>
    [JsonProperty("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Optional icon name from the IG icon atlas (e.g. "move_cursor", "gear").
    /// Null/empty means no icon.
    /// </summary>
    [JsonProperty("icon", NullValueHandling = NullValueHandling.Ignore)]
    public string? Icon { get; init; }

    /// <summary>Whether the item is interactive. Defaults to true.</summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Optional visual style hint (e.g. "destructive" for dangerous actions).
    /// Null means default style.
    /// </summary>
    [JsonProperty("style", NullValueHandling = NullValueHandling.Ignore)]
    public string? Style { get; init; }

    /// <summary>Optional keyboard shortcut hint (e.g. "M", "Del").</summary>
    [JsonProperty("shortcut", NullValueHandling = NullValueHandling.Ignore)]
    public string? Shortcut { get; init; }
}
