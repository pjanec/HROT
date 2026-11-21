using System;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐ <c>CE-3082</c> D2 — an HSM state's EMPTY OnExit runs its C# activity's <c>[BTreeDeactivator]</c>, bound to the
/// activity's own expression / working-state fields. The BTree host calls a leaf's deactivator when a branch is left
/// (it stops what the node started and resets its working state); the HSM host never did, so a posture switched away from
/// kept moving, firing and holding its sensors. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3c D2.
/// <para>
/// ⭐ A DTO rewrite in front of an UNCHANGED emitter: the filled OnExit is an ordinary binding, so the namer, the thunk
/// collector (<see cref="SharedAiBindings"/>) and <see cref="HsmEmitCore"/> need no new arm.
/// ⭐ Fill-an-empty-slot, as CE-388: an AUTHORED OnExit always wins. A blueprint activity is left alone (its own
/// CE-388 channel-release default applies).
/// </para>
/// </summary>
public static class HsmDeactivatorExits
{
    /// <summary>Fills every state's empty OnExit from <paramref name="deactivatorOf"/> (activity FQN → its deactivator's
    /// FQN, or null). Returns how many states were filled.</summary>
    public static int Fill(HsmAssetDto dto, Func<string, string?> deactivatorOf)
    {
        int filled = 0;
        foreach (var s in dto.States)
        {
            var activity = s.Activity;
            if (activity == null || string.IsNullOrEmpty(activity.MethodFqn) || activity.BlueprintAssetId != Guid.Empty) continue;
            if (s.OnExit != null && !s.OnExit.IsEmpty) continue;
            string? deactivator = deactivatorOf(activity.MethodFqn!);
            if (deactivator == null) continue;
            s.OnExit = new BehaviorActionBindingDto
            {
                MethodFqn               = deactivator,
                ExpressionTargetField   = activity.ExpressionTargetField,
                WorkingStateTypeId      = activity.WorkingStateTypeId,
                WorkingStateTargetField = activity.WorkingStateTargetField,
            };
            filled++;
        }
        return filled;
    }
}
