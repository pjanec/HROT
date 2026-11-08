using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>How a group rotate turns the selection: every unit about ITS OWN centre (heading only), or the whole group about its centre (positions too).</summary>
public enum GroupRotateMode
{
    /// <summary>Each unit's heading turns by the angle; positions stay.</summary>
    Each = 0,
    /// <summary>Positions swing round the group's centroid by the angle and every heading turns by it too.</summary>
    AboutCentre = 1,
}

/// <summary>
/// ⭐⭐ B-17 P5 (📄 docs/UX/DESIGN_Map_Interaction.md §2.1 <c>GroupTransform</c>, §2.5 "multi-select") — move and rotate the WHOLE SELECTION as ONE gesture.
/// <para>The drag gizmo and the rotator gizmo both go through it, so a gesture means the same thing whichever gizmo started it: <see cref="Begin"/> brackets ONE
/// undo step (<see cref="EditSteps"/>), the preview writes the local components (as the single-unit drag always did), and <see cref="Commit"/> asks the OWNERS
/// through <see cref="IEntityComponentWriter"/> — one write per unit, all inside that one step. ⛔ Never N steps.</para>
/// <para>Angles are math yaw radians (counter-clockwise in the map's XY plane, the convention <c>SimMath.ExtractYaw</c> reads); a heading is written to the wire as compass degrees,
/// as <see cref="EntityRotatorGizmo"/> always did.</para>
/// </summary>
public sealed class GroupTransform
{
    private struct Member
    {
        public Entity Entity;
        public Vector3 Pos0, Pos;
        public float Yaw0, Yaw;
        public Quaternion Rot0;
    }

    private readonly EntityRepository _repo;
    private readonly ISelectionState _selection;
    private readonly IEntityComponentWriter? _writer;
    private readonly List<Member> _members = new();
    private bool _open, _movesPosition, _turns;
    private Vector2 _centre;

    /// <param name="writer">The write router; null keeps the direct local write (correct on a one-node editor).</param>
    public GroupTransform(EntityRepository repo, ISelectionState selection, IEntityComponentWriter? writer = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _writer = writer;
    }

    /// <summary>The group a gesture on <paramref name="grabbed"/> means: the whole selection when <paramref name="grabbed"/> is part of a selection of two or more transformable units, else null (a single unit is the ordinary gizmo's).</summary>
    public static GroupTransform? ForGrab(EntityRepository repo, ISelectionState selection, IEntityComponentWriter? writer, Entity grabbed)
    {
        var g = new GroupTransform(repo, selection, writer);
        int count = 0; bool has = false;
        foreach (var e in selection.SelectedEntities)
        {
            if (!g.IsMember(e)) continue;
            count++;
            if (e == grabbed) has = true;
        }
        return has && count > 1 ? g : null;
    }

    private bool IsMember(Entity e) => !e.IsNull && _repo.IsAlive(e) && _repo.HasComponent<SimTransform>(e);

    /// <summary>The units of the group (live while no gesture is open; the captured set during one).</summary>
    public IReadOnlyList<Entity> Members
    {
        get
        {
            var list = new List<Entity>();
            if (_open) { foreach (var m in _members) list.Add(m.Entity); }
            else foreach (var e in _selection.SelectedEntities) if (IsMember(e)) list.Add(e);
            return list;
        }
    }

    /// <summary>Whether a gesture is open (between <see cref="Begin"/> and <see cref="Commit"/> / <see cref="Cancel"/>).</summary>
    public bool IsOpen => _open;

    /// <summary>The centroid of the units as they were when the gesture began (or as they are now, before one).</summary>
    public Vector2 Centre
    {
        get
        {
            if (_open) return _centre;
            return CentroidOf(Members, _repo);
        }
    }

    /// <summary>The distance from <see cref="Centre"/> to the farthest unit — the radius of the ring drawn round a group rotate.</summary>
    public float Radius
    {
        get
        {
            var c = Centre; float r = 0f;
            foreach (var e in Members)
            {
                var p = _repo.GetComponentRO<SimTransform>(e).Position;
                r = MathF.Max(r, Vector2.Distance(c, new Vector2(p.X, p.Y)));
            }
            return r;
        }
    }

    /// <summary>The current yaw (math radians) of <paramref name="e"/> in the open gesture's preview, or its live one.</summary>
    public float YawOf(Entity e)
    {
        foreach (var m in _members) if (_open && m.Entity == e) return m.Yaw;
        return _repo.IsAlive(e) && _repo.HasComponent<SimTransform>(e) ? SimMath.ExtractYaw(_repo.GetComponentRO<SimTransform>(e).Rotation) : 0f;
    }

    /// <summary>The position <paramref name="e"/> has in the open gesture's preview, or its live one.</summary>
    public Vector3 PositionOf(Entity e)
    {
        foreach (var m in _members) if (_open && m.Entity == e) return m.Pos;
        return _repo.IsAlive(e) && _repo.HasComponent<SimTransform>(e) ? _repo.GetComponentRO<SimTransform>(e).Position : default;
    }

    private static Vector2 CentroidOf(IReadOnlyList<Entity> es, EntityRepository repo)
    {
        if (es.Count == 0) return default;
        Vector2 sum = default;
        foreach (var e in es) { var p = repo.GetComponentRO<SimTransform>(e).Position; sum += new Vector2(p.X, p.Y); }
        return sum / es.Count;
    }

