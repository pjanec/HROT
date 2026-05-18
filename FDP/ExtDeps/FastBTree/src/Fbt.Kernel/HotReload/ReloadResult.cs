namespace Fbt.HotReload
{
    public enum ReloadResult
    {
        /// <summary>First time this tree name has been registered.</summary>
        NewTree,
        /// <summary>Both StructureHash and ParamHash are identical — no reload needed.</summary>
        NoChange,
        /// <summary>Only ParamHash changed (floats/ints). Entity state is preserved.</summary>
        SoftReload,
        /// <summary>StructureHash changed. Entity states reset via hardResetAction.</summary>
        HardReset,
    }
}
