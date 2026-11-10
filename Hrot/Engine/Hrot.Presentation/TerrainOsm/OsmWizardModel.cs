using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hrot.Terrain.InputSet;
using Hrot.Terrain.Osm;

namespace Hrot.UI.Common.TerrainOsm;

/// <summary>The four steps of the wizard (OSM §6, the step bar).</summary>
public enum OsmWizardStep { Area, Options, Preview, Import }

/// <summary>Where a worker run stands (the preview fetch, the import).</summary>
public enum OsmRunState { Idle, Running, Done, Failed }

/// <summary>A stage row of the Import step (C-14): its label, state, bar and what it produced.</summary>
public sealed class OsmStageRow
{
    public OsmStageRow(string label) { Label = label; }
    public string Label { get; }
    public OsmRunState State { get; internal set; }
    public double Fraction { get; internal set; }
    public string Produced { get; internal set; } = "";
}

/// <summary>
/// ⭐ CE-4375 (OSM §6b) — the composition the editor hands the wizard: the pipeline / tiles / search it HAS (the coordinator's, when on the tree) and the importer
/// (backend's CE-4368, optional). ⛔ No stub pipeline in production: a null <see cref="Pipeline"/> greys the menu item (W-3).
/// </summary>
public sealed record OsmWizardServices(IOsmTerrainPipeline? Pipeline, ITileSource? Tiles, IPlaceSearch? Search, ITerrainImporter? Importer,
                                        string TerrainsRoot, string InputSetRoot)
{
    /// <summary>The finished terrain → New Scenario with it (the host's).</summary>
    public Action<string>? OpenNewScenario { get; init; }
    /// <summary>Opens a folder in the OS file browser (the host's).</summary>
    public Action<string>? ShowFolder { get; init; }
}

/// <summary>
/// ⭐⭐ CE-4375 (OSM §6, §6b — the state diagram) — <b>the wizard's step logic, pure</b> (no ImGui): the box and its measure, the options and the name check, the preview and the import
/// on WORKERS (a <see cref="Task"/> + <see cref="CancellationTokenSource"/>, polled by <see cref="Pump"/> once a frame — the <c>ReplaySearchPanel</c> pattern, C-15), every
/// transition, Cancel, a failure that names its stage with Retry, and Refused that greys Import. The window draws it; the state-machine rail drives it with fakes.
/// </summary>
public sealed class OsmWizardModel
{
    /// <summary>OSM §5.7 — the load HROT is tuned for (buildings, doors + windows, trees in a 5.5 km square). The budget bars are drawn against it.</summary>
    public const double TunedBuildings = 1800, TunedOpenings = 70000, TunedTrees = 176000;

    private readonly OsmWizardServices _services;

    public OsmWizardModel(OsmWizardServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Stages = new[]
        {
            new OsmStageRow("Map data (OSM)"), new OsmStageRow("Read the map data"), new OsmStageRow("Heights at 1 m"), new OsmStageRow("Roads (netconvert)"),
            new OsmStageRow("Buildings"), new OsmStageRow("Trees, walls, water"), new OsmStageRow("Write the input set"), new OsmStageRow("Import into HROT"),
        };
    }

    public OsmWizardServices Services => _services;
    public OsmWizardStep Step { get; private set; } = OsmWizardStep.Area;

    /// <summary>The largest side (m): the pipeline's, else the design's 6 km (Q-O3).</summary>
    public double MaxSideMetres => _services.Pipeline?.MaxSideMetres ?? 6000.0;

    // ── ① Area ─────────────────────────────────────────────────────────────────────────────

    public GeoBox? Box { get; private set; }
    public double BoxWidthM { get; private set; }
    public double BoxHeightM { get; private set; }
    /// <summary>Why the box cannot be used (the pipeline's refusal, or larger than the limit); null = it can.</summary>
    public string? BoxRefusal { get; private set; }

