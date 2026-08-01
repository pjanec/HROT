using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Built-in node registry that provides canonical, ordered, static pin shapes for all known
/// blueprint node kinds.  Dynamic kinds (EventEntry/Return/variable/FunctionCall) are enriched
/// by Stage0_Rehydrate from authored asset state; this registry returns their static skeleton.
/// Pin order matches the NodePinSchema switch cases in the editor (order is load-bearing).
/// </summary>
public sealed class BuiltInNodeRegistry : INodeRegistry
{
    public static readonly BuiltInNodeRegistry Instance = new();

    private static PinSchema ExecIn()  => new("In",  "In",  true,  "");
    private static PinSchema ExecOut() => new("Out", "Out", true,  "");
    private static PinSchema Data(string name, string dir, string typeId)
        => new(name, dir, false, typeId);

    /// <summary>
    /// Returns the canonical ordered static pin shapes for <paramref name="node"/>.
    /// Exec-only or shape-deterministic kinds return full shapes; dynamic kinds return
    /// their known static skeleton (exec pins only, or empty for pure-data nodes).
    /// </summary>
    public IReadOnlyList<PinSchema> GetStaticPins(Node node) => node switch
    {
        BranchNode      => BranchPins(),
        SequenceNode    => SequencePins(),
        LiteralNode lt  => LiteralPins(lt),
        CastNode ca     => CastPins(ca),
        LatentDelayNode => LatentDelayPins(),

        // Dynamic: return known static skeleton; Stage0_Rehydrate enriches from asset state.
        EventEntryNode  => new[] { ExecOut() },
        ReturnNode      => new[] { ExecIn() },
        GetVariableNode => Array.Empty<PinSchema>(),   // pure data-Out, type from variable
        SetVariableNode => new[] { ExecIn(), ExecOut() },

        // GetParameter (GAP-11): pure data-Out node, fully-authored pins (mirrors GetComponentNode --
        // the asset supplies the "Value" out-pin directly, typed from the referenced Parameter, so
        // Stage0_Rehydrate's "node.Pins.Count > 0 => skip" guard leaves it alone; no enricher needed).
        GetParameterNode  => Array.Empty<PinSchema>(),   // pure data-Out, type from the referenced Parameter (authored pin)

        // GetAllParameters: pure data-Out node, ONE out-pin per asset.Parameters entry. Dynamic
        // (like EventEntryNode) -- Stage0_Rehydrate.EnrichGetAllParametersPins rebuilds the full
        // pin set from asset.Parameters, so no static shape here.
        GetAllParametersNode => Array.Empty<PinSchema>(),   // pure data-Out(s), one per asset.Parameters entry

        // GetShared/SetShared (Slice 2a-2): mirrors Get/SetVariable -- static skeleton only;
        // Stage0_Rehydrate enriches data pins directly from SharedTypeId (NOT asset.Variables --
        // the shared type is foreign to this asset). Slice 2b: GetShared's enricher additionally
        // adds an OPTIONAL data-in "Target" Entity pin (cross-entity read) -- still no static
        // shape here, since the enricher fully rebuilds this node's pins regardless.
        GetSharedNode   => Array.Empty<PinSchema>(),   // pure data-(In Target?)/Out, type from SharedTypeId
        SetSharedNode   => new[] { ExecIn(), ExecOut() },

        // GetComponent (P2 migration + CA-01 multi-pin): pure data-(In Target?)/Out node, mirrors
        // GetSharedNode -- static skeleton is empty; Stage0_Rehydrate.EnrichGetComponentPins
        // rebuilds Target(in)/per-field-or-legacy-Value(out)/Found(out) whenever the node is
        // stored pin-less (fully-authored fixtures with Pins.Count > 0 are left alone by the
        // "node.Pins.Count > 0 => skip" guard, same as GetShared).
        GetComponentNode => Array.Empty<PinSchema>(),   // pure data-(In Target?)/Out; enriched by Stage0 when pin-less

        // SetComponent (CA-03, Slice W1): exec node, mirrors SetSharedNode -- static skeleton is
        // exec In/Out; Stage0_Rehydrate.EnrichSetComponentPins adds per-field data-ins + "Written"
        // whenever the node is stored pin-less. Self-only -- no "Target" pin (unlike GetComponent).
        SetComponentNode => new[] { ExecIn(), ExecOut() },

        // Compare (GAP-12) / BinaryOp (native arithmetic) / BooleanOp (native boolean logic):
        // pure data nodes, static skeleton data-(A,B In)/(Result Out). Blocker-1 tail fix: these
        // used to return Array.Empty here on the theory that "the asset always supplies
        // fully-authored Pins, so Stage0_Rehydrate's node.Pins.Count > 0 guard skips this and no
        // enricher is needed" -- true for hand-authored assets, but FALSE for a migrated pin-less
        // asset (Pins: []): with no static shape AND no Stage0_Rehydrate switch case (there isn't
        // one for these kinds), BuildCanonicalPins produced a genuinely EMPTY node.Pins, so
        // Stage5_Schedule's CompareNode/BinaryOpNode/BooleanOpNode cases (which look up "A"/"B" by
        // NAME off cn.Pins) found nothing, silently fell back to a bare AllocValue(UnknownType)
        // with NO producing statement, and dropped the pure FunctionCall/Literal producer that fed
        // it entirely -- emitted C# then referenced an SSA temp (__tN) that was never assigned
        // (CS0103). TypeId is deliberately left "" (untyped, like an exec pin): Stage5 never
        // consults A/B's own TypeRef (ResolveDataPin walks the LINK to the producer's pin type),
        // and Result's IR value type is hardcoded by Stage5 (BoolType for Compare/BooleanOp,
        // aVal.Type for BinaryOp) -- so an empty TypeId costs nothing and Stage4's type checker
        // (VerifyLinkTypes/BP1500) just skips the untyped pin, same as any other exec-only pin.
        // A node that already carries authored pins is unaffected (node.Pins.Count > 0 guard).
        CompareNode      => ComparePins(),
        BinaryOpNode     => ComparePins(),   // same A/B In, Result Out shape as Compare

        // BooleanOp / Not (native boolean logic nodes): mirror CompareNode/BinaryOpNode above.
        // Not is the one-operand case (A in, Result out only).
        BooleanOpNode    => ComparePins(),
        NotNode          => NotPins(),

        // FunctionCall: static exec skeleton; Stage0_Rehydrate fills data pins.
        FunctionCallNode fc when !fc.IsPure => new[] { ExecIn(), ExecOut() },
        FunctionCallNode   => Array.Empty<PinSchema>(), // pure

        // WhenNode: exec In + three named exec-Out pins (names are load-bearing for Stage5).
        WhenNode        => WhenPins(),

        // CallCustomEvent / CallPeerBlueprint / ChannelCommand: exec In/Out skeleton.
        CallCustomEventNode       => new[] { ExecIn(), ExecOut() },
        CallPeerBlueprintNode     => new[] { ExecIn(), ExecOut() },
        CallEventDispatcherNode   => new[] { ExecIn(), ExecOut() },
        BindEventDispatcherNode   => new[] { ExecIn(), ExecOut() },
        ChannelCommandNode        => new[] { ExecIn(), ExecOut() },
        // WaitForChannel (Q#13): exec-in "In", success exec-out "Out" (name kept for link compat),
        // failure exec-out "OnFailure", and a "Status" (NodeStatus) data-out. Names are load-bearing
        // for Stage5 (success = "Out", failure = "OnFailure"). OnFailure/Status unwired ⇒ behavior
        // byte-identical to the pre-Q13 single-exec-out node.
        WaitForChannelNode        => new[]
        {
            ExecIn(),
            ExecOut(),
            new("OnFailure", "Out", true, ""),
            // Runtime enum, carried with the AN2 "global::" sentinel so StaticTypeRegistry accepts it
            // as an unmanaged enum (Int32 backing) — the reflection-less compiler can't verify the FQN,
            // so it trusts the prefix and emits a cast the C# compiler validates. Matches the channel
            // component's Status field type (global::Fbt.NodeStatus).
            Data("Status", "Out", "global::Fbt.NodeStatus"),
        },
        // WaitForEvent (Q#13-D): same OnFailure exec split as WaitForChannel (shared WaitLowering
        // failure-block path). No "Status" data-out — the event-wait status model is unvalidated (no
        // real WaitForEvent asset exists yet); add it demand-driven when a use case appears.
        WaitForEventNode          => new[] { ExecIn(), ExecOut(), new("OnFailure", "Out", true, "") },

        // PublishEvent (P4 -- GAP-3): catalog-driven exec node, mirrors ChannelCommandNode.
        PublishEventNode          => new[] { ExecIn(), ExecOut() },

        // FlowForEach (P1 -- GAP-1): exec-in + "Body"/"Completed" named exec-outs (load-bearing for
        // Stage5) + a "CurrentItem" Entity data-out, plus optional loop-introspection data-outs
        // "CurrentIndex" (0-based iteration index, body-scoped) and "Count" (element count,
        // loop-invariant). Assets/fixtures author the pins they wire explicitly; unwired outs cost
        // nothing (Stage5 only binds a pin when the asset actually authors + wires it).
        FlowForEachNode           => new[]
        {
            ExecIn(),
            new("Body",      "Out", true,  ""),
            new("Completed", "Out", true,  ""),
            Data("CurrentItem",  "Out", "Fdp.Core.Entity"),
            Data("CurrentIndex", "Out", "System.Int32"),
            Data("Count",        "Out", "System.Int32"),
        },

        ArrayMakeNode am          => ArrayMakePins(am),
        ArrayGetNode              => ArrayGetPins(),

        ReadEqsResultNode         => Array.Empty<PinSchema>(),
        SpawnEqsSensorNode        => new[] { ExecIn(), ExecOut() },
        ScoreDecisionNode         => ScoreDecisionPins(),
        ReadRankedResultNode      => ReadRankedResultPins(),
        PartitionElementsNode     => new[] { ExecIn(), ExecOut() },
        AssignRolesNode           => new[] { ExecIn(), ExecOut() },
        AdvancePhaseNode          => new[] { ExecIn(), ExecOut() },
        AcquireSlotNode           => new[] { ExecIn(), ExecOut() },

        _ => Array.Empty<PinSchema>(),
    };

