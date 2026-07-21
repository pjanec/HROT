using System;
using System.Security.Cryptography;
using System.Text;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// Canonical deterministic identity for rehydrated pins (Blocker-1 part 2, architect Q#10-A/C option 1).
/// <para>
/// Blueprints are stored projection-only (<c>"Pins": []</c>); both the compiler (<c>Stage0_Rehydrate</c>)
/// and the editor (<c>BlueprintGraphModel.Rebuild</c>) reconstruct pin GUIDs on load, and links reference
/// pins purely by GUID. A pin GUID derived deterministically from <c>(nodeId, pinName, direction)</c> lets a
/// link resolve to the same pin regardless of link order — replacing the order-fragile positional scheme
/// (exec-In and data-In share a direction bucket, so a non-canonical link order swapped them).
/// </para>
/// <para>
/// Reconstruction is <b>backward-compatible</b> (Q#10-C option 1): a node whose incident links reference a
/// deterministic pin GUID is treated as migrated and gets deterministic GUIDs for all pins; a legacy node
/// (arbitrary/positional link GUIDs) keeps the old positional binding. So this coexists with the many
/// existing pin-less assets that have not been migrated.
/// </para>
/// <para>
/// This MUST be byte-identical to <c>NodeEditor.Primitives.IdGenerator.Deterministic($"pin:{nodeId:N}:{name}:{dir}")</c>
/// (the editor's helper — the compiler cannot reference the editor assembly, hence the replicated
/// algorithm here; a parity unit test pins the two together). SHA-256 of the UTF-8 input, first 16 bytes,
/// with RFC-4122 v5 version+variant bits stamped.
/// </para>
/// <para>
/// INVARIANT: pin names are unique within a (node, direction) bucket — otherwise two pins collide on one
/// GUID. Node registries and FunctionCall parameter lists satisfy this (exec pins "In"/"Out" never share a
/// name with a data pin in the same direction).
/// </para>
/// </summary>
public static class DeterministicIds
{
    /// <summary>Deterministic GUID for a pin, from its owning node id, name, and direction ("In"/"Out").</summary>
    public static Guid PinId(Guid nodeId, string name, string direction)
        => FromString($"pin:{nodeId:N}:{name}:{direction}");

    /// <summary>SHA-256 → first 16 bytes → v5 version/variant bits. Byte-identical to
    /// <c>NodeEditor.Primitives.IdGenerator.Deterministic(string)</c>.</summary>
    public static Guid FromString(string input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var b = new byte[16];
        Array.Copy(hash, b, 16);
        b[6] = (byte)((b[6] & 0x0F) | 0x50); // version 5
        b[8] = (byte)((b[8] & 0x3F) | 0x80); // variant (RFC 4122)
        return new Guid(b);
    }
}
