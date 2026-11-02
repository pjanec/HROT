using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐⭐ A CLUSTER NODE — the id of one running node (a SimHost, a Brain, an IG, the editor …) in the cluster, ONE width (<see cref="int"/>).
    /// 📄 <c>docs/DESIGN_Semantic_Value_Types.md</c> SV-3 (CE-4200). Today's 26 <c>int</c> / 17 <c>long</c> node-id fields were one concept in two widths.
    /// <para>
    /// The TYPE carries the meaning a bare <see cref="int"/> cannot: a tool shows the node's role and name ("Brain #1") instead of a number, and a node id can
    /// no longer be swapped with an entity id or an index. ⭐ Memory and JSON are an <see cref="int"/>'s exactly (one 4-byte field; a plain number), so
    /// recordings, blobs and config files are byte-identical. Conversion to <see cref="int"/> is implicit (every API taking a raw node id keeps compiling);
    /// conversion FROM <see cref="int"/> is explicit.
    /// </para>
    /// <para>⛔ NOT a node id: a navigation-graph node, a blueprint / behaviour-graph NODE, a DDS participant or process id — those keep their own types.
    /// On the DDS wire a node id travels as a plain <c>int</c> (the IDL has no wrapper type); the type stops at the topic boundary.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(NodeIdJsonConverter))]
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
    {
        /// <summary>The node id.</summary>
        public readonly int Value;

        public NodeId(int value) => Value = value;

        /// <summary>No node (<c>0</c> — the master / unset).</summary>
        public static readonly NodeId None = default;

        public static implicit operator int(NodeId id) => id.Value;
        /// <summary>⭐ Explicit: a raw number becomes a node id only on purpose.</summary>
        public static explicit operator NodeId(int value) => new(value);

        public bool Equals(NodeId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is NodeId o && Equals(o);
        public override int GetHashCode() => Value;
        public int CompareTo(NodeId other) => Value.CompareTo(other.Value);
        public static bool operator ==(NodeId a, NodeId b) => a.Value == b.Value;
        public static bool operator !=(NodeId a, NodeId b) => a.Value != b.Value;
        // mixed with a raw int (the same bytes): exact, not ambiguous
        public static bool operator ==(NodeId a, int b) => a.Value == b;
        public static bool operator !=(NodeId a, int b) => a.Value != b;
        public static bool operator ==(int a, NodeId b) => a == b.Value;
        public static bool operator !=(int a, NodeId b) => a != b.Value;

        /// <summary>The number (the node's role and name need the cluster view).</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        /// <summary>The number, formatted with <paramref name="provider"/> (the call shape a plain <see cref="int"/> had).</summary>
        public string ToString(IFormatProvider? provider) => Value.ToString(provider);
    }

    /// <summary><see cref="NodeId"/> exactly as an <see cref="int"/>: a plain JSON number.</summary>
    public sealed class NodeIdJsonConverter : JsonConverter<NodeId>
    {
        public override NodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<int>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value.Value, options);
    }
}
