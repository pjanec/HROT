using System;
using System.Collections.Generic;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Fake.Components;

namespace Hrot.MuscleCharacter.Animation.Fake.Diagnostics;

/// <summary>ANC-P1-10: JSON snapshot export for diagnostic/AAR integration.</summary>
public static class FakeAnimBackendSnapshotJson
{
    /// <summary>Serialize a FakeAnimBackendState to JSON with name resolution.</summary>
    public static string Serialize(FakeAnimBackendState state, Dictionary<int, string>? montageNames = null,
        Dictionary<uint, string>? markerNames = null)
    {
        montageNames ??= new();
        markerNames ??= new();

        var lines = new List<string>
        {
            "{",
            $"  \"generation\": {state.Generation},",
            $"  \"totalTicks\": {state.TotalTicks},",
            "  \"slots\": [",
        };

        for (int i = 0; i < 8; i++)
        {
            var slot = state.Slots[i];
            string montageResolved = montageNames.TryGetValue(slot.ActiveMontage.Hash, out var name)
                ? name
                : "unknown";

            lines.Add($"    {{");
            lines.Add($"      \"slotId\": {i},");
            lines.Add($"      \"isActive\": {(slot.IsActive != 0 ? "true" : "false")},");
            lines.Add($"      \"montageId\": {slot.ActiveMontage.Hash},");
            lines.Add($"      \"montageIdResolved\": \"{montageResolved}\",");
            lines.Add($"      \"elapsedSeconds\": {slot.ElapsedSeconds},");
            lines.Add($"      \"totalDurationSeconds\": {slot.TotalDurationSeconds},");
            lines.Add($"      \"blendWeight\": {slot.BlendWeight},");
            lines.Add($"      \"playRate\": {slot.PlayRate},");
            lines.Add($"      \"inBlendOutWindow\": {(slot.InBlendOutWindow != 0 ? "true" : "false")},");
            lines.Add($"      \"firedNotifyMask\": \"0x{slot.FiredNotifyMask:X16}\"");
            lines.Add(i < 7 ? "    }," : "    }");
        }

        lines.Add("  ],");
        lines.Add("  \"aim\": {");
        lines.Add($"    \"isActive\": {(state.Aim.IsActive != 0 ? "true" : "false")},");
        lines.Add($"    \"worldAimPoint\": {{\"x\": {state.Aim.WorldAimPoint.X}, \"y\": {state.Aim.WorldAimPoint.Y}, \"z\": {state.Aim.WorldAimPoint.Z}}},");
        lines.Add($"    \"targetWorldAimPoint\": {{\"x\": {state.Aim.TargetWorldAimPoint.X}, \"y\": {state.Aim.TargetWorldAimPoint.Y}, \"z\": {state.Aim.TargetWorldAimPoint.Z}}},");
        lines.Add($"    \"blendWeight\": {state.Aim.BlendWeight},");
        lines.Add($"    \"isReleasing\": {(state.Aim.IsReleasing != 0 ? "true" : "false")},");
        lines.Add($"    \"priority\": {state.Aim.Priority}");
        lines.Add("  },");
        lines.Add("  \"stance\": {");
        lines.Add($"    \"currentStance\": \"{state.Stance.CurrentStance}\",");
        lines.Add($"    \"targetStance\": \"{state.Stance.TargetStance}\",");
        lines.Add($"    \"isTransitioning\": {(state.Stance.IsTransitioning != 0 ? "true" : "false")},");
        lines.Add($"    \"transitionProgress\": {state.Stance.TransitionProgress},");
        lines.Add($"    \"transitionTotalSeconds\": {state.Stance.TransitionTotalSeconds}");
        lines.Add("  },");
        lines.Add("  \"locomotion\": {");
        lines.Add($"    \"horizontalSpeed\": {state.HorizontalSpeed},");
        lines.Add($"    \"localHorizontalVelocity\": {{\"x\": {state.LocalHorizontalVelocity.X}, \"y\": {state.LocalHorizontalVelocity.Y}}},");
        lines.Add($"    \"verticalVelocity\": {state.VerticalVelocity},");
        lines.Add($"    \"isGrounded\": {(state.IsGrounded != 0 ? "true" : "false")},");
        lines.Add($"    \"distanceSinceLastFootstep\": {state.DistanceSinceLastFootstep},");
        lines.Add($"    \"nextFootIndex\": {state.NextFootIndex}");
        lines.Add("  },");
        lines.Add("  \"metrics\": {");
        lines.Add($"    \"totalTicks\": {state.TotalTicks}");
        lines.Add("  },");
        lines.Add("  \"notifyBuffer\": [");

        for (int i = 0; i < state.PendingNotifyCount; i++)
        {
            var notifyEvent = state.PendingNotifies[i];
            string markerResolved = markerNames.TryGetValue(notifyEvent.MarkerHash, out var name)
                ? name
                : "unknown";

            lines.Add($"    {{");
            lines.Add($"      \"index\": {i},");
            lines.Add($"      \"kind\": \"{notifyEvent.Kind}\",");
            lines.Add($"      \"markerHash\": \"0x{notifyEvent.MarkerHash:X8}\",");
            lines.Add($"      \"markerHashResolved\": \"{markerResolved}\",");
            lines.Add($"      \"timeSeconds\": {notifyEvent.TimeSeconds},");
            lines.Add($"      \"payload\": {notifyEvent.PayloadUint}");
            lines.Add(i < state.PendingNotifyCount - 1 ? "    }," : "    }");
        }

        lines.Add("  ]");
        lines.Add("}");

        return string.Join("\n", lines);
    }
}