    // ── per-kind pin shapes ───────────────────────────────────────────────────

    /// <summary>Branch: exec-In "In", exec-Out "True", exec-Out "False", data-In "Condition"/Boolean.</summary>
    private static IReadOnlyList<PinSchema> BranchPins()
        => new[]
        {
            new PinSchema("In",        "In",  true,  ""),
            new PinSchema("True",      "Out", true,  ""),
            new PinSchema("False",     "Out", true,  ""),
            new PinSchema("Condition", "In",  false, "System.Boolean"),
        };

    /// <summary>Sequence: exec-In "In", exec-Out "Then0", exec-Out "Then1".</summary>
    private static IReadOnlyList<PinSchema> SequencePins()
        => new[]
        {
            new PinSchema("In",    "In",  true, ""),
            new PinSchema("Then0", "Out", true, ""),
            new PinSchema("Then1", "Out", true, ""),
        };

    /// <summary>Literal: data-Out "Value" typed by LiteralNode.TypeId.</summary>
    private static IReadOnlyList<PinSchema> LiteralPins(LiteralNode lt)
        => new[]
        {
            new PinSchema("Value", "Out", false,
                string.IsNullOrEmpty(lt.TypeId) ? "System.Object" : lt.TypeId),
        };

    /// <summary>
    /// Compare / BinaryOp / BooleanOp: pure data-(A,B In)/(Result Out). TypeId left "" (untyped) --
    /// see the GetStaticPins switch comment for why an unresolved TypeRef here is safe.
    /// </summary>
    private static IReadOnlyList<PinSchema> ComparePins()
        => new[]
        {
            new PinSchema("A",      "In",  false, ""),
            new PinSchema("B",      "In",  false, ""),
            new PinSchema("Result", "Out", false, ""),
        };

