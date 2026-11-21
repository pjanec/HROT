using System;
using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐ M-7 (CE-4486) — the world's content hash compares STATE: a component's FIELD bytes count, its padding does not. 📐 Measured: two identical
/// stepped 1 000-man runs hashed <c>ScenarioClock</c> differently, the rows differing only at bytes 10–12 — the padding after its long and its
/// short, holding a leftover of a pointer (a padding byte is never written by a field assignment, so it keeps what the slot held).
/// </summary>
public sealed class WorldContentHashTests
{
    [ComponentId(457)] private struct Padded { public long A; public short B; }   // 16 B: 6 bytes of padding
    [ComponentId(458)] private unsafe struct Mixed { public byte A; public int B; public bool C; public fixed byte D[3]; }

    /// <summary>🔴 Red-proof: the padding test dropped from <c>ContentHash</c> ⇒ the scribbled padding changes the hash.</summary>
    [Fact]
    public unsafe void M7_PaddingIsHistory_NotState_TheHashIgnoresIt_AFieldStillCounts()
    {
        using var w = new EntityRepository();
        w.RegisterComponent<Padded>();
        var e = w.CreateEntity();
        w.AddComponent(e, new Padded { A = 7, B = 3 });
        ulong clean = w.ContentHash();

        byte* row = (byte*)w.GetComponentPointer(e, ComponentType<Padded>.ID);
        for (int k = 10; k < 16; k++) row[k] = 0xA7;                    // what a slot's padding may hold: a leftover
        Assert.Equal(clean, w.ContentHash());

        w.GetComponentRW<Padded>(e).B = 4;                               // a field: counts
        Assert.NotEqual(clean, w.ContentHash());
    }

    /// <summary>The mask is the MANAGED layout (⛔ not Marshal's, where a bool is 4 bytes): A at 0, B at 4–7, C at 8, the fixed buffer at 9–11.</summary>
    [Fact]
    public unsafe void M7_TheFieldBytes_AreTheManagedLayout_AFixedBufferWhole()
    {
        int size = sizeof(Mixed);
        var mask = EntityRepository.FieldBytesOf(typeof(Mixed), size);
        var expect = new bool[size];
        expect[0] = true;
        for (int k = 4; k < 8; k++) expect[k] = true;
        expect[8] = true;
        for (int k = 9; k < 12; k++) expect[k] = true;
        Assert.Equal(expect, mask);
    }
}
