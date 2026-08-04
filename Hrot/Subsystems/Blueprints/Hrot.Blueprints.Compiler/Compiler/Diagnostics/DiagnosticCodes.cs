namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

public static class DiagnosticCodes
{
    // Stage 1 -- Parse
    public const string BP0001_NullAsset      = "BP0001";
    public const string BP0002_JsonParseError = "BP0002";
    public const string BP0010_EmptyAssetId   = "BP0010";
    public const string BP0011_EmptyName      = "BP0011";

    // Stage 2 -- Validate (asset structure)
    public const string BP1010 = "BP1010";
    public const string BP1011 = "BP1011";
    public const string BP1012 = "BP1012";
    public const string BP1013 = "BP1013";
    public const string BP1020 = "BP1020";
    public const string BP1021 = "BP1021";
    public const string BP1022 = "BP1022";
    public const string BP1023 = "BP1023";
    public const string BP1024 = "BP1024";
    public const string BP1025 = "BP1025";
    public const string BP1030 = "BP1030";
    public const string BP1031 = "BP1031";

    // Stage 2 -- Validate (AiPrimitive intent rules)
    public const string BP1100 = "BP1100";
    public const string BP1101 = "BP1101";
    public const string BP1102 = "BP1102";  // Q#13: WaitForChannel OnFailure chain must terminate in an explicit Return

    // Stage 2 -- Validate (variables and state)
    public const string BP1200 = "BP1200";
    public const string BP1201 = "BP1201";
    public const string BP1210 = "BP1210";
    public const string BP1211 = "BP1211";

    // Stage 2 -- Validate (peer references)
    public const string BP1300 = "BP1300";
    public const string BP1301 = "BP1301";
    public const string BP1302 = "BP1302";

    // Stage 2 -- Validate (catalog references)
    public const string BP1400 = "BP1400";
    public const string BP1401 = "BP1401";
    public const string BP1402 = "BP1402";
    public const string BP1403 = "BP1403";  // BP-15: CallCustomEventNode.EventId references an unknown event
    public const string BP1404 = "BP1404";  // BP-15: ScoreDecisionNode.AssetId missing or not a well-formed GUID
    public const string BP1405 = "BP1405";  // BP-15: ReadRankedResultNode.Rank is negative (rank is 0-based)
    public const string BP1406 = "BP1406";  // BP-15: CastNode.TargetTypeId is empty or unresolvable

    // Stage 2 -- Validate (node kinds with no Stage5 lowering)
    // BP-16: these compile clean today and yield a silent wrong value at runtime. Erroring in Stage 2
    // converts silent data corruption into a build failure. See V_UnloweredNodeKinds.
    public const string BP1420 = "BP1420";  // Node kind has no Stage5 lowering -- would emit default(T) with no diagnostic

    // Stage 2 -- Validate (exec-out connectivity)
    public const string BP1411 = "BP1411";  // ExecOutFanOut: exec-out pin drives more than one successor
    public const string BP1412 = "BP1412";  // DroppedExecSuccessors: scheduler did not follow outgoing exec link(s)

    // Stage 5 -- Schedule (latent-in-Sequence deferral)
    public const string BP1413 = "BP1413";  // LatentInSequence: latent/suspending node inside a Sequence branch is not yet supported

    // Stage 2 -- Validate (type references)
    public const string BP1500 = "BP1500";
    public const string BP1501 = "BP1501";
    public const string BP1502 = "BP1502";  // UnresolvableWildcard
    public const string BP1503 = "BP1503";  // ManagedTypeInState
    public const string BP1504 = "BP1504";  // FC-2/LV-1: fixed-list variable with InitialLength outside [0, Capacity]
    public const string BP1505 = "BP1505";  // FC-2/LV-3: ListWriteNode target is not a declared fixed-list variable
    public const string BP1506 = "BP1506";  // FC-2/LV-3: fixed-list variable wired to a pin that cannot accept a list (whole-list clone via SetVariable is the one exception)
    public const string BP1507 = "BP1507";  // FC-3 (R5): fixed-list type on a Parameter declaration -- lists live on Variables/WorkingState/action DTOs, never Parameters/Shared (v1)

    // Stage 2 -- Validate (graph structure)
    public const string BP1600 = "BP1600";  // OrphanedNode (Stage 2 graph-structure)
    public const string BP1601 = "BP1601";  // GraphHasNoReturn
    public const string BP1602 = "BP1602";  // GraphHasNoEntry

    // Stage 2 -- Validate (function-graph call rules)
    public const string BP1650 = "BP1650";  // Latent node inside a function graph referenced by FunctionCallNode.TargetGraphId
    public const string BP1651 = "BP1651";  // FunctionCallNode.TargetGraphId not found or target graph is not GraphKind.Function
    public const string BP1652 = "BP1652";  // FunctionCallNode argument count mismatch (caller data-IN pin count ≠ target graph Inputs.Count)
    public const string BP1653 = "BP1653";  // FunctionCallNode argument type mismatch (positional: caller data-IN pin type incompatible with target Input type)
    public const string BP1654 = "BP1654";  // Function-graph call cycle detected (direct or transitive recursion)

