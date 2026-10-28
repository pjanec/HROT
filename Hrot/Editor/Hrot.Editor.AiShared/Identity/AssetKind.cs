namespace Hrot.Editor.AiShared;

public enum AssetKind
{
    Blueprint,
    BTree,
    Hsm,
    Blackboard,
    Utility,
    Scenario,

    /// <summary>
    /// ⭐ CE-4098 (AQ108 D19) — a RECORDING (an exercise the master recorded). ⛔ <b>Not a catalog asset</b>: it has no file in any
    /// <see cref="Catalog.IAssetCatalog"/>, no tab in the asset browser and no <c>AssetKindFilter</c> flag — the master's exercise
    /// inventory lists them (<c>RecordingPickerLauncher</c>). The kind exists so a recording has ONE identity across the picker's
    /// Recent list, its icon and the Open-Recording item, like every other thing a user opens.
    /// </summary>
    Recording,
}
