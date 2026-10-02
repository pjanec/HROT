using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Fdp.Tests
{
    /// <summary>
    /// HN-001 — the preview rewind must restore a managed component's PAYLOAD wherever it
    /// restores its PRESENCE bit.
    ///
    /// <para>
    /// The crash these rails pin: <c>POST /preview/exit</c> rewinds the live repository from the
    /// snapshot (<c>ReferencePreviewHandler.UnloadingPreviewCommit</c> → <c>EntityRepository.SyncFrom</c>).
    /// The entity index is always copied (<c>ApplyComponentFilter</c> bumps its chunk versions on every
    /// sync, so its versions never compare equal), but a managed component table is copied only when
    /// its chunk version differs from the source's. A type-erased removal — the command-buffer path
    /// every consuming system uses — nulled the payload without bumping that version, so the rewind
    /// skipped the chunk: presence restored, payload still null. The next tick's
    /// <c>GenesisMaterializationSystem.MaterializeTargets</c> queried <c>WithManaged&lt;T&gt;()</c>,
    /// dereferenced the null and aborted the process.
    /// </para>
    /// </summary>
    public class PreviewRewindManagedComponentTests
    {
        [DataPolicy(DataPolicy.Transient)]
        [ComponentId(302)]
        public sealed class RewindIntent
        {
            public string Label = string.Empty;
        }

        /// <summary>
        /// The exact shape of HN-001: capture, consume the intent through the command buffer, rewind.
        /// </summary>
        [Fact]
        public void Rewind_restores_the_payload_of_a_component_removed_through_the_command_buffer()
        {
            using var live = new EntityRepository();
            live.RegisterManagedComponent<RewindIntent>();

            var e = live.CreateEntity();
            live.SetManagedComponent(e, new RewindIntent { Label = "targets" });
            live.Tick();

            // ── preview enter: snapshot the live world ──
            using var snap = new EntityRepository();
            snap.SyncFrom(live, includeTransient: true);

            // ── inside the preview: a system consumes the intent via the command buffer ──
            using (var cmd = new EntityCommandBuffer())
            {
                cmd.RemoveManagedComponent<RewindIntent>(e);
                cmd.Playback(live);
            }
            Assert.False(live.HasManagedComponent<RewindIntent>(e));

            // ── preview exit: rewind the live world from the snapshot ──
            live.SyncFrom(snap, includeTransient: true);

            Assert.True(live.HasManagedComponent<RewindIntent>(e));
            var view = (ISimulationView)live;
            Assert.Equal("targets", view.GetManagedComponentRO<RewindIntent>(e).Label);
        }

        /// <summary>
        /// The invariant behind the fix, stated where it belongs — on the table itself. A removal
        /// through the type-erased path must make the chunk look dirty, or every version-gated
        /// consumer (snapshot, rewind, SoD replica, flight recorder) silently keeps stale data.
        /// </summary>
        [Fact]
        public void ClearRaw_marks_the_chunk_dirty_so_a_later_sync_cannot_skip_it()
        {
            using var source = new ManagedComponentTable<RewindIntent>();
            using var dest = new ManagedComponentTable<RewindIntent>();

            source.Set(0, new RewindIntent { Label = "kept" }, 7);
            dest.SyncDirtyChunks(source);
            Assert.Equal(7u, dest.GetChunkVersion(0));

            // The destination drops the object through the type-erased path the command buffer uses.
            dest.ClearRaw(0);
            Assert.NotEqual(source.GetChunkVersion(0), dest.GetChunkVersion(0));

            // A resync must therefore see the chunk as dirty and copy the payload back.
            dest.SyncDirtyChunks(source);
            Assert.NotNull(dest.GetRO(0));
        }

        /// <summary>
        /// The removal must still be visible to a snapshot taken AFTER it — the version bump makes
        /// the chunk dirty in both directions, so this pins that the fix did not simply freeze the
        /// chunk in place.
        /// </summary>
        [Fact]
        public void A_snapshot_taken_after_the_removal_still_sees_the_component_gone()
        {
            using var live = new EntityRepository();
            live.RegisterManagedComponent<RewindIntent>();

            var e = live.CreateEntity();
            live.SetManagedComponent(e, new RewindIntent { Label = "targets" });
            live.Tick();

            using (var cmd = new EntityCommandBuffer())
            {
                cmd.RemoveManagedComponent<RewindIntent>(e);
                cmd.Playback(live);
            }

            using var snap = new EntityRepository();
            snap.SyncFrom(live, includeTransient: true);

            Assert.False(snap.HasManagedComponent<RewindIntent>(e));
        }
    }
}
