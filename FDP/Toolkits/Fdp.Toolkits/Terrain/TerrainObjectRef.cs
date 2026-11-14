using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ Buildings Stage 5d-2 (📄 docs/DESIGN_Building_Interiors.md §3b K4, §3j "5d-2 as built") — an AUTHORED reference to a
    /// terrain object (a door today): its KEY, <c>"&lt;terrain&gt;/&lt;building&gt;/&lt;doorId&gt;"</c>. The terrain-object twin of
    /// <c>EntityRef</c>: the type carries the meaning, JSON is the bare key string, <see cref="Resolve"/> finds this node's entity.
    /// <para>⭐ The key is the identity — the door entity's runtime number is allocated at load and means nothing in a saved file
    /// (K4) — so a reference needs no remap pass. Unmanaged (a <see cref="FixedString64"/>), so it can sit in a behaviour's
    /// params block; a key longer than <see cref="FixedString64.MaxLength"/> is refused when read, never truncated.</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(TerrainObjectRefJsonConverter))]
    public readonly struct TerrainObjectRef : IEquatable<TerrainObjectRef>
    {
        public readonly FixedString64 Key;

        public TerrainObjectRef(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (System.Text.Encoding.UTF8.GetByteCount(key) > FixedString64.MaxLength)
                throw new ArgumentException($"Terrain-object key '{key}' is longer than {FixedString64.MaxLength} bytes.", nameof(key));
            Key = new FixedString64(key);
        }

        /// <summary>No reference.</summary>
        public static readonly TerrainObjectRef None = default;

        /// <summary><c>true</c> when nothing is referenced.</summary>
        public bool IsNone => Key.ToString().Length == 0;

        /// <summary>The live entity standing for this terrain object on this node, or <see cref="Entity.Null"/> (none, or not here).</summary>
        public Entity Resolve(EntityRepository? repo)
            => repo == null || IsNone ? Entity.Null : TerrainObjects.Find(repo, Key.ToString());

        /// <summary>The terrain's static definition index of this door in <paramref name="world"/>, or -1.</summary>
        public int DoorIndex(TerrainWorld? world) => world == null || IsNone ? -1 : world.DoorIndexOf(Key.ToString());

        public bool Equals(TerrainObjectRef other) => Key.ToString() == other.Key.ToString();
        public override bool Equals(object? obj) => obj is TerrainObjectRef o && Equals(o);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key.ToString());
        public override string ToString() => Key.ToString();
    }

    /// <summary>JSON for <see cref="TerrainObjectRef"/>: the bare key string (<c>null</c> or <c>""</c> = none).</summary>
    public sealed class TerrainObjectRefJsonConverter : JsonConverter<TerrainObjectRef>
    {
        public override TerrainObjectRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return TerrainObjectRef.None;
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"A terrain-object reference is its key string, got {reader.TokenType}.");
            string key = reader.GetString() ?? "";
            return key.Length == 0 ? TerrainObjectRef.None : new TerrainObjectRef(key);
        }

        public override void Write(Utf8JsonWriter writer, TerrainObjectRef value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }
}