    /// <summary>Not: pure data-(A In)/(Result Out) -- Compare/BinaryOp's single-operand sibling.</summary>
    private static IReadOnlyList<PinSchema> NotPins()
        => new[]
        {
            new PinSchema("A",      "In",  false, ""),
            new PinSchema("Result", "Out", false, ""),
        };

    /// <summary>Cast: exec In/Out + data-In "In"/System.Object + data-Out "Out"/TargetTypeId.</summary>
    private static IReadOnlyList<PinSchema> CastPins(CastNode ca)
        => new[]
        {
            new PinSchema("In",  "In",  true,  ""),
            new PinSchema("Out", "Out", true,  ""),
            new PinSchema("In",  "In",  false, "System.Object"),
            new PinSchema("Out", "Out", false,
                string.IsNullOrEmpty(ca.TargetTypeId) ? "System.Object" : ca.TargetTypeId),
        };

    /// <summary>LatentDelay: exec In/Out + data-In "Duration"/System.Single.</summary>
    private static IReadOnlyList<PinSchema> LatentDelayPins()
        => new[]
        {
            new PinSchema("In",       "In",  true,  ""),
            new PinSchema("Out",      "Out", true,  ""),
            new PinSchema("Duration", "In",  false, "System.Single"),
        };

    /// <summary>
    /// WhenNode: exec-In "In", exec-Out "OnFired", exec-Out "OnEnded", exec-Out "Out".
    /// Pin names "OnFired"/"OnEnded"/"Out" are load-bearing — Stage5_Schedule.GetWhenExecSuccessor
    /// matches by name (OrdinalIgnoreCase).
    /// </summary>
    private static IReadOnlyList<PinSchema> WhenPins()
        => new[]
        {
            new PinSchema("In",      "In",  true, ""),
            new PinSchema("OnFired", "Out", true, ""),
            new PinSchema("OnEnded", "Out", true, ""),
            new PinSchema("Out",     "Out", true, ""),
        };

