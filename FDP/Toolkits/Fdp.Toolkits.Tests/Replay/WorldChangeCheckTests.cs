using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replay;
using Xunit;

namespace Fdp.Toolkits.Tests.Replay
{
    [ComponentId(498)]
    internal struct ChangeCheckPos { public float X; }

    /// <summary>A runtime-only type: it churns while paused (a cache, a bookkeeping counter) and is not world state.</summary>
    [ComponentId(499)]
    [DataPolicy(DataPolicy.Transient)]
    internal struct ChangeCheckChurn { public int Counter; }

    /// <summary>
    /// ⭐⭐ <c>CE-4093</c> (AQ108 D20) — the node's whole-world change check, <see cref="WorldChangeCheck"/>: a hash of the world's keyframe CONTENT
    /// against a baseline. The noise test on a real scenario in the headless editor is <c>Hrot.Editor.Tests.TheWorldChangeCheckOnTheEditorTests</c>.
    /// 📄 docs/blueprints/Architect_Question_108_Run_Mode_Picker.md D20.
    /// </summary>
    public sealed class WorldChangeCheckTests
    {
        private static EntityRepository NewWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<ChangeCheckPos>();
            repo.RegisterComponent<ChangeCheckChurn>();
            return repo;
        }

        /// <summary>⭐ Success condition 3 — the keyframe's header (tick, wall clock) is not world content: two captures of an identical world at
        /// different ticks and wall clocks hash equal, while their BYTES differ (so the exclusion is what makes them equal).
        /// 🔴 Mutation: <c>HashOf</c> hashing the whole buffer (skipping nothing) ⇒ the hash assertion fails.</summary>
        [Fact]
        public void TheHash_IgnoresTheKeyframeHeader_TwoCapturesOfOneWorldAtDifferentTicksAndWallClocksHashEqual()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            repo.Tick();

            var a = WorldKeyframes.Capture(repo, WorldChangeCheck.DefaultOptions, wallClockTicks: 111);
            for (int i = 0; i < 25; i++) repo.Tick();                 // the tick moves; the content does not
            var b = WorldKeyframes.Capture(repo, WorldChangeCheck.DefaultOptions, wallClockTicks: 999_999);

