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

    // Stage 2 -- Validate (type references)
    public const string BP1500 = "BP1500";
    public const string BP1501 = "BP1501";
    public const string BP1502 = "BP1502";  // UnresolvableWildcard
    public const string BP1503 = "BP1503";  // ManagedTypeInState

    // Stage 2 -- Validate (graph structure)
    public const string BP1600 = "BP1600";  // OrphanedNode (Stage 2 graph-structure)
    public const string BP1601 = "BP1601";  // GraphHasNoReturn
    public const string BP1602 = "BP1602";  // GraphHasNoEntry

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
