using System;
using System.Collections.Generic;
using Fdp.Toolkit.Utility;
using Hrot.Editor.AiShared;

namespace Hrot.Utility.Editor.Model;

// In-memory editor model for one utility AI decision.
// Mirrors the runtime UtilityDecisionDef but is mutable and carries VisualIds.
public sealed class UtilityDecisionAsset : IEditableAsset
{
    // ---- IEditableAsset -------------------------------------------------

    public Guid   AssetId         { get; set; } = Guid.NewGuid();
    public string Name            => DisplayName;
    public AssetKind Kind         => AssetKind.Utility;
    public string SourceFilePath  { get; set; } = string.Empty;

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            if (_isDirty) Changed?.Invoke();
        }
    }

    // True iff the source file contains the HROT_EDITOR_GENERATED marker.
    public bool IsEditorOwned { get; set; }

    public event Action? Changed;

    // ---- Decision-specific fields ---------------------------------------

    public string         DisplayName      = string.Empty;
    public DecisionKind   DecisionKind     = DecisionKind.PostureSelect;
    public string         Category         = string.Empty;
    public float          HysteresisBonus  = 0f;
    public List<OptionModel>  Options      = new();
    public List<FixtureRef>   Fixtures     = new();
    public UtilityLayoutData  Layout       = new();
}
