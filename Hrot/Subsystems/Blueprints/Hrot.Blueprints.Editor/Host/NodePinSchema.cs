using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Canonical per-kind pin schema for Blueprint nodes.
/// <para>
/// Loaded <c>.bp.json</c> assets store <c>"Pins": []</c> (the compiler does not
/// require persisted pins).  This class resolves the authoritative pin list for a
/// given <see cref="Node"/> instance so the editor projection can hydrate pins for
/// the canvas without mutating the asset or the serialization format.
/// </para>
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>If <paramref name="node"/>.Pins is non-empty (test-builder assets), return it as-is.</item>
///   <item>Try the <see cref="NodeKindRegistry"/> via <paramref name="registry"/> —
///     call <c>CreateInstance().Pins</c> on the matching descriptor.</item>
///   <item>Fall back to the built-in table below for kinds that are not in the registry
///     (core compiler kinds that appear in the JSON test fixtures).</item>
/// </list>
/// </para>
/// </summary>
internal static class NodePinSchema
{
    /// <summary>
    /// Returns the canonical <see cref="Pin"/> list for <paramref name="node"/>.
    /// The returned pins have freshly generated (non-stable) GUIDs.  The caller's
    /// two-pass GUID-binding step must replace them with the real GUIDs from
    /// incident links before projecting <see cref="BlueprintPinModel"/> instances.
    /// </summary>
    public static IReadOnlyList<Pin> GetCanonicalPins(
        Node node,
        NodeKindRegistry? registry = null)
    {
        // Pass 0: asset already has pins (builder-created test assets).
        if (node.Pins.Count > 0)
            return node.Pins;

        // Pass 1: registry descriptor.
        if (registry != null)
        {
            var kindName = node.GetType().Name; // e.g. "WhenNode", "ReadEqsResultNode"
            var descriptor = registry.TryGet(kindName);
            if (descriptor == null)
            {
                // Also try without the "Node" suffix (registry keys like "When", "ReadEqsResult").
                var shortName = kindName.EndsWith("Node")
                    ? kindName[..^4]
                    : kindName;
                descriptor = registry.TryGet(shortName);
            }
            if (descriptor != null)
            {
                try
                {
                    var instance = descriptor.CreateInstance();
                    if (instance.Pins.Count > 0)
                        return instance.Pins;
                }
                catch { /* fallthrough to built-in table */ }
            }
        }

        // Pass 2: built-in fallback table for core compiler node kinds.
        return node switch
        {
            EventEntryNode      => ExecOnly("Out"),
            ReturnNode          => ExecOnly("In"),
            BranchNode          => BranchPins(),
            SequenceNode        => SequencePins(),
            FunctionCallNode fc => FunctionCallPins(fc),
            GetVariableNode gv  => GetVariablePins(gv),
            SetVariableNode sv  => SetVariablePins(sv),
            LiteralNode lt      => LiteralPins(lt),
            CastNode ca         => CastPins(ca),
            LatentDelayNode     => ExecInOut(),
            ChannelCommandNode  => ExecInOut(),
            WaitForChannelNode  => ExecInOut(),
            WaitForEventNode    => ExecInOut(),
            CallCustomEventNode => ExecInOut(),
            CallPeerBlueprintNode => ExecInOut(),
            CallEventDispatcherNode => ExecInOut(),
            BindEventDispatcherNode => ExecInOut(),
            ArrayMakeNode       => ExecOnly("Out"),
            ArrayGetNode        => ExecInOut(),

            // Newer node kinds whose full pin schemas are in the registry;
            // if they reach here with empty pins just give them exec in/out.
            WhenNode            => ExecInOut(),
            ReadEqsResultNode   => Array.Empty<Pin>(),
            SpawnEqsSensorNode  => ExecInOut(),
            ScoreDecisionNode   => ExecInOut(),
            ReadRankedResultNode => Array.Empty<Pin>(),
            PartitionElementsNode => ExecInOut(),
            AssignRolesNode     => ExecInOut(),
            AdvancePhaseNode    => ExecInOut(),
            AcquireSlotNode     => ExecInOut(),

            _ => Array.Empty<Pin>(),
        };
    }

    // ── per-kind schema helpers ───────────────────────────────────────────────

    /// <summary>A single exec pin in the given direction.</summary>
    private static IReadOnlyList<Pin> ExecOnly(string direction)
        => new[] { MakeExec(direction == "In" ? "In" : "Out", direction) };

    /// <summary>Exec-in + exec-out, named "In" and "Out".</summary>
    private static IReadOnlyList<Pin> ExecInOut()
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

    private static IReadOnlyList<Pin> BranchPins()
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("True",  "Out"),
            MakeExec("False", "Out"),
        };

    private static IReadOnlyList<Pin> SequencePins()
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("Then0", "Out"),
            MakeExec("Then1", "Out"),
        };

    private static IReadOnlyList<Pin> FunctionCallPins(FunctionCallNode fc)
    {
        var pins = new List<Pin>();
        if (!fc.IsPure)
        {
            pins.Add(MakeExec("In",  "In"));
            pins.Add(MakeExec("Out", "Out"));
        }
        return pins;
    }

    private static IReadOnlyList<Pin> GetVariablePins(GetVariableNode gv)
        => new[]
        {
            MakeData("Value", "Out", "System.Object"),
        };

    private static IReadOnlyList<Pin> SetVariablePins(SetVariableNode sv)
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("Out",   "Out"),
            MakeData("Value", "In",  "System.Object"),
            MakeData("Value", "Out", "System.Object"),
        };

    private static IReadOnlyList<Pin> LiteralPins(LiteralNode lt)
        => new[]
        {
            MakeData("Value", "Out", string.IsNullOrEmpty(lt.TypeId) ? "System.Object" : lt.TypeId),
        };

    private static IReadOnlyList<Pin> CastPins(CastNode ca)
        => new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("In",  "In",  "System.Object"),
            MakeData("Out", "Out", string.IsNullOrEmpty(ca.TargetTypeId) ? "System.Object" : ca.TargetTypeId),
        };

    // ── primitive factory helpers ─────────────────────────────────────────────

    private static Pin MakeExec(string name, string direction) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = direction,
        IsExec    = true,
        TypeRef   = new BlueprintTypeRef(),
    };

    private static Pin MakeData(string name, string direction, string typeId) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = direction,
        IsExec    = false,
        TypeRef   = new BlueprintTypeRef { TypeId = typeId },
    };
}
