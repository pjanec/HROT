using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ U-25 / S4 (docs/UX/DESIGN_Initial_State.md §4.3) — a named <b>Point</b> (8816) on the map: a diamond (4 lines), its name, a clickable pick box; an ORIENTED
/// point adds its heading tick; any other TKB type draws nothing. 🔴 Red-proof (compiles): the type check against <c>TkbEntityTypes.DeployRole</c> ⇒ nothing drawn ⇒ red.
/// </summary>
public sealed class NamedPointGizmoTests : IDisposable
{
    private readonly EntityRepository _repo = new();

    public NamedPointGizmoTests()
    {
        _repo.RegisterComponent<SimTransform>(); _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterComponent<TkbIdentity>(); _repo.RegisterComponent<EntityInfo>();
    }

    public void Dispose() => _repo.Dispose();

    private Entity Point(long type, WorldRotation rotation, int net)
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)type });
        _repo.AddComponent(e, new SimTransform { Position = new WorldPoint(100, 200, 0), Rotation = rotation });
        _repo.AddComponent(e, new NetworkIdentity(net));
        _repo.AddComponent(e, new EntityInfo { Name = new FixedString64("LZ Alpha") });
        return e;
    }

    [Fact]
    public void U25_ANamedPoint_DrawsADiamondItsNameAndAPickBox_AnOrientedOneItsHeading()
    {
        var gizmo = new NamedPointGizmo();
        var plain = Point(TkbEntityTypes.Point, WorldRotation.Identity, 41);
        var buffer = new DebugPrimitiveBuffer();
        gizmo.Draw(_repo, plain, buffer);
        var prims = buffer.GetFrame().ToArray();
        Assert.Equal(4, prims.Count(p => p.Shape == DebugPrimitiveShape.Line));   // the diamond, no heading
        Assert.Single(prims, p => p.Shape == DebugPrimitiveShape.Text);
        Assert.Single(prims, p => DebugPrimitiveBuffer.IsPickTarget(p) && p.BoxAnchorId == 41);

        var oriented = Point(TkbEntityTypes.Point, WorldRotation.FromHeadingDeg(45f), 42);
        var b2 = new DebugPrimitiveBuffer();
        gizmo.Draw(_repo, oriented, b2);
        Assert.Equal(5, b2.GetFrame().ToArray().Count(p => p.Shape == DebugPrimitiveShape.Line));   // + the heading tick

        var other = Point(TkbEntityTypes.DeployRole, WorldRotation.Identity, 43);
        var b3 = new DebugPrimitiveBuffer();
        gizmo.Draw(_repo, other, b3);
        Assert.Empty(b3.GetFrame().ToArray());
    }
}
