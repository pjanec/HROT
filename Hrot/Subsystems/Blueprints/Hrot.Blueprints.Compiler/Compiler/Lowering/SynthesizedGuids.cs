namespace Hrot.Blueprints.Core.Compiler.Lowering;

internal static class SynthesizedGuids
{
    public static Guid PhaseField(Guid assetId)
        => Derive("phase-field", assetId.ToString());

    public static Guid WaitUntilTimeField(Guid assetId)
        => Derive("wait-until-time", assetId.ToString());

    /// <summary>
    /// Returns a deterministic GUID for the synthesized _when_&lt;id8&gt;_prev field
    /// of a specific WhenNode within a specific Blueprint asset.
    /// </summary>
    public static Guid WhenPrevField(Guid assetId, Guid nodeId)
        => Derive("when-prev-field", assetId.ToString(), nodeId.ToString());

    /// <summary>⭐ CE-2070 — the synthesized <c>_score_&lt;id8&gt;_last</c> field of one ScoreDecision node.</summary>
    public static Guid ScoreLastWinnerField(Guid assetId, string nodeId8)
        => Derive("score-last-winner", assetId.ToString(), nodeId8);

    public static Guid DispatchBlock(Guid graphId)
        => Derive("dispatch-block", graphId.ToString());

    public static Guid PhaseBlock(Guid graphId, int phase)
        => Derive("phase-block", graphId.ToString(), phase.ToString());

    private static Guid Derive(string purpose, params string[] inputs)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var s = purpose + "|" + string.Join("|", inputs);
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        var first16 = new byte[16];
        Array.Copy(hash, first16, 16);
        return new Guid(first16);
    }
}
