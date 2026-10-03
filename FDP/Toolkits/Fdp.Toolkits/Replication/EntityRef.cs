using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// ⭐⭐ An AUTHORED reference to another entity: its NETWORK id — the identity that survives a save, a scenario load
    /// and a node boundary. 📄 <c>docs/blueprints/DESIGN_Entity_Reference.md</c>.
    /// <para>
    /// The TYPE carries the meaning, so every consumer keys on it: the editor offers an entity picker for it, scenario
    /// load remaps it (<see cref="EntityRefRemap"/>), the blueprint and blackboard palettes offer it, and
    /// <see cref="Resolve"/> turns it into this node's local <see cref="Entity"/>. In JSON it is the BARE NUMBER, so a file
    /// written when the field was a <c>long</c> loads unchanged.
    /// </para>
    /// <para>⛔ Authored or saved data uses <see cref="EntityRef"/>; a local <see cref="Entity"/> handle is runtime state
    /// only (it is recycled and node-local — <c>cgf-scn-2</c> Rule 3).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [JsonConverter(typeof(EntityRefJsonConverter))]
    public readonly struct EntityRef : IEquatable<EntityRef>
    {
        /// <summary>The referenced entity's network id; <c>0</c> = no reference.</summary>
        public readonly long NetworkId;

        public EntityRef(long networkId) => NetworkId = networkId;

        /// <summary>No reference.</summary>
        public static readonly EntityRef None = default;

        /// <summary><c>true</c> when nothing is referenced.</summary>
        public bool IsNone => NetworkId == 0;

        /// <summary>
        /// The local entity this reference names on this node, or <see cref="Entity.Null"/> when it is none, unknown here,
        /// or stale — through <see cref="NetworkIdResolver.ResolveNetworkId"/>, the one resolver with a stale check.
        /// </summary>
        public Entity Resolve(EntityRepository? repo)
            => IsNone ? Entity.Null : NetworkIdResolver.ResolveNetworkId(repo, NetworkId);

        /// <summary><see cref="Resolve(EntityRepository?)"/> from a simulation view (a non-repository view resolves nothing).</summary>
        public Entity Resolve(ISimulationView? view) => Resolve(view as EntityRepository);

        /// <summary><see cref="Resolve(EntityRepository?)"/> as a try-pattern; <c>false</c> leaves <paramref name="entity"/> null.</summary>
        public bool TryResolve(EntityRepository? repo, out Entity entity)
        {
            entity = Resolve(repo);
            return !entity.IsNull;
        }

        /// <summary>The reference to <paramref name="entity"/> — its runtime network id, or <see cref="None"/> when it has none.</summary>
        public static EntityRef Of(EntityRepository? repo, Entity entity)
            => new(NetworkIdResolver.RuntimeNetworkIdOf(repo, entity));

        /// <summary>This reference with its id rewritten by <paramref name="oldToNew"/> (unchanged when absent).</summary>
        public EntityRef Remap(IReadOnlyDictionary<long, long> oldToNew)
            => !IsNone && oldToNew.TryGetValue(NetworkId, out long n) ? new EntityRef(n) : this;

        /// <summary>⭐ Explicit both ways: the same number, so a blueprint's coercion (a cast) and C# code can cross between a
        /// stored <c>long</c> id and a reference — but never silently.</summary>
        public static explicit operator EntityRef(long networkId) => new(networkId);
        /// <inheritdoc cref="op_Explicit(long)"/>
        public static explicit operator long(EntityRef reference) => reference.NetworkId;

        public bool Equals(EntityRef other) => NetworkId == other.NetworkId;
        public override bool Equals(object? obj) => obj is EntityRef o && Equals(o);
        public override int GetHashCode() => NetworkId.GetHashCode();
        public static bool operator ==(EntityRef a, EntityRef b) => a.NetworkId == b.NetworkId;
        public static bool operator !=(EntityRef a, EntityRef b) => a.NetworkId != b.NetworkId;

        public override string ToString() => IsNone ? "(none)" : "#" + NetworkId;
    }

    /// <summary>
    /// <see cref="EntityRef"/> as the BARE network id (<c>1001</c>). Reads a number, a numeric string, <c>null</c> (none) or
    /// the object form <c>{"NetworkId": 1001}</c>; always writes the number.
    /// </summary>
    public sealed class EntityRefJsonConverter : JsonConverter<EntityRef>
    {
        public override EntityRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return new EntityRef(reader.GetInt64());
                case JsonTokenType.Null:
                    return EntityRef.None;
                case JsonTokenType.String:
                    var s = reader.GetString();
                    return string.IsNullOrWhiteSpace(s) ? EntityRef.None
                         : long.TryParse(s, out long fromString) ? new EntityRef(fromString)
                         : throw new JsonException($"EntityRef: '{s}' is not a network id.");
                case JsonTokenType.StartObject:
                    long id = 0;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName) continue;
                        bool isId = string.Equals(reader.GetString(), "NetworkId", StringComparison.OrdinalIgnoreCase);
                        reader.Read();
                        if (isId && reader.TokenType == JsonTokenType.Number) id = reader.GetInt64();
                        else reader.Skip();
                    }
                    return new EntityRef(id);
                default:
                    throw new JsonException($"EntityRef: unexpected {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, EntityRef value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.NetworkId);
    }
}
