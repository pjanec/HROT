using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace Hrot.UI.Common.Map2D;

/// <summary>
/// ⭐ L-12 / CE-4311 (docs/UX/DESIGN_Scenario_Lifecycle_Review.md LC-1, R-4166) — <b>an empty scenario says what to do next</b>: while a terrain is loaded, the
/// station is in Edit and the world holds no entity yet, the map shows <i>"Empty scenario — right-click the map ▸ Add Entity"</i> in its centre; gone after the
/// first entity. ⭐ On every map host by construction (<c>MapInteractionPack.AttachMapLayers</c>, beside <see cref="TerrainFramingLayer"/>). ⭐ 0 B per frame:
/// one cached query per world, read through its allocation-free <c>IsEmpty</c>.
/// </summary>
public sealed class EmptyScenarioHintLayer : IMapLayer
{
    /// <summary>The hint, in the design's words (the "or press A" of LC-1 is left out: no such key is bound).</summary>
    public const string Text = "Empty scenario — right-click the map ▸ Add Entity";

    private const int FontSize = 20;
    private readonly Func<EntityRepository?> _world;
    private EntityRepository? _queried;
    private EntityQuery? _entities;

    public EmptyScenarioHintLayer(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    public string Name => "EmptyScenarioHint";
    public int LayerBitIndex => -1;

    /// <summary>⭐ U-12 (CE-4129) — what the map says in a RUN (Preview / Live) over a world with no entity: a play over a world that is empty (or still loading) must not read as "nothing happens".</summary>
    public const string RunningText = "No entities in this world - the scenario is empty or still loading";

    /// <summary>Whether the hint shows now: a terrain is loaded, the station edits, and nothing has been placed.</summary>
    public bool Shows() => Hint(Hrot.Common.Editing.EditingMode.Edit);

    /// <summary>⭐ U-12 (CE-4129) — whether the run notice (<see cref="RunningText"/>) shows now: a terrain is loaded, the station is in a Preview or Live run, and the world holds no entity.</summary>
    public bool ShowsRunNotice()
    {
        var w = _world();
        var mode = w is null ? Hrot.Common.Editing.EditingMode.Edit : Hrot.Common.Editing.StationMode.Of(w);
        return mode is Hrot.Common.Editing.EditingMode.Preview or Hrot.Common.Editing.EditingMode.Live && Hint(mode);
    }

    private bool Hint(Hrot.Common.Editing.EditingMode mode)
    {
        var w = _world();
        if (w is null || TerrainFramingLayer.BoundsOf(w) is null) return false;
        if (Hrot.Common.Editing.StationMode.Of(w) != mode) return false;
        if (!w.IsComponentTypeRegistered<EntityInfo>()) return true;
        if (!ReferenceEquals(w, _queried)) { _queried = w; _entities = w.Query().With<EntityInfo>().Build(); }
        return _entities!.IsEmpty;
    }

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }

    public void DrawOverlay(RenderContext ctx)
    {
        string? text = Shows() ? Text : ShowsRunNotice() ? RunningText : null;
        if (text is null) return;
        var viewport = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        var centre = OpenMapArea.Centre(viewport);
        int width = Raylib.MeasureText(text, FontSize);
        int x = (int)(centre.X - width / 2f), y = (int)(centre.Y - FontSize / 2f);
        Raylib.DrawRectangle(x - 10, y - 6, width + 20, FontSize + 12, new Color(0, 0, 0, 150));
        Raylib.DrawText(text, x, y, FontSize, new Color(230, 230, 230, 255));
    }

    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
