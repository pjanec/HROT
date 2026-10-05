using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// ⭐⭐ <c>CE-2068</c> — an AUTHORED reference to a utility decision. The TYPE makes a field pickable (R-184): the editor
    /// offers the decision catalog for any field of this type. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3.
    /// <para>Runtime value: the decision id (<c>UtilityDecisionCatalog.ComputeId(assetId)</c>), what the scorer keys on.
    /// JSON: the decision's ASSET ID (a string), so a file names the decision and survives an id-scheme change. A bare
    /// number is read too (a hand-registered decision with no asset id is written that way); 0 = none.</para>
    /// </summary>
    [JsonConverter(typeof(UtilityDecisionRefJsonConverter))]
    public readonly struct UtilityDecisionRef : IEquatable<UtilityDecisionRef>
    {
        /// <summary>The decision id; 0 = none.</summary>
        public readonly int Id;

        public UtilityDecisionRef(int id) => Id = id;

        /// <summary>The reference to the decision with <paramref name="assetId"/>.</summary>
        public static UtilityDecisionRef FromAssetId(string? assetId)
            => string.IsNullOrEmpty(assetId) ? None : new UtilityDecisionRef(UtilityDecisionCatalog.ComputeId(assetId!));

        public static readonly UtilityDecisionRef None = default;
        public bool IsNone => Id == 0;

        /// <summary>The registered decision's asset id in <paramref name="registry"/>, or null.</summary>
        public string? AssetIdIn(UtilityRegistry registry)
            => registry.TryGet(Id, out var def, out _) && def != null && !string.IsNullOrEmpty(def.AssetId) ? def.AssetId : null;

        public bool Equals(UtilityDecisionRef other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is UtilityDecisionRef o && Equals(o);
        public override int GetHashCode() => Id;
        public static bool operator ==(UtilityDecisionRef a, UtilityDecisionRef b) => a.Id == b.Id;
        public static bool operator !=(UtilityDecisionRef a, UtilityDecisionRef b) => a.Id != b.Id;
        public override string ToString() => IsNone ? "(none)" : "#" + Id.ToString("X8");
    }

    /// <summary><see cref="UtilityDecisionRef"/> as the decision's asset id (falling back to the bare id).</summary>
    public sealed class UtilityDecisionRefJsonConverter : JsonConverter<UtilityDecisionRef>
    {
        public override UtilityDecisionRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.String => UtilityDecisionRef.FromAssetId(reader.GetString()),
                JsonTokenType.Number => new UtilityDecisionRef(reader.GetInt32()),
                JsonTokenType.Null   => UtilityDecisionRef.None,
                _ => throw new JsonException($"A UtilityDecisionRef is an asset id or a number, not {reader.TokenType}."),
            };

        public override void Write(Utf8JsonWriter writer, UtilityDecisionRef value, JsonSerializerOptions options)
        {
            if (value.IsNone) { writer.WriteNumberValue(0); return; }
            if (value.AssetIdIn(UtilityDecisionCatalog.Shared) is { } assetId) writer.WriteStringValue(assetId);
            else writer.WriteNumberValue(value.Id);
        }
    }
}