    /// <summary>ScoreDecision: exec In/Out + data-Out "WinningOptionId"/System.Byte.</summary>
    private static IReadOnlyList<PinSchema> ScoreDecisionPins()
        => new[]
        {
            new PinSchema("In",              "In",  true,  ""),
            new PinSchema("Out",             "Out", true,  ""),
            new PinSchema("WinningOptionId", "Out", false, "System.Byte"),
        };

    /// <summary>ReadRankedResult: three data-Out pins (IsValid/Entity/Score).</summary>
    private static IReadOnlyList<PinSchema> ReadRankedResultPins()
        => new[]
        {
            new PinSchema("IsValid", "Out", false, "System.Boolean"),
            new PinSchema("Entity",  "Out", false, "System.Int64"),
            new PinSchema("Score",   "Out", false, "System.Single"),
        };

    /// <summary>ArrayMake: exec In/Out + two data-In element pins + data-Out "Array".</summary>
    private static IReadOnlyList<PinSchema> ArrayMakePins(ArrayMakeNode am)
    {
        var elemType = string.IsNullOrEmpty(am.ElementTypeId) ? "System.Object" : am.ElementTypeId;
        return new[]
        {
            new PinSchema("In",    "In",  true,  ""),
            new PinSchema("Out",   "Out", true,  ""),
            new PinSchema("0",     "In",  false, elemType),
            new PinSchema("1",     "In",  false, elemType),
            new PinSchema("Array", "Out", false, elemType + "[]"),
        };
    }

    /// <summary>ArrayGet: exec In/Out + Array data-In + Index data-In + Element data-Out.</summary>
    private static IReadOnlyList<PinSchema> ArrayGetPins()
        => new[]
        {
            new PinSchema("In",      "In",  true,  ""),
            new PinSchema("Out",     "Out", true,  ""),
            new PinSchema("Array",   "In",  false, "System.Object"),
            new PinSchema("Index",   "In",  false, "System.Int32"),
            new PinSchema("Element", "Out", false, "System.Object"),
        };
}
