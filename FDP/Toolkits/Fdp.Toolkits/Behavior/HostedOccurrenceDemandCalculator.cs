using System;
using System.Collections.Generic;
using Fdp.Toolkit.Blueprints;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>O7b-3</c> — THE RUNTIME JOIN: which blueprints does this HSM host, and how much store
/// will their working states need?</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §27.7.
///
/// <para>🔒 <b>Why the join is HERE and not in the HSM emitter</b> (user ruling, <c>2026-09-21</c>:
/// <i>"would that mean the hsm code will need to know about blueprint catalogs? Not good."</i>). ⭐ The
/// bridge between the two already exists and costs nothing to read: an HSM action id <b>IS</b> the
/// blueprint id truncated to 16 bits — <c>CSharpEmitter.cs:383</c> emits
/// <c>RegisterAction(unchecked((ushort)BlueprintId), …)</c>. ⇒ the machine's own
/// <see cref="StateDef"/>s already name their blueprints; nothing new has to be emitted, and
/// <c>HsmBridgeEmitCore</c> stays ignorant of blueprints.</para>
///
/// <para>⭐⭐ <b>And the truncation is SAFE, measured <c>2026-09-21</c>:</b> across <b>85</b> distinct
/// blueprint assets there are <b>85</b> distinct low-16 values — zero collisions — and
/// <c>HsmDispatcherIdAnalyzer</c> raises <c>BHU020_DuplicateDispatcherId</c> on a collision, so
/// <b>within one compilation</b> it is a BUILD ERROR rather than a silent alias.</para>
///
/// <para>🔴🔴 <b>But NOT across assemblies, and that is filed as <c>CE-299</c>.</b>
/// <c>HsmActionDispatcher</c>'s tables are <c>private static readonly Dictionary&lt;ushort, IntPtr&gt;</c>
/// — <b>process-global</b> — and <c>RegisterAction</c> is a plain indexer assignment ⇒ <b>silent
/// last-writer-wins</b>. ⚠ <b>For SIZING that is harmless</b>: <see cref="BuildActionIdIndex"/> takes
/// the LARGER of a colliding pair, so the store stays big enough either way. ⛔ The hazard is the
/// DISPATCH — the wrong thunk runs — and that is <c>CE-299</c>'s, not this type's.</para>
///
/// <para>⚠ <b>What this CANNOT know, stated plainly.</b> The occurrence KEY needs the region slot the
/// kernel picks at runtime, so this computes a SIZE and never a manifest. ⇒ the slots still attach
/// lazily (§24.8) — this only guarantees there is room when they do.</para>
/// </summary>
public static class HostedOccurrenceDemandCalculator
{
    /// <summary>
    /// The demand for one behaviour, or <see langword="null"/> when it cannot host anything.
    ///
    /// <para>⛔ <see langword="null"/> is returned for a behaviour with no HSM definition. ⚠ A machine
    /// that hosts nothing returns a <c>SlotCount: 0</c> demand instead — the two are deliberately
    /// different, and <see cref="HostedOccurrenceDemand"/> says why.</para>
    /// </summary>
    /// <param name="blueprints">
    /// Every registered blueprint as <c>(id, definition)</c> — <c>BlueprintRegistry.GetAll()</c> or a
    /// staging buffer's contents. ⭐ Only <c>AiPrimitive</c>-dispatch entries can be hosted; the others
    /// are ignored rather than mis-counted.
    /// </param>
    public static HostedOccurrenceDemand? For(
        BehaviorDefinition definition,
        IEnumerable<KeyValuePair<int, BlueprintDefinition>> blueprints)
    {
        if (blueprints is null) throw new ArgumentNullException(nameof(blueprints));
        return For(definition, BuildActionIdIndex(blueprints));
    }

    /// <summary>
    /// ⭐ The same, against an index built ONCE for a whole scan — <c>O(behaviours + blueprints)</c>
    /// instead of <c>O(behaviours × blueprints)</c>. ⚠ Both overloads share one body so the two
    /// callers cannot drift.
    /// </summary>
    public static HostedOccurrenceDemand? For(
        BehaviorDefinition definition,
        IReadOnlyDictionary<ushort, int> sizeByActionId)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (sizeByActionId is null) throw new ArgumentNullException(nameof(sizeByActionId));