    /// <summary>
    /// Opens ONE gesture: captures the selection (its units' positions and headings are the gesture's "before") and begins the undo step BEFORE anything is written.
    /// Returns false (and opens nothing) when the selection holds no transformable unit.
    /// </summary>
    public bool Begin(string label)
    {
        if (_open) return true;
        _members.Clear();
        foreach (var e in _selection.SelectedEntities)
        {
            if (!IsMember(e)) continue;
            ref readonly var tf = ref _repo.GetComponentRO<SimTransform>(e);
            float yaw = SimMath.ExtractYaw(tf.Rotation);
            _members.Add(new Member { Entity = e, Pos0 = tf.Position, Pos = tf.Position, Yaw0 = yaw, Yaw = yaw, Rot0 = tf.Rotation });
        }
        if (_members.Count == 0) return false;
        var es = new List<Entity>(_members.Count);
        foreach (var m in _members) es.Add(m.Entity);
        _centre = CentroidOf(es, _repo);
        _movesPosition = _turns = false;
        _open = true;
        EditSteps.Begin(_repo, label);
        return true;
    }

    /// <summary>Preview: every unit sits at its original position plus <paramref name="delta"/> (idempotent — always from the originals).</summary>
    public void PreviewMove(Vector2 delta)
    {
        if (!_open) return;
        _movesPosition = true; _turns = false;
        for (int i = 0; i < _members.Count; i++)
        {
            var m = _members[i];
            m.Pos = new Vector3(m.Pos0.X + delta.X, m.Pos0.Y + delta.Y, m.Pos0.Z);
            m.Yaw = m.Yaw0;
            _members[i] = m;
            Apply(m);
        }
    }

    /// <summary>Preview: every heading turns by <paramref name="angleRad"/>; with <see cref="GroupRotateMode.AboutCentre"/> the positions also swing round the centroid.</summary>
    public void PreviewRotate(float angleRad, GroupRotateMode mode)
    {
        if (!_open) return;
        _turns = true; _movesPosition = mode == GroupRotateMode.AboutCentre;
        float cos = MathF.Cos(angleRad), sin = MathF.Sin(angleRad);
        for (int i = 0; i < _members.Count; i++)
        {
            var m = _members[i];
            m.Yaw = m.Yaw0 + angleRad;
            if (mode == GroupRotateMode.AboutCentre)
            {
                float dx = m.Pos0.X - _centre.X, dy = m.Pos0.Y - _centre.Y;
                m.Pos = new Vector3(_centre.X + dx * cos - dy * sin, _centre.Y + dx * sin + dy * cos, m.Pos0.Z);
            }
            else m.Pos = m.Pos0;
            _members[i] = m;
            Apply(m);
        }
    }

    /// <summary>Move the whole selection by <paramref name="delta"/> — ONE gesture, ONE undo step.</summary>
    public bool Move(Vector2 delta)
    {
        if (!Begin("Move")) return false;
        PreviewMove(delta);
        Commit();
        return true;
    }

    /// <summary>Rotate the whole selection by <paramref name="angleRad"/> — ONE gesture, ONE undo step.</summary>
    public bool Rotate(float angleRad, GroupRotateMode mode)
    {
        if (!Begin("Rotate")) return false;
        PreviewRotate(angleRad, mode);
        Commit();
        return true;
    }

    /// <summary>
    /// Ends the gesture: each unit's result goes to its owner through the writer (position, heading, or both — what the gesture changed), then the ONE step closes.
    /// A unit the writer refuses is put back where it began (nothing was written, nobody asked).
    /// </summary>
    public void Commit()
    {
        if (!_open) return;
        try
        {
            if (_writer == null) return;   // the preview already wrote the local components: correct on a one-node editor
            for (int i = 0; i < _members.Count; i++)
            {
                var m = _members[i];
                if (!_repo.IsAlive(m.Entity)) continue;
                var changes = new List<EntityAttributeChange>(3);
                if (_movesPosition)
                {
                    changes.Add(EntityAttributeChange.Double(AttributeIds.PositionX, m.Pos.X));
                    changes.Add(EntityAttributeChange.Double(AttributeIds.PositionY, m.Pos.Y));
                }
                if (_turns) changes.Add(EntityAttributeChange.Double(AttributeIds.Heading, SimMath.YawRadToCompassDeg(m.Yaw)));
                if (changes.Count == 0) continue;
                if (_writer.Write(m.Entity, changes) == EntityWriteRoute.Refused)
                {
                    var back = m; back.Pos = m.Pos0; back.Yaw = m.Yaw0;
                    Apply(back, restore: true);
                }
            }
        }
        finally { _open = false; EditSteps.End(_repo); }
    }

    /// <summary>Abandons the gesture: every unit goes back where it began, and the step records nothing.</summary>
    public void Cancel()
    {
        if (!_open) return;
        foreach (var m in _members)
        {
            var back = m; back.Pos = m.Pos0; back.Yaw = m.Yaw0;
            Apply(back, restore: true);
        }
        _open = false;
        EditSteps.Cancel(_repo);
    }

    /// <summary>The preview write — local, like the single-unit drag's (a moved unit is stationary: its speed resets).</summary>
    private void Apply(Member m, bool restore = false)
    {
        if (!_repo.IsAlive(m.Entity) || !_repo.HasComponent<SimTransform>(m.Entity)) return;
        ref var tf = ref _repo.GetComponentRW<SimTransform>(m.Entity);
        tf.Position = m.Pos;
        if (restore) tf.Rotation = m.Rot0;
        else if (_turns) tf.Rotation = SimMath.FromYaw(m.Yaw);
        if (_movesPosition && _repo.HasComponent<VehicleState>(m.Entity))
        {
            ref var vs = ref _repo.GetComponentRW<VehicleState>(m.Entity);
            vs.Speed = 0;
        }
    }
}
