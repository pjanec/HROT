namespace Hrot.Editor.AiShared.HotReload;

public enum HotReloadTier
{
    /// <summary>Layout-only change. Runtime not affected.</summary>
    Cosmetic,
    /// <summary>Parameter change. Instances retain state but lookup tables are patched.</summary>
    Soft,
    /// <summary>Topology change. Instances reset to initial state.</summary>
    Hard,
}
