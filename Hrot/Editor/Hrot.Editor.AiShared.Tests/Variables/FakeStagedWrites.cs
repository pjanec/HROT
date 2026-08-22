using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐ <b><c>W4</c>'s test stand-in for <c>DataBreakpointManager</c>'s staged set.</b>
///
/// <para>⚠⚠ <b>Why a double here and NOT in the <c>W4</c> acceptance rail.</b> 📌 The programme's own
/// lesson *(<c>BP-402</c> ①/②)*: a rail that builds its own composition root cannot see a
/// composition-root defect. ⇒ ⭐ this double exists for the <b>unit</b> rails, which are about the
/// monitor/model's own arithmetic and would otherwise need a live ECS repository to say anything;
/// ⭐⭐ the rail that matters — <i>Details and Watch report the SAME staged bytes</i> — drives the
/// <b>REAL</b> <c>DataBreakpointManager</c> through the real <c>IStagedWrites</c>, because that is the
/// claim the design makes.</para>
///
/// <para>⛔ <c>DrainInto</c> is a plain clear: this double owns no repository, and what the unit rails
/// need from a drain is <i>"the mutation leaves the set"</i> — 📌 the auto-clear §4 fork A buys.</para>
/// </summary>
internal sealed class FakeStagedWrites : IStagedWrites
{
    private readonly Dictionary<(Entity, int, int), byte[]> _staged = new();

    public bool HasPending => _staged.Count > 0;
    public bool IsRewound  { get; set; }

    /// <summary>⭐ Stage one field write, keyed exactly as <c>TryGetPending</c> looks it up.</summary>
    public void Stage(Entity entity, int typeId, int byteOffset, byte[] bytes)
        => _staged[(entity, typeId, byteOffset)] = bytes;

    public void DrainInto(ISimulationView view) => _staged.Clear();

    public bool TryGetPending(Entity entity, int typeId, int byteOffset, out byte[] bytes)
        => _staged.TryGetValue((entity, typeId, byteOffset), out bytes!)
            || Miss(out bytes);

    private static bool Miss(out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        return false;
    }
}

/// <summary>
/// ⭐ <b>A <see cref="StagedWriteView"/> over <see cref="FakeStagedWrites"/> with a trivial address
/// map</b>, so a unit rail can say <i>"this row is staged"</i> without a blueprint session.
/// ⛔ The PRODUCTION resolver is <c>BlueprintLiveValueWriter.ResolveStagedField</c>; this one only
/// stands in for it where the claim under test is not about resolution.
/// </summary>
internal static class FakeStagedWriteView
{
    /// <summary>
    /// ⭐⭐ <b>A DISTINCT address per <c>(asset, variable)</c>.</b>
    /// ⚠⚠ Not a constant pair: 📐 the rails that need this put two rows on the SAME entity and
    /// distinguish them by asset, so an address that ignored the origin would mark BOTH pending and the
    /// rail would pass while asserting nothing — 📌 exactly <c>BP-402</c> ②'s vacuous shape.
    /// ⭐ The real resolver discriminates the same way *(a name resolves to its own component + offset)*;
    /// this just does it arithmetically.
    /// </summary>
    public static StagedFieldAddress AddressOf(VariableRowOrigin origin)
        => new(TypeId:     origin.AssetId.GetHashCode(),
               ByteOffset: Math.Abs(StringComparer.Ordinal.GetHashCode(origin.VariablePath ?? "")) % 4096,
               SizeBytes:  4);

    public static StagedWriteView Over(FakeStagedWrites writes, Func<Entity?> selectedEntity)
        => new(() => writes, (origin, _) => AddressOf(origin), selectedEntity);

    /// <summary>⭐ Stage a row's own bytes, at the address <see cref="Over"/> will look them up at.</summary>
    public static void Stage(
        this FakeStagedWrites writes, VariableRowOrigin origin, Entity entity, byte[] bytes)
    {
        var a = AddressOf(origin);
        writes.Stage(entity, a.TypeId, a.ByteOffset, bytes);
    }
}