    /// <summary>The box is over the limit (the size chip RED, Next greyed — C-7).</summary>
    public bool BoxTooLarge => Box != null && (BoxWidthM > MaxSideMetres || BoxHeightM > MaxSideMetres);

    /// <summary>Sets and measures the box (before any download).</summary>
    public void SetBox(GeoBox box)
    {
        var b = new GeoBox(Math.Min(box.South, box.North), Math.Min(box.West, box.East), Math.Max(box.South, box.North), Math.Max(box.West, box.East));
        Box = b;
        if (_services.Pipeline is { } p)
        {
            var (w, h, refusal) = p.Measure(b);
            BoxWidthM = w; BoxHeightM = h; BoxRefusal = refusal;
        }
        else
        {
            (BoxWidthM, BoxHeightM) = WebMercator.SizeOf(b);
            BoxRefusal = null;
        }
        if (BoxRefusal == null && BoxTooLarge) BoxRefusal = $"larger than {MaxSideMetres / 1000.0:0} × {MaxSideMetres / 1000.0:0} km";
    }

    public (double Lat, double Lon) BoxCentre => Box is { } b ? ((b.South + b.North) * 0.5, (b.West + b.East) * 0.5) : (0, 0);

    // ── the place search (C-6) ────────────────────────────────────────────────────────────

