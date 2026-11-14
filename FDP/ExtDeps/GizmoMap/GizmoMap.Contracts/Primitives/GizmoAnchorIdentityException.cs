using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b>Thrown when a gizmo primitive that NEEDS an identity is emitted without one.</b>
    /// 🔒 User ruling, <c>2026-09-11</c>: *"if the zero identity throws an exception on some suitable
    /// (central?) place where gizmos are processed so it is easy to catch the case soon after it happens
    /// in a new code."* 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.8.
    ///
    /// <para>⭐⭐ <b>Its own type on purpose:</b> the point of the ruling is that a new emitter's mistake
    /// be <i>easy to catch</i>, so it is catchable and greppable as itself rather than as one more
    /// <c>InvalidOperationException</c>. ⭐ It derives from <see cref="InvalidOperationException"/> so a
    /// host that already handles that keeps working.</para>
    ///
    /// <para>⭐ <b>Thrown from ONE place</b> — <c>DebugPrimitive.AssertHasIdentity</c>, called by all four
    /// emission funnels of the two primitive buffers. ⇒ every primitive of every frame passes it.</para>
    /// </summary>
    public sealed class GizmoAnchorIdentityException : InvalidOperationException
    {
        public GizmoAnchorIdentityException(string message) : base(message) { }
    }

    /// <summary>
    /// ⭐⭐ <b>The strictness switch for <see cref="DebugPrimitive.AssertHasIdentity"/>.</b>
    ///
    /// <para>⭐⭐⭐ <b>Default <c>TRUE</c> — it THROWS.</b> 🔒 That follows the user's standing ruling of
    /// <c>2026-09-04</c> on <c>FdpConfig.FailFastOnModuleException</c>: *"the fail fast should be on by
    /// default as we are still in a wild development phase, not even close to production."* ⭐ And the
    /// throw genuinely surfaces: <c>SystemScheduler.ExecuteSystem</c>'s <c>try/catch</c> is
    /// <b>commented out</b>, and fail-fast is on, so it propagates with its stack instead of being
    /// swallowed — which is the <c>CE-188</c> failure (<c>StatelessGizmoSystem</c> threw on every frame
    /// of every editor run and nothing failed) already fixed.</para>
    ///
    /// <para>⛔⛔ <b>Why a flag at all, rather than an unconditional throw.</b> This runs on every
    /// primitive of every frame, and a violation therefore throws every frame. ⚠ If a host in the field
    /// hits one it cannot fix on the spot, it needs a way to keep drawing — and the alternative to a
    /// documented switch is someone deleting the check. ⇒ set <c>FDP_GIZMO_IDENTITY_STRICT=0</c> (or
    /// <c>false</c>/<c>off</c>) at process start, or assign this property. ⭐ With it off the violation
    /// still fires a <c>Debug.Assert</c> in a debug build, so it is never silent in development.</para>
    ///
    /// <para>⚠⚠ <b>Why this lives HERE and not in <c>FdpConfig</c>:</b> <c>GizmoMap.Contracts</c> is
    /// deliberately self-contained — <i>"NO other ProjectReferences — this assembly is self-contained
    /// (BCL only)"</i> (its <c>.csproj</c>) — so it cannot see <c>Fdp.Core</c>. ⛔ It is the same env-var
    /// idiom as <c>FDP_FAIL_FAST</c>, not a second config system.</para>
    /// </summary>
    public static class GizmoIdentityEnforcement
    {
        public static bool Strict { get; set; } =
            Environment.GetEnvironmentVariable("FDP_GIZMO_IDENTITY_STRICT")
                is not ("0" or "false" or "FALSE" or "off" or "OFF");
    }
}
