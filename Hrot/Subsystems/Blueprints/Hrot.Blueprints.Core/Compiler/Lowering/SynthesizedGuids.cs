namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class SynthesizedGuids
{
    public static Guid PhaseField(Guid assetId)
        => Derive("phase-field", assetId.ToString());

    public static Guid WaitUntilTimeField(Guid assetId)
        => Derive("wait-until-time", assetId.ToString());

    public static Guid DispatchBlock(Guid graphId)
        => Derive("dispatch-block", graphId.ToString());

    public static Guid PhaseBlock(Guid graphId, int phase)
        => Derive("phase-block", graphId.ToString(), phase.ToString());

    private static Guid Derive(string purpose, params string[] inputs)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var s = purpose + "|" + string.Join("|", inputs);
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return new Guid(hash[..16]);
    }
}
