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

    // Stage 2 -- Validate (wait/latent rules)
    public const string BP1500 = "BP1500";
    public const string BP1501 = "BP1501";
    public const string BP1502 = "BP1502";
    public const string BP1503 = "BP1503";

    // Stage 3 -- Normalize
    public const string BP2001 = "BP2001";
    public const string BP2002 = "BP2002";
    public const string BP2003 = "BP2003";

    // Stage 4 -- TypeResolve
    public const string BP3001 = "BP3001";

    // Stage 5 -- Schedule
    public const string BP4001 = "BP4001";
    public const string BP4002 = "BP4002";
    public const string BP4003 = "BP4003";
    public const string BP4004 = "BP4004";

    // Stage 6 -- Lower
    public const string BP5001 = "BP5001";

    // Stage 7 -- Emit
    public const string BP6001 = "BP6001";

    // Stage 8 -- Roslyn finalize
    public const string BP7001 = "BP7001";

    // Internal compiler errors
    public const string BP9001 = "BP9001";
}
