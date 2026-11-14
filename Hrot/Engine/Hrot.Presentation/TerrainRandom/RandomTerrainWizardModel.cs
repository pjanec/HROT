namespace Hrot.UI.Common.TerrainRandom;

public enum RandomTerrainStep { Preset, Preview, Open }

/// <summary>
/// ⭐ Terrain authoring U-8 (docs/DESIGN_Terrain_Authoring.md §12, §13 W-1, §18f; mockup 1) — the logic of the random-terrain wizard, PURE: ① the preset and the four short rows (Size · Relief · Vegetation · Water) and the seed, ② the generated
/// preview with its verdict and the two shuffles (<i>Shuffle layout</i> keeps the ground, <i>Shuffle ground</i> makes a new one), ③ <i>Open in editor</i>. Generate and shuffle run on a worker (<see cref="Pump"/> lands them), so the frame never waits.
/// </summary>
public sealed class RandomTerrainWizardModel
{
    private readonly IRandomTerrainService _service;
    private Task<RandomTerrainView>? _running;
    private Task<string>? _opening;
    private readonly CancellationTokenSource _cancel = new();

    public RandomTerrainWizardModel(IRandomTerrainService service, ulong seed = 0)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Seed = seed != 0 ? seed : (ulong)Random.Shared.Next(10000, 99999);
        Name = "random-" + Seed;
    }

    public RandomTerrainStep Step { get; private set; } = RandomTerrainStep.Preset;
    public int Preset { get; set; } = 4;               // Town with suburbs, the mockup's selection
    public int Size { get; set; } = 2;                 // 3 km
    public int Relief { get; set; } = 1;               // Rolling
    public int Vegetation { get; set; } = 1;
    public int Water { get; set; } = 2;                // A river
    public ulong Seed { get; set; }
    public string Name { get; set; }

    /// <summary>The six presets' tile pictures, filled one by one by <see cref="LoadPictures"/> (null until each arrives, or when the host cannot make it).</summary>
    public RandomTerrainView?[] Pictures { get; } = new RandomTerrainView?[RandomTerrainChoices.Presets.Length];

    /// <summary>Makes the preset tiles' pictures on one worker, in order, so the first step shows its stand-ins at once and the real pictures as they land. A failed picture stays a stand-in.</summary>
    public void LoadPictures()
    {
        var ct = _cancel.Token;
        _ = Task.Run(() =>
        {
            for (int i = 0; i < Pictures.Length && !ct.IsCancellationRequested; i++)
                try { Volatile.Write(ref Pictures[i], _service.PresetPicture(i)); } catch (Exception) { /* the tile keeps its stand-in */ }
        }, ct);
    }

    public RandomTerrainView? View { get; private set; }
    public string? Error { get; private set; }
    public bool Busy => _running is { IsCompleted: false } || _opening is { IsCompleted: false };
    public string? OpenedScenario { get; private set; }
    /// <summary>What the running work is (the status line): <c>Generating…</c>, <c>Shuffling the layout…</c>, <c>Importing the terrain…</c>.</summary>
    public string Activity { get; private set; } = "";

    public RandomTerrainSettings Settings(ulong layoutSeed = 0) => new(Preset, RandomTerrainChoices.Sizes[Size].Metres, Relief, Vegetation, Water, Seed, layoutSeed != 0 ? layoutSeed : Seed);

    /// <summary>① → ②: makes the ground and the layout.</summary>
    public void Generate()
    {
        if (Busy) return;
        Activity = "Generating…"; Error = null;
        var settings = Settings();
        _running = Task.Run(() => _service.Generate(settings));
        Step = RandomTerrainStep.Preview;
    }

    /// <summary>② Shuffle layout: new places for the blocks, same ground (a new layout seed derived from the last).</summary>
    public void ShuffleLayout()
    {
        if (Busy || View is null) return;
        LayoutSeed = LayoutSeed * 6364136223846793005UL + 1442695040888963407UL;
        Activity = "Shuffling the layout…"; Error = null;
        ulong seed = LayoutSeed;
        _running = Task.Run(() => _service.ShuffleLayout(seed) ?? throw new InvalidOperationException("nothing generated yet"));
    }

    /// <summary>② Shuffle ground: a new ground seed (new hills, roads and river), then a layout.</summary>
    public void ShuffleGround()
    {
        if (Busy) return;
        Seed = Seed * 6364136223846793005UL + 1442695040888963407UL;
        LayoutSeed = 0;
        Activity = "Generating a new ground…"; Error = null;
        var settings = Settings();
        _running = Task.Run(() => _service.Generate(settings));
    }

    public ulong LayoutSeed { get; private set; } = 1;

    /// <summary>② → ①.</summary>
    public void Back() { if (!Busy) Step = RandomTerrainStep.Preset; }

    /// <summary>② → ③: saves the ground as a terrain and the blocks as a scenario, then the host opens it.</summary>
    public void OpenInEditor()
    {
        if (Busy || View is null || !NameOk) return;
        Activity = "Importing the terrain…"; Error = null;
        string name = Name;
        _opening = Task.Run(() => _service.OpenInEditorAsync(name, _cancel.Token));
        Step = RandomTerrainStep.Open;
    }

    /// <summary>One plain word: letters, digits, <c>-</c> and <c>_</c>.</summary>
    public bool NameOk => !string.IsNullOrWhiteSpace(Name) && Name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_') ;

    public bool NameExists => NameOk && _service.NameExists(Name);

    public void Cancel() => _cancel.Cancel();

    /// <summary>Lands finished work (call every frame). A failure becomes <see cref="Error"/> — never an exception in the frame.</summary>
    public void Pump()
    {
        if (_running is { IsCompleted: true } r)
        {
            _running = null;
            if (r.IsFaulted) { Error = r.Exception!.GetBaseException().Message; if (View is null) Step = RandomTerrainStep.Preset; }
            else View = r.Result;
        }
        if (_opening is { IsCompleted: true } o)
        {
            _opening = null;
            if (o.IsFaulted) { Error = o.Exception!.GetBaseException().Message; Step = RandomTerrainStep.Preview; }
            else if (o.IsCanceled) Step = RandomTerrainStep.Preview;
            else OpenedScenario = o.Result;
        }
    }
}
