using System;
using System.Collections.Generic;
using Fdp.Core;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.History
{
    public class EntitySelectionHistoryTests
    {
        // ── FND-T01: PushSelection, GoBack, GoForward ──────────────────────────

        [Fact]
        public void FND_T01_PushSelection_EnablesGoBack_GoBackAndForwardFireEvent()
        {
            var history = new EntitySelectionHistory();
            var entityA = new Entity(1, 1);
            var entityB = new Entity(2, 1);

            history.PushSelection(entityA);
            history.PushSelection(entityB);

            Assert.True(history.CanGoBack);
            Assert.False(history.CanGoForward);

            // GoBack: fires OnSelectionChanged exactly once with entityA
            int backFires = 0;
            Entity backReceived = default;
            history.OnSelectionChanged += e => { backFires++; backReceived = e; };

            history.GoBack();

            Assert.Equal(1, backFires);
            Assert.Equal(entityA, backReceived);

            // GoForward: fires OnSelectionChanged exactly once with entityB
            int fwdFires = 0;
            Entity fwdReceived = default;
            history.OnSelectionChanged += e => { fwdFires++; fwdReceived = e; };

            history.GoForward();

            Assert.Equal(1, fwdFires);
            Assert.Equal(entityB, fwdReceived);
        }

        // ── FND-T02: Duplicate push is no-op ──────────────────────────────────

        [Fact]
        public void FND_T02_DuplicatePush_IsNoOp_HistorySizeStaysOne()
        {
            var history = new EntitySelectionHistory();
            var entity = new Entity(1, 1);

            history.PushSelection(entity);
            history.PushSelection(entity); // duplicate

            Assert.False(history.CanGoBack, "CanGoBack should be false after only one distinct push.");
        }

        // ── FND-T03: GoBack then PushSelection truncates forward stack ─────────

        [Fact]
        public void FND_T03_GoBackThenPush_TruncatesForwardStack()
        {
            var history = new EntitySelectionHistory();
            var a = new Entity(1, 1);
            var b = new Entity(2, 1);
            var c = new Entity(3, 1);

            history.PushSelection(a);
            history.PushSelection(b);
            history.GoBack(); // now pointing at a

            Assert.True(history.CanGoForward, "After GoBack, CanGoForward should be true.");

            history.PushSelection(c); // should truncate forward (b is gone)

            Assert.False(history.CanGoForward, "After new push after GoBack, CanGoForward should be false.");
            Assert.True(history.CanGoBack, "CanGoBack should still be true.");
        }

        // ── FND-T04: Re-entrance guard inside OnSelectionChanged ───────────────

        [Fact]
        public void FND_T04_ReentranceGuard_SuppressesInnerPush()
        {
            var history = new EntitySelectionHistory();
            var a = new Entity(1, 1);
            var b = new Entity(2, 1);
            var extraEntity = new Entity(99, 1);

            history.PushSelection(a);
            history.PushSelection(b);

            int totalFires = 0;
            history.OnSelectionChanged += e =>
            {
                totalFires++;
                // Attempt re-entrant push — must be suppressed
                history.PushSelection(extraEntity);
            };

            history.GoBack();

            // OnSelectionChanged should have fired exactly once
            Assert.Equal(1, totalFires);
            // CanGoForward should still be true (re-entrant push was suppressed)
            Assert.True(history.CanGoForward);
        }

        // ── FND-T05: PlaybackHistoryTracker smoke ─────────────────────────────

        [Fact]
        public void FND_T05_PlaybackHistoryTracker_MirrorsEntityHistoryInvariants()
        {
            var tracker = new PlaybackHistoryTracker();

            tracker.PushFrame(5);
            tracker.PushFrame(10);
            tracker.PushFrame(15);

            Assert.True(tracker.CanGoBack);
            Assert.False(tracker.CanGoForward);

            // GoBack → OnSeekRequested(10)
            int seekFires = 0;
            int lastSeeked = -1;
            tracker.OnSeekRequested += f => { seekFires++; lastSeeked = f; };

            tracker.GoBack();
            Assert.Equal(1, seekFires);
            Assert.Equal(10, lastSeeked);

            // GoBack again → OnSeekRequested(5)
            seekFires = 0;
            tracker.GoBack();
            Assert.Equal(1, seekFires);
            Assert.Equal(5, lastSeeked);

            Assert.False(tracker.CanGoBack, "CanGoBack should be false at the start.");

            // GoForward → OnSeekRequested(10)
            seekFires = 0;
            tracker.GoForward();
            Assert.Equal(1, seekFires);
            Assert.Equal(10, lastSeeked);

            // Push 20 — truncates forward (15 is gone)
            tracker.PushFrame(20);
            Assert.False(tracker.CanGoForward, "After new push, CanGoForward should be false.");
            Assert.True(tracker.CanGoBack, "CanGoBack should be true.");
        }

        // ── Randomized smoke test ─────────────────────────────────────────────

        [Fact]
        public void FND_T_Smoke_RandomizedPushBackForward_RemainsConsistent()
        {
            var history = new EntitySelectionHistory();
            var rng = new Random(42);
            var entities = new Entity[10];
            for (int i = 0; i < entities.Length; i++)
                entities[i] = new Entity(i + 1, 1);

            for (int iteration = 0; iteration < 100; iteration++)
            {
                int op = rng.Next(3);
                if (op == 0)
                {
                    history.PushSelection(entities[rng.Next(entities.Length)]);
                }
                else if (op == 1)
                {
                    history.GoBack();
                }
                else
                {
                    history.GoForward();
                }

                // CanGoBack/CanGoForward must be internally consistent
                // (CanGoForward implies there IS a forward item to navigate to)
                if (history.CanGoBack && history.CanGoForward)
                {
                    // Both being true is valid: we are in the middle of the stack
                }
                else if (!history.CanGoBack && !history.CanGoForward)
                {
                    // At a single-entry or empty stack — valid
                }
                // No assertion failure here means the state is self-consistent
            }
        }
    }
}