    // Stage 2 -- Validate (WhenNode rules)
    public const string BP2001 = "BP2001";  // WhenNode in unsupported dispatch
    public const string BP2002 = "BP2002";  // WhenNode missing required payload
    public const string BP2003 = "BP2003";  // WhenNode Value Changed: invalid property path
    public const string BP2004 = "BP2004";  // WhenNode Value Changed: peer BP variable not declared
    public const string BP2005 = "BP2005";  // WhenNode Event Fired: event type not in catalog
    public const string BP2006 = "BP2006";  // WhenNode Event Fired: Self filter without target field
    public const string BP2007 = "BP2007";  // WhenNode Event Fired: payload condition invalid
    public const string BP2008 = "BP2008";  // WhenNode Condition Met: predicate tree null or empty
    public const string BP2009 = "BP2009";  // WhenNode Condition Met: predicate DTO references unknown type
    public const string BP2010 = "BP2010";  // WhenNode EQS Result: sensor variable not declared
    public const string BP2011 = "BP2011";  // WhenNode EQS Result: trigger requires threshold/max-age
    public const string BP2012 = "BP2012";  // WhenNode Edges set to None
    public const string BP2013 = "BP2013";  // WhenNode Event Fired falling edge meaningless (warning)
    public const string BP2014 = "BP2014";  // WhenNode Value Changed epsilon on non-float field (warning)
    public const string BP2015 = "BP2015";  // WhenNode downstream of a Branch (warning)
    public const string BP2016 = "BP2016";  // WhenNode Event Fired on BestEffort event (warning)
    public const string BP2017 = "BP2017";  // Brain WhenNode on PropagatesAcrossNodes=false event (error)

    // Stage 2 -- Validate (ReadEqsResultNode rules)
    public const string BP2020 = "BP2020";  // ReadEqsResultNode in unsupported dispatch
    public const string BP2021 = "BP2021";  // ReadEqsResultNode sensor variable not declared

    // Stage 2 -- Validate (SpawnEqsSensorNode rules)
    public const string BP2030 = "BP2030";  // SpawnEqsSensorNode in unsupported dispatch
    public const string BP2031 = "BP2031";  // SpawnEqsSensorNode template not found
    public const string BP2032 = "BP2032";  // SpawnEqsSensorNode InstanceId collision

    // Stage 2 -- Validate (GetShared/SetShared rules -- Slice 2a-2)
    public const string BP2040 = "BP2040";  // SharedTypeId empty
    public const string BP2041 = "BP2041";  // SharedTypeId does not resolve to a known unmanaged/blittable struct type
    public const string BP2042 = "BP2042";  // GetShared/SetShared in unsupported (Library) dispatch -- no `self` in scope

    public const string BP2050 = "BP2050";  // FlowForEach body contains a latent or (P1a) Branch node -- body must be a synchronous, latent-free (and branch-free) sub-DAG

    // Stage 2 -- Validate (SetComponent rules -- CA-03, Slice W1)
    public const string BP2060 = "BP2060";  // SetComponentNode.ComponentTypeFqn empty
    public const string BP2061 = "BP2061";  // SetComponentNode.ComponentTypeFqn not a well-formed type name
    public const string BP2062 = "BP2062";  // SetComponentNode carries a "Target" pin -- self-only, not permitted

    // Stage 2 -- Validate (managed component-read flow rules -- CA-05, Slice 1b)
    public const string BP2063 = "BP2063";  // Managed GetComponent field value wired into a persisting sink (SetVariable/SetShared)

    // Stage 2 -- Validate (managed component-write rules -- CA-06, Slice W2)
    public const string BP2064 = "BP2064";  // Managed SetComponentNode carries per-field Fields -- managed write is whole-replace-only
    public const string BP2065 = "BP2065";  // Managed SetComponentNode in AiPrimitive dispatch -- TickCore has no IEntityCommandBuffer in scope

    // Stage 2 -- Validate (component-collection consumer rules -- CA-07b)
    public const string BP2066 = "BP2066";  // ComponentForEach/ComponentItemGet/ComponentItemCount: "Collection" is wired but baked accessor FQNs are empty

    // Stage 2 -- Validate (component-collection WRITE rules -- FC-1, Q#20)
    public const string BP2067 = "BP2067";  // CollectionWriteNode: "Collection" is wired but ComponentTypeFqn/WriteAccessorFqn are empty or malformed (not baked at wire time)
    public const string BP2068 = "BP2068";  // CollectionWriteNode bound to a ManagedMember collection -- managed collections are not element-writable (Q#20-C, snapshot aliasing)
    public const string BP2069 = "BP2069";  // CollectionWriteNode carries a "Target" pin -- writes are self-only (Q#16/Q#20)
    public const string BP2070 = "BP2070";  // CollectionWriteNode's producer GetComponent has "Target" wired -- cross-entity collection write is not permitted (G4)
    public const string BP2071 = "BP2071";  // WARNING: CollectionWriteNode mutates the collection a surrounding ComponentForEach is iterating (G3 -- wire-dependent semantics)

    // Stage 3 -- Normalize
    public const string BP3010 = "BP3010";
    public const string BP3011 = "BP3011";
    public const string BP3012 = "BP3012";

    // Stage 4 -- TypeResolve
    public const string BP3001 = "BP3001";

    // Stage 5 -- Schedule
    public const string BP4001 = "BP4001";
    public const string BP4002 = "BP4002";
    public const string BP4003 = "BP4003";
    public const string BP4004 = "BP4004";

    // Stage 6 -- Lower
    public const string BP5001 = "BP5001";
    public const string BP5001_LibraryHasNoFunctions = "BP5001";

    // Stage 7 -- Emit
    public const string BP6001 = "BP6001";

    // Stage 8 -- Roslyn finalize
    public const string BP7001 = "BP7001";

    // Internal compiler errors
    public const string BP9001 = "BP9001";
    public const string BP9001_InternalLibraryLatent = "BP9001";
}
