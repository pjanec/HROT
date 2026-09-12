using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Diagnostics.Gizmos.Systems;
using Xunit;

namespace Fdp.Toolkits.Tests.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b>Rails for <see cref="GizmoFocusRegistry"/> — the ONE exclusive-focus slot.</b>
    /// 📄 <c>docs/designs/gizmos-1/gizmo-input-focus-design.md</c> §6.2b · ruling <c>R-144</c> ·
    /// 📄 <c>docs/blueprints/Architect_Question_68_Gizmo_Focus_Registry.md</c> §6.
    ///
    /// <para>⚠ <b>Why a NEW class rather than folding into the arbiters' suites</b> (<c>R-142</c> ④): the
    /// thing under test is the slot SHARED BY BOTH arbiters, so it belongs to neither
    /// <c>GlobalGizmoManagerTests</c> nor <c>DataDrivenGizmoSystemBindingTests</c>. The existing suites
    /// keep their own claims and stayed green through this change (190/190, unchanged).</para>
    ///
    /// <para>🔴 <b>What these pin that NOTHING could before:</b> *"at most one exclusive focus per
    /// subsystem"* across BOTH arbiters. Until this registry existed the two held independent
    /// <c>_focusedGizmo</c> fields, and the invariant was true only while every arm went through
    /// <c>ToolController.CancelOtherArbiter</c> — a convention, not a construction.</para>
    /// </summary>
    public sealed class GizmoFocusRegistryTests
    {
        private sealed class Probe : IEntityStatefulGizmo
        {
            public bool RequiresExclusiveFocus { get; init; } = true;
            public bool WantsRawInput          { get; init; }
            public bool IsFocused { get; private set; }
            public bool Disposed  { get; private set; }
            public int  FocusChanges { get; private set; }

            public void SetFocus(bool f) { IsFocused = f; FocusChanges++; }
            public void UpdateAndDraw(ISimulationView v, float dt, IDebugDrawBuilder b) { }
            public void OnInteractionStarted(GizmoPickToken t, Vector3 w) { }
            public void OnDragUpdate(Vector3 p) { }
            public void OnCommit(Vector3 w) { }
            public void OnMenuAction(int id) { }
            public void OnMouseEvent(MapMouseButton b, bool p, Vector3 w) { }
            public void OnKeyEvent(MapKeyboardKey k, bool p) { }
            public void OnCancel() { }
            public void Dispose() { Disposed = true; }
        }

        // Stand-ins for the two arbiters. The registry never calls into an owner — it only compares
        // references — so a plain object is a faithful stand-in and keeps these rails unit-sized.
        private static readonly object ArbiterA = new();
        private static readonly object ArbiterB = new();

        // ── The claim of 68-A ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>THE claim: the slot is exclusive ACROSS arbiters.</b> ⛔ This is what two private
        /// <c>_focusedGizmo</c> fields could not express at any cost — each guarded exclusivity only
        /// within itself, so two "exclusive" tools held focus at once and the terminal received two
        /// <c>InputCaptureBinding</c>s for one frame.
        /// </summary>
        [Fact]
        public void TheSlotIsExclusiveAcrossArbitersNotJustWithinOne()
        {
            var reg   = new GizmoFocusRegistry();
            var first = new Probe();
            var later = new Probe();

            Assert.True(reg.TryGrant(ArbiterA, first));
            Assert.True(first.IsFocused);

            // ⛔ A DIFFERENT arbiter asks. Before the registry this succeeded, because it was asking
            //    a different field.
            Assert.False(reg.TryGrant(ArbiterB, later));

            Assert.False(later.IsFocused);
            Assert.Same(first, reg.Holder);
        }

        /// <summary>⭐ A gizmo that wants neither exclusivity nor raw input never takes the slot.</summary>
        [Fact]
        public void AGizmoThatWantsNeitherExclusivityNorRawInputNeverTakesTheSlot()
        {
            var reg       = new GizmoFocusRegistry();
            var permanent = new Probe { RequiresExclusiveFocus = false, WantsRawInput = false };

            Assert.False(reg.TryGrant(ArbiterA, permanent));
            Assert.Null(reg.Holder);
            Assert.False(permanent.IsFocused);
        }

        /// <summary>⭐ <c>WantsRawInput</c> alone is enough — the predicate is an OR, in ~14 old sites.</summary>
        [Fact]
        public void WantsRawInputAloneIsEnoughToTakeTheSlot()
        {
            var reg = new GizmoFocusRegistry();
            var raw = new Probe { RequiresExclusiveFocus = false, WantsRawInput = true };

            Assert.True(reg.TryGrant(ArbiterA, raw));
            Assert.Same(raw, reg.Holder);
        }

        // ── Behaviour ③ — the STEAL ───────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ <c>GrantStealing</c> displaces the holder; <c>TryGrant</c> does not. ⛔ Kept as two members
        /// on purpose — a grant that silently steals is how one-slot arbiters go wrong, and this is the
        /// one focus behaviour with no <c>GlobalGizmoManager</c> counterpart.
        /// </summary>
        [Fact]
        public void GrantStealingDisplacesTheHolderWhereTryGrantRefuses()
        {
            var reg  = new GizmoFocusRegistry();
            var held = new Probe();
            var thief = new Probe();

            reg.TryGrant(ArbiterA, held);
            Assert.False(reg.TryGrant(ArbiterA, thief));   // refuses …
            Assert.Same(held, reg.Holder);

            reg.GrantStealing(ArbiterA, thief);            // … steals
            Assert.Same(thief, reg.Holder);
            Assert.False(held.IsFocused);
            Assert.True(thief.IsFocused);
            Assert.False(held.Disposed);                   // ⭐ displaced, NOT destroyed
        }

        /// <summary>⚠ Stealing focus you already hold must not churn <c>SetFocus</c>.</summary>
        [Fact]
        public void StealingFocusYouAlreadyHoldIsANoOp()
        {
            var reg   = new GizmoFocusRegistry();
            var gizmo = new Probe();

            reg.TryGrant(ArbiterA, gizmo);
            int changes = gizmo.FocusChanges;

            reg.GrantStealing(ArbiterA, gizmo);

            Assert.Equal(changes, gizmo.FocusChanges);
            Assert.Same(gizmo, reg.Holder);
        }

        // ── Behaviours ②④⑤⑥ — release / suspend / resume / cancel ────────────────────────────────

        /// <summary>⭐ Release is scoped to the actual holder, and is safe for a gizmo that never held it.</summary>
        [Fact]
        public void ReleaseOnlyAffectsTheActualHolder()
        {
            var reg      = new GizmoFocusRegistry();
            var holder   = new Probe();
            var stranger = new Probe();

            reg.TryGrant(ArbiterA, holder);

            Assert.False(reg.Release(stranger));
            Assert.Same(holder, reg.Holder);

            Assert.True(reg.Release(holder));
            Assert.Null(reg.Holder);
            Assert.False(reg.Release(holder));   // idempotent
        }

        /// <summary>
        /// ⭐⭐⭐ <c>Q27-F</c>: <b>suspend is <c>SetFocus(false)</c> WITHOUT the <c>Dispose()</c></b>.
        /// ⛔ <c>Assert.False(tool.Disposed)</c> is the whole difference between an interruption and a
        /// switch — it is what stops a picker destroying the half-drawn route underneath it.
        /// </summary>
        [Fact]
        public void SuspendYieldsTheSlotWithoutDestroyingTheToolAndResumeGivesItBack()
        {
            var reg  = new GizmoFocusRegistry();
            var tool = new Probe();

            reg.TryGrant(ArbiterA, tool);

            var suspended = reg.Suspend();

            Assert.Same(tool, suspended);
            Assert.False(tool.IsFocused);
            Assert.False(tool.Disposed);         // ⭐⭐ ALIVE
            Assert.Null(reg.Holder);             // … and the slot is free for the interrupter

            reg.Resume(ArbiterA, tool);

            Assert.True(tool.IsFocused);
            Assert.Same(tool, reg.Holder);
        }

        /// <summary>⚠ A resume displaces whatever grabbed the slot meanwhile — including another arbiter's.</summary>
        [Fact]
        public void ResumeDisplacesWhateverGrabbedTheSlotMeanwhile()
        {
            var reg      = new GizmoFocusRegistry();
            var original = new Probe();
            var squatter = new Probe();

            reg.TryGrant(ArbiterA, original);
            reg.Suspend();
            reg.TryGrant(ArbiterB, squatter);

            reg.Resume(ArbiterA, original);

            Assert.Same(original, reg.Holder);
            Assert.False(squatter.IsFocused);
            Assert.False(squatter.Disposed);     // ⛔ the registry never disposes — that is lifecycle
        }

        /// <summary>
        /// ⭐⭐ <c>TakeForCancel</c> empties the slot and hands the gizmo back — ⛔ and does NOT cancel or
        /// dispose it. Lifecycle belongs to the arbiter that registered it, which is the only one that
        /// knows the key to remove.
        /// </summary>
        [Fact]
        public void TakeForCancelEmptiesTheSlotButLeavesLifecycleToTheArbiter()
        {
            var reg   = new GizmoFocusRegistry();
            var gizmo = new Probe();

            reg.TryGrant(ArbiterA, gizmo);
            var taken = reg.TakeForCancel();

            Assert.Same(gizmo, taken);
            Assert.Null(reg.Holder);
            Assert.False(gizmo.IsFocused);
            Assert.False(gizmo.Disposed);
            Assert.Null(reg.TakeForCancel());    // empty slot
        }

        // ── Behaviour ⑨ — RecipientFor, and the hazard the shared slot creates ────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>THE ORDER IS LOAD-BEARING: the holder wins over the target lookup.</b>
        /// 🔴 The measured code is <c>_focusedGizmo ?? FindGizmo(...)</c>. A first draft of the ruling
        /// wrote that order backwards, which would have silently re-routed every drag, commit and cancel
        /// to whatever the token happened to resolve. This rail is why that is not a latent possibility.
        /// </summary>
        [Fact]
        public void RecipientForPrefersTheHolderOverTheTargetLookup()
        {
            var reg          = new GizmoFocusRegistry();
            var holder       = new Probe();
            var byTargetHit  = new Probe();

            reg.TryGrant(ArbiterA, holder);

            Assert.Same(holder, reg.RecipientFor(ArbiterA, () => byTargetHit));
        }

        /// <summary>
        /// ⭐⭐⭐ <b>NO DOUBLE DELIVERY — the holder is offered ONLY to the arbiter that granted it.</b>
        /// 🔴 This is the hazard the shared slot creates and the reason every member takes an owner: both
        /// arbiters run in <c>PostSimulation</c> over the SAME bus, so without this every mouse, key and
        /// drag event would reach one gizmo twice.
        /// </summary>
        [Fact]
        public void TheHolderIsOfferedOnlyToTheArbiterThatGrantedIt()
        {
            var reg    = new GizmoFocusRegistry();
            var holder = new Probe();

            reg.TryGrant(ArbiterA, holder);

            Assert.Same(holder, reg.RecipientFor(ArbiterA, null));
            Assert.Null(reg.RecipientFor(ArbiterB, null));
        }

        /// <summary>
        /// ⭐⭐ <b>A non-owning arbiter still runs its TARGET LOOKUP.</b> ⛔ Suppressing it would change
        /// the SPATIAL routing path — the one path §4c never compared between the two arbiters — and
        /// §6.2b scopes that out of this unit. ⭐ No double delivery results: a target lookup only ever
        /// resolves the arbiter's OWN gizmos, which the other never delivers to.
        /// </summary>
        [Fact]
        public void ANonOwningArbiterStillRunsItsTargetLookup()
        {
            var reg      = new GizmoFocusRegistry();
            var holder   = new Probe();
            var byTarget = new Probe();

            reg.TryGrant(ArbiterA, holder);

            Assert.Same(byTarget, reg.RecipientFor(ArbiterB, () => byTarget));
        }

        /// <summary>⭐ With an empty slot it is the target lookup alone — the entity-scoped arm.</summary>
        [Fact]
        public void WithAnEmptySlotItIsTheTargetLookupAlone()
        {
            var reg      = new GizmoFocusRegistry();
            var byTarget = new Probe();

            Assert.Same(byTarget, reg.RecipientFor(ArbiterA, () => byTarget));
            Assert.Null(reg.RecipientFor(ArbiterA, null));
        }

        // ── Behaviour ⑧ — binding emission ───────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐ Only the OWNING arbiter emits the holder's <c>InputCaptureBinding</c>. ⛔ Otherwise both
        /// would write one into the same buffer for one gizmo, and §6.3 says the terminal has no honest
        /// way to choose between two.
        /// </summary>
        [Fact]
        public void OnlyTheOwningArbiterEmitsTheHoldersCaptureBinding()
        {
            var reg      = new GizmoFocusRegistry();
            var holder   = new Probe();
            var bystander = new Probe();

            reg.TryGrant(ArbiterA, holder);

            Assert.True(reg.ShouldEmitBinding(ArbiterA, holder));
            Assert.False(reg.ShouldEmitBinding(ArbiterB, holder));
            Assert.False(reg.ShouldEmitBinding(ArbiterA, bystander));
        }
    }
}