            Assert.NotEqual(a.Tick, b.Tick);
            Assert.NotEqual(a.Bytes, b.Bytes);                         // the header differs …
            Assert.Equal(WorldChangeCheck.HashOf(a), WorldChangeCheck.HashOf(b));   // … the content hash does not
        }

        /// <summary>⭐ Success condition 2 — an edit moves the hash; re-baselining (what a completed save does) makes the world unchanged again.
        /// 🔴 Mutation: <c>HasChanged</c> returning false ⇒ the first assertion fails; returning <c>true</c> always ⇒ the last fails.</summary>
        [Fact]
        public void AnEdit_IsChanged_AndARebaselineMakesItUnchangedAgain()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            var check = new WorldChangeCheck();
            check.TakeBaseline(repo);
            for (int i = 0; i < 10; i++) repo.Tick();
            Assert.False(check.HasChanged(repo), "ticking is not a change");

            repo.SetComponent(e, new ChangeCheckPos { X = 2f });       // the user moves a unit
            Assert.True(check.HasChanged(repo));

            check.TakeBaseline(repo);                                   // the save round completed
            Assert.False(check.HasChanged(repo));

            repo.SetComponent(e, new ChangeCheckPos { X = 1f });       // and back
            Assert.True(check.HasChanged(repo), "any difference from the saved world counts, even the original value");
        }

        /// <summary>Adding and removing an entity are changes too (the structure is in the keyframe).</summary>
        [Fact]
        public void AddingOrDestroyingAnEntity_IsChanged()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            var check = new WorldChangeCheck();
            check.TakeBaseline(repo);

            var extra = repo.CreateEntity(); repo.SetComponent(extra, new ChangeCheckPos { X = 5f });
            Assert.True(check.HasChanged(repo));
            repo.DestroyEntity(extra);
            repo.DestroyEntity(e);
            Assert.True(check.HasChanged(repo));
        }

        /// <summary>No baseline ⇒ nothing says the world is saved: <c>HasChanged</c> is true, and <c>HasBaseline</c> tells the two apart.</summary>
        [Fact]
        public void WithNoBaseline_HasChangedIsTrue_AndHasBaselineSaysWhy()
        {
            using var repo = NewWorld();
            var check = new WorldChangeCheck();
            Assert.False(check.HasBaseline);
            Assert.True(check.HasChanged(repo));
            check.TakeBaseline(repo);
            Assert.True(check.HasBaseline);
            Assert.False(check.HasChanged(repo));
            check.ClearBaseline();
            Assert.False(check.HasBaseline);
            Assert.True(check.HasChanged(repo));
        }

        /// <summary>⭐ A type that is not world state says so BY ITS OWN FLAG (<c>[DataPolicy(Transient)]</c>): its churn while paused never makes a quiet
        /// world "changed" — no second list anywhere. 🔴 Mutation: <c>DefaultOptions</c> = <c>CaptureOptions.EditorDryRun</c> (every type) ⇒ this fails.</summary>
        [Fact]
        public void ATransientTypesChurn_IsNotAChange_ButARealComponentsEditIs()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity();
            repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            repo.SetComponent(e, new ChangeCheckChurn { Counter = 0 });
            var check = new WorldChangeCheck();
            check.TakeBaseline(repo);

            for (int i = 1; i <= 20; i++)
            {
                repo.Tick();
                repo.SetComponent(e, new ChangeCheckChurn { Counter = i });   // a system writing while paused
            }
            Assert.False(check.HasChanged(repo));

            repo.SetComponent(e, new ChangeCheckPos { X = 9f });
            Assert.True(check.HasChanged(repo));
        }

        /// <summary>
        /// ⭐⭐ The measured noise (uc-urban-battle, a real editor): SELECTING a unit adds its runtime-only <c>SelectionState</c> — and that stamps the
        /// entity's <c>LastChangeTick</c> in the keyframe's entity-index cold chunk, which read as a changed world. A runtime-only component added to,
        /// written on or removed from an entity (its type excluded by its own flag) is NOT a change. 🔴 Mutation: <c>HashOf</c> not skipping
        /// <c>LastChangeTick</c> ⇒ this fails.
        /// </summary>
        [Fact]
        public void TouchingARuntimeOnlyComponent_StampsTheEntitysLastChangeTick_ButIsNotAChange()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            var other = repo.CreateEntity(); repo.SetComponent(other, new ChangeCheckPos { X = 2f });
            repo.Tick();
            var check = new WorldChangeCheck();
            var before = WorldKeyframes.Capture(repo, WorldChangeCheck.DefaultOptions);
            check.TakeBaseline(repo);

            for (int i = 0; i < 3; i++) repo.Tick();
            repo.SetComponent(e, new ChangeCheckChurn { Counter = 1 });       // adds a Transient component (what a selection does)
            repo.SetComponent(e, new ChangeCheckChurn { Counter = 2 });
            repo.RemoveComponent<ChangeCheckChurn>(e);
            repo.SetComponent(e, new ChangeCheckChurn { Counter = 3 });

            var after = WorldKeyframes.Capture(repo, WorldChangeCheck.DefaultOptions);
            Assert.NotEqual(before.Bytes.AsSpan(21).ToArray(), after.Bytes.AsSpan(21).ToArray());   // the BYTES moved (the stamped tick) …
            Assert.False(check.HasChanged(repo));                                                    // … the content did not
        }

        // ── settling: a world just loaded still gets its first frame of initial work (measured on uc-urban-battle, 9 types, once) ────────────

        /// <summary>
        /// ⭐⭐ A load starts a SETTLING: no baseline (the node answers "no-baseline") until the world has stopped moving — the baseline is the first frame
        /// whose content equals the previous frame's. A world that moves in its first frame after the load (the simulation's one-time initialisation) is
        /// therefore NOT baselined early. 🔴 Mutations: <c>SettleStep</c> baselining at its first call ⇒ the "still moving" assertions fail; never
        /// baselining ⇒ the last assertion fails.
        /// </summary>
        [Fact]
        public void ASettling_BaselinesTheFirstWorldThatStopsMoving()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            var check = new WorldChangeCheck();
            Assert.False(check.SettleStep(repo));                    // nothing settling ⇒ a no-op
            check.TakeBaseline(repo);
            check.BeginSettling();                                    // a load: the old baseline is dropped
            Assert.True(check.IsSettling);
            Assert.False(check.HasBaseline);

            Assert.False(check.SettleStep(repo));                    // frame 1: the first hash
            repo.SetComponent(e, new ChangeCheckPos { X = 2f });     // the first frame's one-time initialisation moved the world
            Assert.False(check.SettleStep(repo));                    // frame 2: moved ⇒ still settling
            Assert.False(check.HasBaseline);
            Assert.True(check.SettleStep(repo));                     // frame 3: the same as frame 2 ⇒ THIS is the baseline
            Assert.True(check.HasBaseline);
            Assert.False(check.IsSettling);
            Assert.False(check.GaveUpSettling);
            Assert.Equal(3, check.SettledAfterFrames);
            Assert.False(check.HasChanged(repo));                    // the initialised world is the baseline …
            repo.SetComponent(e, new ChangeCheckPos { X = 3f });
            Assert.True(check.HasChanged(repo));                     // … and an edit after it counts
        }

        /// <summary>A world that never stops moving does not wait forever: after the cap the baseline is taken anyway, and says so.</summary>
        [Fact]
        public void ASettling_GivesUpAtTheCap_AndSaysSo()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 0f });
            var check = new WorldChangeCheck();
            check.BeginSettling();
            int frames = 0;
            while (!check.SettleStep(repo, maxFrames: 5)) { frames++; repo.SetComponent(e, new ChangeCheckPos { X = frames }); }

            Assert.True(check.HasBaseline);
            Assert.True(check.GaveUpSettling);
            Assert.Equal(5, check.SettledAfterFrames);
        }

        /// <summary>Clearing the baseline (the node went to Idle) cancels a settling in progress.</summary>
        [Fact]
        public void ClearingTheBaseline_CancelsASettling()
        {
            using var repo = NewWorld();
            var check = new WorldChangeCheck();
            check.BeginSettling();
            check.SettleStep(repo);
            check.ClearBaseline();

            Assert.False(check.IsSettling);
            Assert.False(check.SettleStep(repo));
            Assert.False(check.HasBaseline);
        }

        // ── the node's answer: Orchestration.Handlers.WorldChangeCheckHandler (CE-4093, success condition 4) ─────────────────────

        /// <summary>⛔ PERMANENT WIRE VALUE (R-42): the next free value after <c>FinalizeReview = 34</c>, in BOTH enums (the mirror rail
        /// <c>FdpOrchestrationEnumSyncTests</c> keeps them equal and pins the NED side too).</summary>
        [Fact]
        public void CheckUnsaved_IsThePermanentWireValue35()
        {
            Assert.Equal(35, (int)NodeOpType.CheckUnsaved);
        }

        private static ExecuteNodeOpIntent Check() => new() { Operation = NodeOpType.CheckUnsaved, TransactionId = Guid.NewGuid() };

        /// <summary>⭐ Success condition 4 — the handler answers "no-baseline" before one was taken, "unchanged" for a quiet world, "changed" after an edit, and
        /// "unchanged" again after a re-baseline; it claims <c>CheckUnsaved</c> and nothing else; it mutates nothing.
        /// 🔴 Mutations: the handler ignoring <c>HasBaseline</c> ⇒ the first assertion fails; answering <c>Unchanged</c> always ⇒ the edit assertion fails.</summary>
        [Fact]
        public async System.Threading.Tasks.Task TheHandler_AnswersNoBaseline_Unchanged_Changed()
        {
            using var repo = NewWorld();
            var e = repo.CreateEntity(); repo.SetComponent(e, new ChangeCheckPos { X = 1f });
            var check = new WorldChangeCheck();
            var handler = new WorldChangeCheckHandler(check, repo);

            Assert.True(handler.CanHandle(NodeOpType.CheckUnsaved));
            Assert.False(handler.CanHandle(NodeOpType.SerializeLocal));
            Assert.False(handler.CanHandle(NodeOpType.PrepareEdit));

            Assert.Equal("no-baseline", await handler.PrepareAsync(Check(), default));
            check.TakeBaseline(repo);
            repo.Tick();
            Assert.Equal("unchanged", await handler.PrepareAsync(Check(), default));
            repo.SetComponent(e, new ChangeCheckPos { X = 2f });
            Assert.Equal("changed", await handler.PrepareAsync(Check(), default));
            check.TakeBaseline(repo);
            Assert.Equal("unchanged", await handler.PrepareAsync(Check(), default));
        }

        /// <summary>The answer survives the wire as the master's aggregator reads it: the plain word or its JSON string; a silent or unknown answer reads as
        /// CHANGED (only an explicit "unchanged" means saved).</summary>
        [Theory]
        [InlineData("changed", "changed", true)]
        [InlineData("\"changed\"", "changed", true)]
        [InlineData("\"unchanged\"", "unchanged", false)]
        [InlineData("unchanged", "unchanged", false)]
        [InlineData("\"no-baseline\"", "no-baseline", true)]
        [InlineData("", null, true)]
        [InlineData(null, null, true)]
        [InlineData("\"wat\"", null, true)]
        public void TheResponse_ParsesTheWayTheWireCarriesIt_AndOnlyUnchangedMeansSaved(string? json, string? word, bool meansChanged)
        {
            Assert.Equal(word, WorldChangeCheckHandler.Parse(json));
            Assert.Equal(meansChanged, WorldChangeCheckHandler.MeansChanged(json));
        }

        /// <summary>The save wrapper forwards the save untouched and re-baselines at the node's commit; it refuses to wrap a handler whose protocol it would drop.
        /// 🔴 Mutation: <c>Commit</c> not taking the baseline ⇒ the baseline assertion fails.</summary>
        [Fact]
        public void TheSaveWrapper_ForwardsEverything_AndRebaselinesAtCommit_AndRefusesWhatItWouldDrop()
        {
            using var repo = NewWorld();
            repo.SetComponent(repo.CreateEntity(), new ChangeCheckPos { X = 1f });
            var check = new WorldChangeCheck();
            var inner = new Save();
            var wrapper = new BaselineAfterSaveHandler(inner, check, repo);
            var intent = new ExecuteNodeOpIntent { Operation = NodeOpType.SerializeLocal, TransactionId = Guid.NewGuid() };

            Assert.True(wrapper.CanHandle(NodeOpType.SerializeLocal));
            Assert.True(wrapper.CanHandle(intent));
            Assert.Same(inner, wrapper.Inner);
            Assert.False(check.HasBaseline);

            wrapper.PrepareAsync(intent, default).GetAwaiter().GetResult();
            Assert.False(check.HasBaseline, "writing the slice is not yet the node's completed part");
            wrapper.Commit(intent, null);

            Assert.True(inner.Prepared && inner.Committed);
            Assert.True(check.HasBaseline);
            Assert.False(check.HasChanged(repo));

            Assert.Throws<ArgumentException>(() => new BaselineAfterSaveHandler(new SaveThatObserves(), check, repo));
        }

        private class Save : IClusterStateHandler
        {
            public bool Prepared, Committed;
            public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;
            public System.Threading.Tasks.Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, System.Threading.CancellationToken ct) { Prepared = true; return System.Threading.Tasks.Task.FromResult<object?>(null); }
            public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) => Committed = true;
            public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
        }

        private sealed class SaveThatObserves : Save, IIdleResetHandler { public void OnEnteredIdle() { } }

        /// <summary>A frame the walker cannot parse is refused loudly (a layout change must redden this suite), never hashed into a confident wrong answer.</summary>
        [Fact]
        public void AKeyframeShorterThanItsHeader_IsRefused()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => WorldChangeCheck.HashOf(WorldKeyframe.FromRecordedFrame(new byte[8])));
        }
    }
}
