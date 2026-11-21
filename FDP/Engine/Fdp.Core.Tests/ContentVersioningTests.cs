using Fdp.Core;
using Fdp.Core.Diagnostics;
using Xunit;

namespace Fdp.Core.Tests;

/// <summary>
/// ⭐ D-5 (the parallel-simulation experiment, E-4) — commit by content: a chunk stamped this frame that holds the bytes it held before gets
/// its previous version back at the frame's end; a real change keeps its stamp.
/// 🔴 Red-proof: the byte compare dropped (every stamped chunk reverted) ⇒ the real change at step 3 reads as no change.
/// </summary>
[Collection("ContentVersioning")]
public sealed class ContentVersioningTests
{
    [ComponentId(505)] private struct Cv { public int V; }

    [Fact]
    public void AWriteThatChangesNothing_KeepsThePreviousVersion_ARealChangeStamps()
    {
        bool was = ContentVersioning.On;
        ContentVersioning.On = true;
        try
        {
            using var w = new EntityRepository();
            w.RegisterComponent<Cv>();
            var e = w.CreateEntity();
            w.AddComponent(e, new Cv { V = 1 });
            var table = w.GetComponentTable<Cv>();

            w.Tick(); w.GetComponentRW<Cv>(e).V = 2; ContentVersioning.EndFrame(w);   // a real change: stamped, shadow taken
            uint changedAt = table.GetVersionForEntity(e.Index);
            Assert.Equal(w.GlobalVersion, changedAt);

            w.Tick(); w.GetComponentRW<Cv>(e).V = 2; ContentVersioning.EndFrame(w);   // the same bytes written
            Assert.Equal(changedAt, table.GetVersionForEntity(e.Index));
            Assert.False(table.HasChanges(changedAt));
            Assert.Equal(1, ContentVersioning.LastReverted);

            w.Tick(); w.GetComponentRW<Cv>(e).V = 3; ContentVersioning.EndFrame(w);   // a real change again
            Assert.Equal(w.GlobalVersion, table.GetVersionForEntity(e.Index));
            Assert.True(table.HasChanges(changedAt));
        }
        finally { ContentVersioning.On = was; }
    }
}
