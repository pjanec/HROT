using System;
using System.Security.Cryptography;
using System.Text;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐ <c>CE-4153</c> S2 (docs/DESIGN_Scenario_Entity_Keys.md) — the ONE rule for an entity whose identity is DERIVED from another's:
    /// <c>uuidv5(namespace = the parent's EntityGuid, name)</c> (RFC 4122, SHA-1). Deterministic, so the same parent and slot give the same
    /// GUID on every node and every load. Used by <c>CreateEntityRequestSystem</c> for a composite template's children and seat presets;
    /// the macro expansion (S2: <c>uuidv5(instance GUID, local GUID)</c>) is the next caller — ⛔ no second implementation.
    /// </summary>
    public static class EntityGuidDerivation
    {
        /// <summary>The v5 GUID of <paramref name="name"/> within the namespace <paramref name="parent"/>.</summary>
        public static Guid Derive(Guid parent, string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            Span<byte> ns = stackalloc byte[16];
            parent.TryWriteBytes(ns, bigEndian: true, out _);   // RFC 4122 byte order
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var data = new byte[16 + nameBytes.Length];
            ns.CopyTo(data);
            nameBytes.CopyTo(data, 16);
            Span<byte> hash = stackalloc byte[20];
            SHA1.HashData(data, hash);
            Span<byte> b = stackalloc byte[16];
            hash.Slice(0, 16).CopyTo(b);
            b[6] = (byte)((b[6] & 0x0F) | 0x50);   // version 5
            b[8] = (byte)((b[8] & 0x3F) | 0x80);   // RFC 4122 variant
            return new Guid(b, bigEndian: true);
        }
    }
}
