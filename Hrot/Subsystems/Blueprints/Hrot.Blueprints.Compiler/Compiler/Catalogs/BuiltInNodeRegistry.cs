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

        // GetShared/SetShared (Slice 2a-2): mirrors Get/SetVariable -- static skeleton only;
        // Stage0_Rehydrate enriches data pins directly from SharedTypeId (NOT asset.Variables --
        // the shared type is foreign to this asset).
        GetSharedNode   => Array.Empty<PinSchema>(),   // pure data-Out, type from SharedTypeId
        SetSharedNode   => new[] { ExecIn(), ExecOut() },

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
        WaitForChannelNode        => new[] { ExecIn(), ExecOut() },
        WaitForEventNode          => new[] { ExecIn(), ExecOut() },

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
