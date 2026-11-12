using Fdp.Core;
using Xunit;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐ POC 3 (Parallel Simulation §11, A1-2) — a managed component READ (<c>GetComponent&lt;T&gt;</c>) stamps its chunk version today, so every
/// reader marks the chunk changed (the CGF's egress gate saw an authority "change" every frame: <c>OwnershipEgressSystem</c> reads every
/// entity's <c>DescriptorOwnership</c>). With <see cref="EntityRepository.ManagedReadsStamp"/> off the read leaves it; a real write (Set) still
/// stamps. 🔴 Red-proof: the switch on (today) ⇒ the read stamps (its own second assert).
/// </summary>
[Collection("ManagedReadsStamp")]
public sealed class ManagedReadNoStampTests
{
    [ComponentId(509)] private sealed record Owned(int V);

    [Fact]
    public void AManagedRead_DoesNotStamp_WhenTheSwitchIsOff_AWriteStill()
    {
        bool was = EntityRepository.ManagedReadsStamp;
        try
        {
            using var w = new EntityRepository();
            w.RegisterManagedComponent<Owned>();
            var e = w.CreateEntity();
            w.SetManagedComponent(e, new Owned(1));
            Assert.True(w.TryGetTable(typeof(Owned), out var table));

            EntityRepository.ManagedReadsStamp = false;
            w.Tick(); uint v = w.GlobalVersion;
            w.Tick();
            Assert.Equal(1, w.GetComponent<Owned>(e).V);
            Assert.False(table.HasChanges(v));
            w.SetManagedComponent(e, new Owned(2));
            Assert.True(table.HasChanges(v));

            EntityRepository.ManagedReadsStamp = true;   // today's default
            w.Tick(); uint v2 = w.GlobalVersion;
            w.Tick();
            Assert.Equal(2, w.GetComponent<Owned>(e).V);
            Assert.True(table.HasChanges(v2));
        }
        finally { EntityRepository.ManagedReadsStamp = was; }
    }
}
