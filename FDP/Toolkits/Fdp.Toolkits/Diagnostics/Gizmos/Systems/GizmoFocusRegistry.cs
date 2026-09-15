using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Systems
{
    /// <summary>
    /// ⭐⭐⭐ <b>THE exclusive-focus slot — one instance shared by every arbiter in a subsystem.</b>
    /// 📄 <c>docs/designs/gizmos-1/gizmo-input-focus-design.md</c> §6.2b · ruling <c>R-144</c> ·
    /// 📄 <c>docs/blueprints/Architect_Question_68_Gizmo_Focus_Registry.md</c> §6.
    ///
    /// <para>🔒 <b>This is §6.2's <c>ActiveGlobalGizmo</c>, finally built.</b> The design specified one
    /// registry holding the slot; the contracts were ported into FDP and the arbiter was not, so FDP grew
    /// <b>two</b> independent <c>_focusedGizmo</c> fields — <see cref="GlobalGizmoManager"/> and
    /// <see cref="DataDrivenGizmoSystem"/> — that share a bus and a draw buffer but not the invariant.
    /// ⛔ *"At most one exclusive focus per subsystem"* was therefore true only by CONVENTION
    /// (<c>ToolController.CancelOtherArbiter</c> cancelling the other arbiter before arming), which holds
    /// only for tools that go through the controller. §6.2a records the measurement.</para>
    ///
    /// <para>⭐⭐ <b>ONE INSTANCE, not one class</b> (§6.2b ①). Giving each arbiter its own registry
    /// satisfies every signature here and <b>fixes nothing</b>. <c>MapInteractionPack</c> is the single
    /// production composition root of both arbiters and creates exactly one of these.</para>
    ///
    /// <para>⛔⛔ <b>Why every method takes an <c>owner</c></b> (§6.2b ②): both arbiters run in
    /// <c>PostSimulation</c> over the SAME bus, and each routes events to its own holder. Sharing the slot
    /// naively would make BOTH route to it ⇒ every mouse/key/drag event delivered <b>twice</b>. ⇒ the
    /// <b>slot</b> is shared, so exclusivity is true by construction, but <b>routing and binding emission
    /// stay with the OWNING arbiter</b>. The owner is an opaque token — the registry never calls into it.</para>
    ///
    /// <para>⚠ <b>No stack lives here.</b> Suspend/resume ordering and depth belong to
    /// <c>ToolController</c>, so the arbiters do not grow two half-copies of one stack.</para>
    ///
    /// <para>⛔ Not thread-safe, and deliberately so: every caller is a <c>PostSimulation</c> system on the
    /// host's own thread. Transient, never serialized, never replicated.</para>
    /// </summary>
    public sealed class GizmoFocusRegistry
    {
        private IEntityStatefulGizmo? _holder;
        private object?               _owner;

        /// <summary>The gizmo currently holding exclusive focus, or <c>null</c>.</summary>
        public IEntityStatefulGizmo? Holder => _holder;

        /// <summary>The arbiter that granted the current focus, or <c>null</c>. Diagnostics only.</summary>
        public object? Owner => _owner;

        /// <summary>
        /// ⭐ The predicate that decides whether a gizmo participates in focus at all, written ~14 times
        /// across the two arbiters before this existed.
        /// </summary>
        public static bool WantsFocus(IEntityStatefulGizmo gizmo)
            => gizmo.RequiresExclusiveFocus || gizmo.WantsRawInput;

        /// <summary>
        /// Behaviour ① — grant focus <b>only if the slot is empty</b> and the gizmo wants it.
        /// Returns <c>true</c> when this call granted it.
        /// </summary>
        public bool TryGrant(object owner, IEntityStatefulGizmo gizmo)
        {
            if (gizmo == null || !WantsFocus(gizmo)) return false;
            if (_holder != null) return false;

            _holder = gizmo;
            _owner  = owner;
            gizmo.SetFocus(true);
            return true;
        }

        /// <summary>
        /// ⭐⭐ Behaviour ③ — <b>STEAL</b>: displace whoever holds the slot and take it.
        ///
        /// <para>⚠ This is the one behaviour with no <see cref="GlobalGizmoManager"/> counterpart. It is
        /// <c>DataDrivenGizmoSystem</c>'s interaction-start rule: touching a gizmo gives it the input,
        /// whatever held it before. ⛔ Kept as its own member rather than folded into
        /// <see cref="TryGrant"/> — a grant that silently steals is how one-slot arbiters go wrong.</para>
        /// </summary>
        public void GrantStealing(object owner, IEntityStatefulGizmo gizmo)
        {
            if (gizmo == null || !WantsFocus(gizmo)) return;
            if (ReferenceEquals(_holder, gizmo)) return;

            _holder?.SetFocus(false);
            _holder = gizmo;
            _owner  = owner;
            gizmo.SetFocus(true);
        }

        /// <summary>
        /// Behaviour ② — release the slot <b>only if this gizmo holds it</b>. Returns <c>true</c> when it
        /// did. ⭐ Idempotent, and safe to call for a gizmo that never held focus.
        /// </summary>
        public bool Release(IEntityStatefulGizmo? gizmo)
        {
            if (gizmo == null || !ReferenceEquals(_holder, gizmo)) return false;

            gizmo.SetFocus(false);
            _holder = null;
            _owner  = null;
            return true;
        }

        /// <summary>
        /// ⭐⭐⭐ Behaviour ④ — <b><c>SetFocus(false)</c> WITHOUT the <c>Dispose()</c></b>, returning the
        /// suspended gizmo so the caller can resume exactly it.
        ///
        /// <para>🔒 <c>Q27-F</c>'s *"suspend = <c>SetFocus(false)</c> without the <c>Dispose()</c>"*. The
        /// gizmo stays registered with its arbiter, so <b>it keeps drawing</b> — a half-drawn route that
        /// vanishes while you pick a point and then reappears reads as a bug.</para>
        /// </summary>
        public IEntityStatefulGizmo? Suspend()
        {
            var suspended = _holder;
            if (suspended == null) return null;

            suspended.SetFocus(false);
            _holder = null;
            _owner  = null;
            return suspended;
        }

        /// <summary>
        /// Behaviour ⑤ — give the slot back to a previously suspended gizmo, displacing anything that
        /// grabbed it meanwhile.
        ///
        /// <para>⚠ The caller must check the gizmo is still REGISTERED with it: a suspended gizmo may have
        /// completed or been cancelled, and resuming a disposed one is worse than not resuming. That check
        /// needs the arbiter's own registry, which is why it is not here.</para>
        /// </summary>
        public void Resume(object owner, IEntityStatefulGizmo? gizmo)
        {
            if (gizmo == null) return;

            if (_holder != null && !ReferenceEquals(_holder, gizmo))
                _holder.SetFocus(false);

            _holder = gizmo;
            _owner  = owner;
            gizmo.SetFocus(true);
        }

        /// <summary>
        /// Behaviour ⑥ — hand the holder back and empty the slot, for a caller that is about to cancel and
        /// dispose it. ⭐ The precise inverse of <see cref="Suspend"/>, and what a stack POP needs.
        ///
        /// <para>⛔ The registry does NOT call <c>OnCancel</c> or <c>Dispose</c>: those are lifecycle, and
        /// lifecycle belongs to the arbiter that registered the gizmo.</para>
        /// </summary>
        public IEntityStatefulGizmo? TakeForCancel()
        {
            var gizmo = _holder;
            if (gizmo == null) return null;

            gizmo.SetFocus(false);
            _holder = null;
            _owner  = null;
            return gizmo;
        }

        /// <summary>
        /// Behaviour ⑧ — <c>true</c> when <paramref name="gizmo"/> is the holder, <paramref name="owner"/>
        /// granted it, and it actually wants a capture binding.
        ///
        /// <para>⛔ The owner check is what stops both arbiters emitting an
        /// <c>InputCaptureBinding</c> for one gizmo into one buffer.</para>
        /// </summary>
        public bool ShouldEmitBinding(object owner, IEntityStatefulGizmo gizmo)
            => ReferenceEquals(_holder, gizmo)
            && ReferenceEquals(_owner, owner)
            && WantsFocus(gizmo);

        /// <summary>
        /// ⭐⭐⭐ Behaviour ⑨ — <b>THE routing rule, stated once: the focus holder receives un-anchored
        /// input.</b>
        ///
        /// <para>⛔⛔ <b>This is NOT a fallback, and the order is load-bearing.</b> The measured code is
        /// <c>_focusedGizmo ?? FindGizmo(...)</c> — <b>holder FIRST</b>, target lookup second. Writing it
        /// the other way round silently re-routes every drag, commit and cancel.</para>
        ///
        /// <para>📐 <b>Why the name matters</b> (§4c): <c>GlobalGizmoManager.Execute</c> <b>never reads
        /// <c>evt.Token</c></b> — it delivers every raw event to the holder unconditionally — and
        /// <c>MakeInputCaptureBinding</c> never stamps a <c>GizmoTypeId</c>, so raw-input tokens carry
        /// <c>GizmoTypeId == 0</c> and a target lookup can essentially never match for them. ⇒ the two
        /// arbiters run the SAME policy; one simply has an extra entity-scoped arm. Deleting the *"fallback"*
        /// would not lose an edge case — it would delete raw mouse and keyboard input for entity-scoped
        /// gizmos outright.</para>
        ///
        /// <para>⚠⚠ <b>The holder is offered ONLY to its owner</b> — that is what stops one gizmo being
        /// delivered the same event twice. ⛔ <b>But a non-owning arbiter still runs its target lookup</b>,
        /// which is exactly what it does today. Suppressing that instead would change the SPATIAL routing
        /// path — the one path §4c never compared between the two arbiters — and §6.2b scopes it out.
        /// ⭐ No double delivery results: a target lookup only ever resolves the arbiter's OWN gizmos, and
        /// the other arbiter never delivers to those.</para>
        /// </summary>
        /// <param name="owner">The arbiter asking. Only the granting owner receives the holder.</param>
        /// <param name="resolveByTarget">
        /// The second arm — an entity/target lookup. Pass <c>null</c> for an arbiter that has none
        /// (<see cref="GlobalGizmoManager"/>), which is the same rule with nothing to fall through to.
        /// </param>
        public IEntityStatefulGizmo? RecipientFor(
            object owner, Func<IEntityStatefulGizmo?>? resolveByTarget)
        {
            if (_holder != null && ReferenceEquals(_owner, owner))
                return _holder;

            return resolveByTarget?.Invoke();
        }
    }
}
