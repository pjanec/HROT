using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// Per-asset mutable state threaded through all emitters. Full implementation in TASK-CP-004.
/// </summary>
internal sealed class EmissionContext
{
    private int _localCounter;

    public IrAsset Asset { get; }

    public EmissionContext(IrAsset asset)
    {
        Asset = asset;
    }

    public string NextLocalCounter(string prefix)
        => $"{prefix}{_localCounter++}";
}
