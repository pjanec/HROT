using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Layers;

/// <summary>
/// ⭐ U-10 / M1 (docs/UX/DESIGN_Show_On_Map.md §2) — the map side of <see cref="MapPreview"/>: added by the ONE map attach (every host — editor, CGF, SimHost,
/// player, Replay Browser) just before the gizmo layer, it writes the preview's shapes into the host's LOCAL primitive buffer as the map draws, so the
/// existing 2-D / 3-D renderers draw them with everything else — no second overlay. ⭐ Written at DRAW time, after the frame's egress and before the next
/// <c>EndFrame</c>: nothing reaches the wire or a recording. ⛔ Not a scene layer: a film pass never runs it.
/// </summary>
public sealed class MapPreviewLayer : IMapLayer
{
    private readonly DebugPrimitiveBuffer _buffer;
    private readonly Func<LiveRefScope?> _refs;
    private bool _pending;

    public MapPreviewLayer(MapPreview preview, DebugPrimitiveBuffer buffer, Func<LiveRefScope?> refs)
    {
        Preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _refs = refs ?? throw new ArgumentNullException(nameof(refs));
    }

    public MapPreview Preview { get; }
    public string Name => "Show on map";
    public int LayerBitIndex => -1;   // always drawn
    public bool Has3D => true;

    public void Update(float dt) => _pending = true;
    public void Draw(RenderContext ctx) => Emit();
    public void Draw3D(RenderContext ctx) => Emit();

    /// <summary>Once per frame, in whichever view draws first.</summary>
    private void Emit()
    {
        if (!_pending) return;
        _pending = false;
        Preview.Emit(_buffer, _refs());
    }

    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
