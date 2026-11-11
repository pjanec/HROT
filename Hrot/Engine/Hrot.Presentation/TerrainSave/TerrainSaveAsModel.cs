namespace Hrot.UI.Common.TerrainSave;

/// <summary>The dialog's steps: the form, the overwrite confirmation (W-4), done.</summary>
public enum TerrainSaveStep { Form, ConfirmOverwrite, Done }

/// <summary>
/// ⭐ Terrain authoring U-5 (docs/DESIGN_Terrain_Authoring.md §13 W-3/W-4, §14 E-4…E-6; mockup 4) — the logic of <i>Save terrain as</i>, PURE (a rail runs it without a window): the name and its validity, the summary the engine would make
/// (refreshed when the name changes), the two choices (<i>Pre-build the walkable mesh</i>, <i>Create a scenario on it</i>), the overwrite confirmation naming the terrain's users, and the save itself. The engine is behind
/// <see cref="ITerrainSaveService"/>; every failure is a message on the model, never an exception in the frame.
/// </summary>
public sealed class TerrainSaveAsModel
{
    private readonly ITerrainSaveService _service;
    private string _name = "";
    private string _previewedFor = "\0";

    public TerrainSaveAsModel(ITerrainSaveService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        string stem = TerrainSourceNames.TerrainOf(_service.SourceScenarioName) ?? _service.SourceScenarioName ?? _service.SourceTerrainName ?? "terrain";   // a terrain's staged source saves back over that terrain
        Name = stem;
        ScenarioName = stem + "-mission";
    }

    public string Name
    {
        get => _name;
        set { if (value != _name) { _name = value; Step = TerrainSaveStep.Form; } }
    }

    public string ScenarioName { get; set; }
    public bool PrebuildMesh { get; set; } = true;
    public bool CreateScenario { get; set; } = true;
    public TerrainSaveStep Step { get; private set; } = TerrainSaveStep.Form;

    /// <summary>The summary for the current name (null until <see cref="Refresh"/> ran or when it failed — see <see cref="Error"/>).</summary>
    public TerrainSavePreview? Preview { get; private set; }

    /// <summary>The last failure (a bad name, a source that cannot be baked, a failed save), or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The users of the existing terrain of this name (W-4), filled when the overwrite confirmation is asked.</summary>
    public IReadOnlyList<string> Users { get; private set; } = Array.Empty<string>();

    public TerrainSaveOutcome? Outcome { get; private set; }

    /// <summary>The folder the terrain goes to, as the mockup says it: <c>assets/terrain/&lt;name&gt;/</c>.</summary>
    public string PathLine => $"assets/terrain/{Name}/  (with its source inside)";

    /// <summary>Why the name cannot be used, or null: a terrain name is one plain word of letters, digits, <c>-</c> and <c>_</c>.</summary>
    public string? NameProblem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "a name is required";
            foreach (char c in Name) if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.')) return "use letters, digits, - and _";
            return Name.StartsWith('.') || Name.Contains("..") ? "not a plain name" : null;
        }
    }

    public bool NameOk => NameProblem is null;
    public bool Overwrites => NameOk && _service.Exists(Name);

    /// <summary>Re-reads the summary when the name changed since the last read (cheap otherwise). Never throws.</summary>
    public void Refresh()
    {
        if (_previewedFor == Name) return;
        _previewedFor = Name;
        Preview = null; Error = null;
        if (!NameOk) return;
        try { Preview = _service.Preview(Name); }
        catch (Exception e) when (e is not OutOfMemoryException) { Error = e.Message; }
    }

    /// <summary>The estimate line of the mesh option: <c>≈ 2 min; loads fast after</c>, or <c>≈ 40 s</c>; empty when unknown or when nothing is left to bake.</summary>
    public string MeshEstimate
    {
        get
        {
            if (Preview?.EstimateSeconds is not { } s || s <= 0.5) return "loads fast after";
            return (s >= 90 ? $"≈ {Math.Round(s / 60)} min" : $"≈ {Math.Round(s)} s") + "; loads fast after";
        }
    }

    /// <summary>The <i>Save terrain</i> button: a name that works, a summary that was made, and a scenario name when one is asked for.</summary>
    public bool CanSave => NameOk && Preview is not null && Error is null && (!CreateScenario || !string.IsNullOrWhiteSpace(ScenarioName)) && Step != TerrainSaveStep.Done;

    /// <summary>
    /// Save terrain: a name that already exists asks first (W-4 — <see cref="Step"/> becomes <see cref="TerrainSaveStep.ConfirmOverwrite"/>, with <see cref="Users"/>); otherwise it saves. Returns true once the terrain
    /// is written (<see cref="Outcome"/>).
    /// </summary>
    public bool Save()
    {
        Refresh();
        if (!CanSave) return false;
        if (Step == TerrainSaveStep.Form && Overwrites)
        {
            try { Users = _service.UsersOf(Name); } catch (Exception e) when (e is not OutOfMemoryException) { Users = Array.Empty<string>(); }
            Step = TerrainSaveStep.ConfirmOverwrite;
            return false;
        }
        return Write();
    }

    /// <summary>Replace (the overwrite confirmation): saves over the existing terrain.</summary>
    public bool ConfirmReplace() => Step == TerrainSaveStep.ConfirmOverwrite && Write();

    /// <summary>Cancel on the confirmation: back to the form, nothing written.</summary>
    public void CancelReplace() { if (Step == TerrainSaveStep.ConfirmOverwrite) Step = TerrainSaveStep.Form; }

    private bool Write()
    {
        try
        {
            Outcome = _service.Save(Name, PrebuildMesh, CreateScenario ? ScenarioName : null);
            Step = TerrainSaveStep.Done; Error = null;
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Error = e.Message; Step = TerrainSaveStep.Form;
            return false;
        }
    }
}