    public string SearchText = "";
    public IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)> SearchResults { get; private set; } = Array.Empty<(string, double, double, GeoBox?)>();
    public string? SearchFailure { get; private set; }
    private Task<IReadOnlyList<(string Name, double Lat, double Lon, GeoBox? Box)>>? _searchTask;
    private CancellationTokenSource? _searchCts;

    /// <summary>Searches <see cref="SearchText"/> on a worker (the results land on a later <see cref="Pump"/>).</summary>
    public void RunSearch()
    {
        if (_services.Search is not { } search || string.IsNullOrWhiteSpace(SearchText)) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token; string q = SearchText;
        SearchFailure = null;
        _searchTask = Task.Run(() => search.SearchAsync(q, ct), ct);
    }

    public bool Searching => _searchTask is { IsCompleted: false };

    /// <summary>A result picked: the box becomes the place's own (clamped to the limit), else a 2 km box round it. Returns the box.</summary>
    public GeoBox PickResult(int index)
    {
        var r = SearchResults[index];
        GeoBox box = r.Box ?? WebMercator.BoxAround(r.Lat, r.Lon, 2000, 2000);
        var (w, h) = WebMercator.SizeOf(box);
        if (w > MaxSideMetres || h > MaxSideMetres)   // clamped 1 m INSIDE the limit: a box of exactly the limit measures a hair over it (rounding) and would be refused
            box = WebMercator.BoxAround(r.Lat, r.Lon, Math.Min(w, MaxSideMetres - 1), Math.Min(h, MaxSideMetres - 1));
        SearchResults = Array.Empty<(string, double, double, GeoBox?)>();
        SetBox(box);
        return box;
    }

    // ── ② Options ─────────────────────────────────────────────────────────────────────────

    public string Name = "";
    public bool IncludeBuildings = true, IncludeTrees = true, IncludeWalls = true, IncludeWater = true;
    public double StoreyHeight = 3.0;
    public int DefaultStoreys = 2;
    public bool Replace;

    /// <summary>Why the name cannot be used (null = it can): empty, or not a valid folder name.</summary>
    public string? NameRefusal
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "a terrain needs a name";
            // ⭐ CE-4378 — the importer's ONE rule (Hrot.Terrain.InputSet.TerrainName): what is refused here is what the import would refuse
            return Hrot.Terrain.InputSet.TerrainName.IsValid(Name) ? null : "letters and digits, then - and _ (it is a folder name)";
        }
    }

    /// <summary>The terrain the name would be saved as.</summary>
    public string SavedAs => "assets/terrain/" + Name + "/";

    /// <summary>A terrain of this name exists (the amber row, C-9).</summary>
    public bool NameExists => NameRefusal == null && Directory.Exists(Path.Combine(_services.TerrainsRoot, Name));

    /// <summary>The scenarios that use the existing terrain (listed beside Replace).</summary>
    public IReadOnlyList<string> ScenariosUsingName => NameExists ? _services.Importer?.ScenariosUsing(Name) ?? Array.Empty<string>() : Array.Empty<string>();

    public OsmImportOptions Options => new()
    {
        Name = Name, Buildings = IncludeBuildings, Trees = IncludeTrees, Walls = IncludeWalls, Water = IncludeWater,
        StoreyHeight = StoreyHeight, DefaultStoreys = DefaultStoreys,
    };

    // ── ③ Preview ─────────────────────────────────────────────────────────────────────────

    public OsmRunState PreviewState { get; private set; }
    public OsmPreview? Preview { get; private set; }
    public OsmProgress PreviewProgress => _previewProgress;
    private OsmProgress _previewProgress;
    private Task<OsmPreview>? _previewTask;
    private CancellationTokenSource? _previewCts;

    /// <summary>The failure in words, naming the service or the stage (§7); null = none.</summary>
    public string? Failure { get; private set; }

    public BudgetVerdict? Verdict => Preview?.Verdict;

    /// <summary>The load against the tuned load (1 = tuned) — buildings, openings, trees.</summary>
    public (double Buildings, double Openings, double Trees) Load
        => Preview is { } p ? (p.Counts.Buildings / TunedBuildings, (p.Counts.Doors + p.Counts.Windows) / TunedOpenings, p.Counts.EstimatedTrees / TunedTrees) : (0, 0, 0);

    /// <summary>Where a load sits on a budget bar (0…1): 1× at ⅓, 2× at ⅔ (green ends), 3× at ⅚ (amber ends), the red zone above (C-13).</summary>
    public static double BarPosition(double load)
        => load <= 2 ? load / 3.0 : load <= 3 ? 2.0 / 3.0 + (load - 2) / 6.0 : 5.0 / 6.0 + Math.Min(load - 3, 1.0) / 6.0;

    // ── ④ Import ──────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<OsmStageRow> Stages { get; }
    public OsmRunState ImportState { get; private set; }
    /// <summary>The written terrain folder (importer composed) — "Open in New Scenario".</summary>
    public string? TerrainFolder { get; private set; }
    /// <summary>The written input set — the "not built yet" end when no importer is composed.</summary>
    public string? InputSetFolder { get; private set; }
    private Task<(string InputSet, string? Terrain)>? _importTask;
    private CancellationTokenSource? _importCts;
    private readonly Stopwatch _elapsed = new();
    private int _importRun;

    public TimeSpan Elapsed => _elapsed.Elapsed;

    /// <summary>The whole run's fraction (0…1) — the stages count equally.</summary>
    public double OverallFraction
    {
        get
        {
            double sum = 0;
            foreach (var s in Stages) sum += s.State == OsmRunState.Done ? 1 : s.Fraction;
            return sum / Stages.Count;
        }
    }

    /// <summary>"about 2 minutes left" (null until a fraction is known).</summary>
    public TimeSpan? Remaining
    {
        get
        {
            double f = OverallFraction;
            if (f <= 0.02 || ImportState != OsmRunState.Running) return null;
            return TimeSpan.FromSeconds(Elapsed.TotalSeconds * (1 - f) / f);
        }
    }

    // ── the transitions (the state diagram, §6b) ─────────────────────────────────────────

    /// <summary>Why the footer's primary button is greyed now (null = it acts).</summary>
    public string? PrimaryRefusal => Step switch
    {
        OsmWizardStep.Area => Box == null ? "drag a box on the map, or search a place" : BoxRefusal,
        OsmWizardStep.Options => NameRefusal ?? (NameExists && !Replace ? "the terrain exists — tick Replace to overwrite it" : null),
        OsmWizardStep.Preview => PreviewState == OsmRunState.Running ? "the preview is still being made"
                               : PreviewState == OsmRunState.Failed ? "the preview failed — Retry"
                               : Preview?.Verdict == BudgetVerdict.Refused ? "too heavy for HROT — pick a smaller box" : null,
        _ => null,
    };

    /// <summary>The primary action: Next (Area) · Preview (Options) · Import (Preview). False when refused.</summary>
    public bool Primary()
    {
        if (PrimaryRefusal != null) return false;
        switch (Step)
        {
            case OsmWizardStep.Area: Step = OsmWizardStep.Options; return true;
            case OsmWizardStep.Options: Step = OsmWizardStep.Preview; StartPreview(); return true;
            case OsmWizardStep.Preview: Step = OsmWizardStep.Import; StartImport(); return true;
        }
        return false;
    }

    public bool CanBack => Step is OsmWizardStep.Options or OsmWizardStep.Preview;

    public void Back()
    {
        if (Step == OsmWizardStep.Options) Step = OsmWizardStep.Area;
        else if (Step == OsmWizardStep.Preview) { CancelPreview(); Step = OsmWizardStep.Options; }
    }

    /// <summary>Cancel: in Import it stops the run (nothing written) and returns to Preview; elsewhere the caller closes the window. Returns whether the window should close.</summary>
    public bool Cancel()
    {
        if (Step == OsmWizardStep.Import && ImportState == OsmRunState.Running)
        {
            _importCts?.Cancel();
            _importRun++;   // its late answer is ignored
            _elapsed.Stop();
            ImportState = OsmRunState.Idle;
            foreach (var s in Stages) { s.State = OsmRunState.Idle; s.Fraction = 0; s.Produced = ""; }
            Step = OsmWizardStep.Preview;
            return false;
        }
        CancelPreview();
        _searchCts?.Cancel();
        return true;
    }

    /// <summary>Retry after a failure: the preview, or the import.</summary>
    public void Retry()
    {
        if (Step == OsmWizardStep.Preview && PreviewState == OsmRunState.Failed) StartPreview();
        else if (Step == OsmWizardStep.Import && ImportState == OsmRunState.Failed) StartImport();
    }

    private void StartPreview()
    {
        CancelPreview();
        if (_services.Pipeline is not { } pipeline || Box is not { } box) { PreviewState = OsmRunState.Failed; Failure = "no pipeline"; return; }
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token; var options = Options;
        var sync = new SyncProgress<OsmProgress>(p => _previewProgress = p);
        Failure = null; Preview = null; PreviewState = OsmRunState.Running;
        _previewTask = Task.Run(() => pipeline.PreviewAsync(box, options, sync, ct), ct);
    }

    private void CancelPreview()
    {
        _previewCts?.Cancel();
        _previewTask = null;
        if (PreviewState == OsmRunState.Running) PreviewState = OsmRunState.Idle;
    }

    private void StartImport()
    {
        if (_services.Pipeline is not { } pipeline || Box is not { } box) { ImportState = OsmRunState.Failed; Failure = "no pipeline"; return; }
        _importCts = new CancellationTokenSource();
        var ct = _importCts.Token; var options = Options; string name = Name; bool replace = Replace;
        var importer = _services.Importer;
        string folder = Path.Combine(_services.InputSetRoot, name);
        foreach (var s in Stages) { s.State = OsmRunState.Idle; s.Fraction = 0; s.Produced = ""; }
        Failure = null; TerrainFolder = null; InputSetFolder = null; ImportState = OsmRunState.Running;
        int run = ++_importRun;
        var osm = new SyncProgress<OsmProgress>(p => { if (run == _importRun) OnStage(p); });
        var imp = new SyncProgress<ImportProgress>(p => { if (run == _importRun) OnImportStage(p); });
        _elapsed.Restart();
        _importTask = Task.Run(async () =>
        {
            string set = await pipeline.WriteInputSetAsync(box, options, folder, osm, ct).ConfigureAwait(false);
            string? terrain = importer == null ? null : await importer.ImportAsync(set, name, replace, imp, ct).ConfigureAwait(false);
            return (set, terrain);
        }, ct);
    }

    private readonly object _stageLock = new();

    private void OnStage(OsmProgress p)
    {
        lock (_stageLock)
        {
            int at = (int)p.Stage;
            for (int i = 0; i < at && i < Stages.Count; i++) if (Stages[i].State != OsmRunState.Done) { Stages[i].State = OsmRunState.Done; Stages[i].Fraction = 1; }
            var row = Stages[Math.Min(at, Stages.Count - 2)];
            row.State = p.Fraction >= 1 ? OsmRunState.Done : OsmRunState.Running;
            row.Fraction = Math.Clamp(p.Fraction, 0, 1);
            if (!string.IsNullOrEmpty(p.Message)) row.Produced = p.Message;
        }
    }

    private void OnImportStage(ImportProgress p)
    {
        lock (_stageLock)
        {
            for (int i = 0; i < Stages.Count - 1; i++) { Stages[i].State = OsmRunState.Done; Stages[i].Fraction = 1; }
            var row = Stages[^1];
            row.State = OsmRunState.Running; row.Fraction = Math.Clamp(p.Fraction, 0, 1); row.Produced = p.Stage;
        }
    }

    /// <summary>⭐ Once a frame on the frame thread: lands what the workers finished (C-15 — the frame never waits).</summary>
    public void Pump()
    {
        if (_searchTask is { IsCompleted: true } st)
        {
            if (st.IsCompletedSuccessfully) SearchResults = st.Result;
            else if (!st.IsCanceled) SearchFailure = "the place search failed — " + (st.Exception?.GetBaseException().Message ?? "unknown");
            _searchTask = null;
        }
        if (_previewTask is { IsCompleted: true } pt)
        {
            if (pt.IsCompletedSuccessfully) { Preview = pt.Result; PreviewState = OsmRunState.Done; }
            else if (pt.IsCanceled) PreviewState = OsmRunState.Idle;
            else { PreviewState = OsmRunState.Failed; Failure = "the preview failed (" + StageWords(_previewProgress.Stage) + ") — " + (pt.Exception?.GetBaseException().Message ?? "unknown"); }
            _previewTask = null;
        }
        if (_importTask is { IsCompleted: true } it)
        {
            _elapsed.Stop();
            if (it.IsCompletedSuccessfully)
            {
                lock (_stageLock)
                {
                    for (int i = 0; i < Stages.Count - 1; i++) { Stages[i].State = OsmRunState.Done; Stages[i].Fraction = 1; }
                    if (it.Result.Terrain != null) { Stages[^1].State = OsmRunState.Done; Stages[^1].Fraction = 1; }
                }
                InputSetFolder = it.Result.InputSet; TerrainFolder = it.Result.Terrain; ImportState = OsmRunState.Done;
            }
            else if (!it.IsCanceled)
            {
                string stage = "the import";
                lock (_stageLock) foreach (var s in Stages) if (s.State == OsmRunState.Running) { stage = s.Label; s.State = OsmRunState.Failed; break; }
                ImportState = OsmRunState.Failed;
                Failure = stage + " failed — " + (it.Exception?.GetBaseException().Message ?? "unknown");
            }
            _importTask = null;
        }
    }

    private static string StageWords(OsmStage s) => s switch
    {
        OsmStage.Fetch => "downloading the map data",
        OsmStage.Parse => "reading the map data",
        OsmStage.Heights => "heights",
        OsmStage.Roads => "roads",
        OsmStage.Buildings => "buildings",
        OsmStage.Vegetation => "trees, walls, water",
        _ => "writing",
    };

    /// <summary>An <see cref="IProgress{T}"/> that reports on the CALLING thread (the worker): the value is a plain field the frame reads — no SynchronizationContext needed.</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public SyncProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
