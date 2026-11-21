using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Common.Diagnostics.Gizmos;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ §6x (docs/DESIGN_Map_3D_Mode.md FX-1 … FX-3; Indirect Fire §3.4 FM-3) — the FIRE MISSION's aim points: every gun laid or firing on
/// a POINT with an INDIRECT mount (its replicated <see cref="WeaponPosture"/> AIMED at a point — <c>FireAtPoint</c> reports it while it waits
/// for its fire time and while it fires) gets, at that point, a ring the size of its round's blast injury radius and a small cross in its
/// side colour, so a battery's sheaf reads from the rings' pattern on every map host; a SELECTED gun also gets a dashed line to its point.
/// Nothing new on the wire: a gun whose rounds are gone drops its mark by itself. A passive global gizmo like
/// <see cref="DesignationOverlayGizmo"/>, registered by <c>MapInteractionPack</c>.
/// </summary>
public sealed class FireMissionOverlayGizmo : IEntityStatefulGizmo
{
    /// <summary>The ring's radius when the round names no blast (m).</summary>
    public const float DefaultRadiusM = 20f;

    private readonly Func<IReadOnlyCollection<Entity>> _selected;
    private readonly List<(Entity Gun, Vector3 Aim, float Radius)> _aims = new();

    public FireMissionOverlayGizmo(Func<IReadOnlyCollection<Entity>> selected)
        => _selected = selected ?? throw new ArgumentNullException(nameof(selected));

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    /// <summary>The marks drawn by the last frame (rails).</summary>
    public int MarksDrawn { get; private set; }
    public int LinesDrawn { get; private set; }

    /// <summary>Every gun of <paramref name="world"/> aimed at a point with an indirect weapon: the gun, its aim point and its round's ring.</summary>
    public static void AimsOf(EntityRepository world, List<(Entity Gun, Vector3 Aim, float Radius)> into)
    {
        into.Clear();
        if (!world.IsComponentTypeRegistered<WeaponPosture>()) return;
        foreach (var e in world.Query().With<WeaponPosture>().Build())
        {
            ref readonly var p = ref world.GetComponentRO<WeaponPosture>(e);
            if (p.Kind != WeaponPostureKind.Aimed || !p.Target.IsNull || p.Point == Vector3.Zero) continue;
            if (IndirectMount(world, e, p.ActiveMount) is not { } mount) continue;
            var warhead = CombatTkb.WarheadOf(world, mount);
            float r = warhead is { BlastInjuryRadiusM: > 0f } w ? w.BlastInjuryRadiusM : warhead is { FragmentRadiusM: > 0f } f ? f.FragmentRadiusM : DefaultRadiusM;
            into.Add((e, p.Point, r));
        }
    }

    /// <summary>The mount <paramref name="index"/> of <paramref name="unit"/> fires with, when it is an INDIRECT one: its own mount, or the
    /// weapon system it controls at 32 + k (that weapon's first mount).</summary>
    public static WeaponMountDto? IndirectMount(EntityRepository world, Entity unit, int index)
    {
        var mount = WeaponSystems.IsControlledIndex(index) && WeaponSystems.ControlledAt(world, unit, index) is { IsNull: false } weapon
            ? CombatTkb.MountOf(world, weapon, 0)
            : CombatTkb.MountOf(world, unit, index);
        return mount is { Arc: not IndirectArc.Unset } ? mount : null;
    }

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        MarksDrawn = LinesDrawn = 0;
        if (view is not EntityRepository world) return;
        AimsOf(world, _aims);
        if (_aims.Count == 0) return;
        var selected = _selected();
        bool sides = world.IsComponentTypeRegistered<EntityInfo>();
        foreach (var (gun, aim, radius) in _aims)
        {
            var colour = sides && world.HasComponent<EntityInfo>(gun) ? SidePalette.Of(world.GetComponentRO<EntityInfo>(gun).ForceId) : SidePalette.Neutral;
            draw.DrawSphere(aim, radius, colour, thickness: 2f, sizeMode: SizeMode.WorldMeters, style: LineStyle.Dashed);
            float c = MathF.Max(2f, radius * 0.15f);
            draw.DrawLine(aim + new Vector3(-c, -c, 0f), aim + new Vector3(c, c, 0f), colour, 2f);
            draw.DrawLine(aim + new Vector3(-c, c, 0f), aim + new Vector3(c, -c, 0f), colour, 2f);
            MarksDrawn++;
            if (selected.Contains(gun) && world.HasComponent<SimTransform>(gun))
            {
                draw.DrawLine(world.GetComponentRO<SimTransform>(gun).Position, aim, colour, 1.5f, style: LineStyle.Dashed);
                LinesDrawn++;
            }
        }
    }

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnDragUpdate(Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMenuAction(int actionId) { }
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    public void Dispose() { }
}
