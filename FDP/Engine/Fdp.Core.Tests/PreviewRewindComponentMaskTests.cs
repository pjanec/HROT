using Fdp.Core;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// CE-3067 — the preview rewind must restore the entity index (the component MASKS) whole.
    ///
    /// <para>
    /// Adding or removing a component writes the mask without stamping the mask chunk's version, and
    /// <c>ApplyComponentFilter</c> bumps the DESTINATION's version by +1 on every sync. So when the source
    /// gained exactly ONE entity create/destroy in that chunk since the capture, both sides read V+1, the
    /// version-gated copy skipped the chunk, and the rewind kept the masks of the preview. 📐 Measured in
    /// <c>TakeCoverScenarioTests.CE2101_PreviewFromEdit_*</c>: a unit's blackboard tier was promoted 256→1024
    /// during the preview; after Stop it carried the 1024 bit (whose data had been rewound to zeros) and not
    /// the 256 bit, and <c>BrainTickSystem</c> threw "no ROOT TREE STATE slot".
    /// </para>
    /// </summary>
    public class PreviewRewindComponentMaskTests
    {
        [ComponentId(460)]
        public struct SmallStore { public int Value; }

        [ComponentId(461)]
        public struct LargeStore { public int Value; }

        [Fact]
        public void Rewind_restores_the_masks_when_the_preview_created_exactly_one_entity_in_the_chunk_CE3067()
        {
            using var live = new EntityRepository();
            live.RegisterComponent<SmallStore>();
            live.RegisterComponent<LargeStore>();

            var unit = live.CreateEntity();
            live.AddComponent(unit, new SmallStore { Value = 256 });
            live.Tick();

            // ── preview enter ──
            using var snap = new EntityRepository();
            snap.SyncFrom(live);

            // ── inside the preview: a tier promotion (add the larger store, remove the smaller) and ONE
            //    entity created in the same chunk — the single create/destroy that makes the versions coincide ──
            live.Tick();
            live.AddComponent(unit, new LargeStore { Value = 1024 });
            live.RemoveComponent<SmallStore>(unit);
            live.CreateEntity();

            // ── preview exit ──
            live.SyncFrom(snap);

            Assert.True(live.HasComponent<SmallStore>(unit), "the rewind must restore the snapshot's store bit");
            Assert.False(live.HasComponent<LargeStore>(unit), "the bit added during the preview must be gone");
            Assert.Equal(256, live.GetComponentRO<SmallStore>(unit).Value);
        }
    }
}