        var blob = definition.HsmDefinition;
        if (blob is null) return null;

        if (sizeByActionId.Count == 0) return new HostedOccurrenceDemand(0, 0);

        // ⭐ Distinct (state, blueprint) pairs — that is exactly what makes two distinct slot KEYS,
        //   because the region a state runs in is fixed by the machine's topology.
        var seen = new HashSet<(int State, ushort ActionId)>();
        var payloads = new List<int>();

        void Count(int stateIndex, ushort actionId)
        {
            if (actionId == 0) return;                                   // 0 = none, per StateDef
            if (!sizeByActionId.TryGetValue(actionId, out int size)) return;  // not a hosted blueprint
            if (!seen.Add((stateIndex, actionId))) return;               // already counted
            payloads.Add(size);
        }

        var states = blob.States;
        for (int s = 0; s < states.Length; s++)
        {
            ref readonly var state = ref blob.GetState(s);
            Count(s, state.OnEntryActionId);
            Count(s, state.OnExitActionId);
            Count(s, state.ActivityActionId);
            Count(s, state.TimerActionId);

            // Per-region transitions dispatch against the SOURCE state (HsmKernelCore:594/:727), so
            // their occurrence is keyed by that state — countable exactly.
            if (state.FirstTransitionIndex == 0xFFFF) continue;
            for (int t = 0; t < state.TransitionCount; t++)
            {
                ref readonly var trans = ref blob.GetTransition(state.FirstTransitionIndex + t);
                Count(s, trans.GuardId);
                Count(s, trans.ActionId);
            }
        }

        // ⚠ GLOBAL transitions are the one inexact arm, and it is worth being explicit rather than
        //   inventing a bound. HsmKernelCore:551 evaluates a global guard against `activeLeafIds[0]`
        //   — whatever region 0's leaf happens to be — so its occurrence key varies with the machine's
        //   current state. ⛔ Counting it per state would multiply the demand by StateCount and push
        //   every machine with one global transition to the largest tier; ⭐ counting it ONCE covers
        //   the common case (a global guard that fires from a settled state) and leaves the existing
        //   loud "no room" as the backstop for the rest.
        var globals = blob.GlobalTransitions;
        for (int g = 0; g < globals.Length; g++)
        {
            ref readonly var gt = ref blob.GetGlobalTransition(g);
            Count(stateIndex: -1, gt.GuardId);
            Count(stateIndex: -1, gt.ActionId);
        }

        return HostedOccurrenceDemand.Of(payloads);
    }

    /// <summary>
    /// ⭐ The <c>ushort</c> action id → hosted working-state size index, built from the blueprint
    /// registry. ⚠ Built per call: this runs once per behaviour at REGISTRATION, never per frame.
    ///
    /// <para>⛔ A duplicate low-16 is a build error upstream (<c>BHU020</c>) <b>within one
    /// compilation</b>; ⚠ <b>across assemblies it is NOT caught</b> — <c>CE-299</c>. ⭐ Taking the
    /// LARGER of the two is therefore a real defence, not defensive decoration: the store stays big
    /// enough for whichever thunk actually wins the dispatcher table.</para>
    /// </summary>
    public static Dictionary<ushort, int> BuildActionIdIndex(
        IEnumerable<KeyValuePair<int, BlueprintDefinition>> blueprints)
    {
        var index = new Dictionary<ushort, int>();
        foreach (var kv in blueprints)
        {
            var def = kv.Value;
            if (def is null || def.Kind != BlueprintDispatchKind.AiPrimitive) continue;

            ushort actionId = unchecked((ushort)kv.Key);
            if (actionId == 0) continue;               // 0 means "no action" in a StateDef

            index[actionId] = index.TryGetValue(actionId, out int existing)
                ? Math.Max(existing, def.StateSize)
                : def.StateSize;
        }
        return index;
    }
}
