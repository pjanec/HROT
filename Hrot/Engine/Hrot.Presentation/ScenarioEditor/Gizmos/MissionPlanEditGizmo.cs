using System;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Map.Common.Components;
using Hrot.Presentation.Orders;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐⭐ W-1 / P-8 8d-2 (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8 E2, §5.12; mockup <c>mission-plan-editor.svg</c>) — the selected unit's
    /// plan EDITED on the map: a handle on each task whose place is a point (<see cref="MissionPlanPlaces.IsMovable"/>), at its marker
    /// (<see cref="MissionPresentationGizmo.PositionsOf"/> — the ONE answer); dragging one moves that task's FIRST place field
    /// (<see cref="MissionPlanPlaces.MovePlace"/>) and commits the WHOLE plan through <see cref="IMissionEditorService.CommitMissionAsync"/> — the
    /// Orders tab's commit: one undo step in Edit, conflict-checked in Live. ⛔ It draws no route or markers of its own — the plan drawing
    /// (<see cref="MissionPresentationGizmo"/>) stays the one drawing; this adds the handles and the drag's preview. Right-click or Escape ends it.
    /// </summary>
    public sealed class MissionPlanEditGizmo : IEntityStatefulGizmo
    {
        // ⭐ fidelity (mission-plan-drag.png): the handle is a translucent pad AROUND the marker — an opaque one hid the marker and its number
        private static readonly Rgba32 Handle = new(255, 255, 255, 60), HandleActive = new(242, 192, 51, 70), Rim = new(255, 255, 255, 230),
                                       Active = new(242, 192, 51), Preview = new(242, 192, 51, 200);

        private readonly EntityRepository _repo;
        private readonly Entity _entity;
        private readonly int _networkId;
        private readonly Action _onRemove;
        private readonly IMissionEditorService _missions;
        private readonly Vector3[] _at = new Vector3[MissionPlanQueue.MaxTasks];   // reused every frame (R-2084)

        private int _dragging = -1;
        private Vector3 _dragAt;
        // ⭐ after a commit the moved marker is drawn where it was dropped until the plan value changes (or the commit settles): no jump back
        private int _pendingTask = -1;
        private Vector3 _pendingAt;
        private ActiveMissionPlan? _pendingFor;
        private Task<MissionCommitResult>? _pending;

        public bool RequiresExclusiveFocus => true;
        public bool WantsRawInput => true;
        public bool IsFocused { get; private set; }
        public void SetFocus(bool isFocused) => IsFocused = isFocused;

        /// <summary>The last commit's outcome (rails); null while none settled.</summary>
        public MissionCommitResult? LastResult { get; private set; }

        public MissionPlanEditGizmo(ISimulationView view, Entity entity, int networkId, Action onRemove, IMissionEditorService missions)
        {
            _repo = view as EntityRepository ?? throw new ArgumentException("MissionPlanEditGizmo requires EntityRepository access.", nameof(view));
            _entity = entity;
            _networkId = networkId;
            _onRemove = onRemove ?? throw new ArgumentNullException(nameof(onRemove));
            _missions = missions ?? throw new ArgumentNullException(nameof(missions));
        }

        public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
        {
            if (_pending is { IsCompleted: true } done) { LastResult = done.Status == TaskStatus.RanToCompletion ? done.Result : null; _pending = null; _pendingTask = -1; }
            if (!IsFocused || !_repo.IsAlive(_entity) || !_repo.HasManagedComponent<ActiveMissionPlan>(_entity)) return;   // §4.7i — a suspended tool draws no handles
            var plan = ((ISimulationView)_repo).GetManagedComponentRO<ActiveMissionPlan>(_entity);
            if (!ReferenceEquals(plan, _pendingFor)) _pendingTask = -1;   // the plan moved: the drawing shows the committed place
            int n = MissionPresentationGizmo.PositionsOf(view, _entity, plan, _at);
            var places = MissionPlanPlaces.Of(plan);
            for (int i = 0; i < n; i++)
            {
                if (!MissionPlanPlaces.IsMovable(places[i])) continue;
                var p = i == _dragging ? _dragAt : i == _pendingTask ? _pendingAt : _at[i];
                if (i == _dragging || i == _pendingTask)
                {
                    // the preview: the moved marker and the legs to its neighbours (the committed drawing still shows the old ones)
                    Vector3 from = i == 0 ? (Vector3)view.GetComponentRO<SimTransform>(_entity).Position : _at[i - 1];
                    draw.DrawLine(from, p, Preview, 2f, SizeMode.ScreenPixels);
                    if (i + 1 < n) draw.DrawLine(p, _at[i + 1], Preview, 2f, SizeMode.ScreenPixels);
                    draw.DrawSphere(p, 10f, Rim, thickness: 1.2f, sizeMode: SizeMode.ScreenPixels, fillColor: Active);
                }
                var prim = default(DebugPrimitive);
                prim.Shape = DebugPrimitiveShape.Box2D;
                prim.Space = CoordinateSpace.World;
                prim.TargetView = PipelineTarget.Map2D;
                prim.BoxCenterX = p.X;
                prim.BoxCenterY = p.Y;
                prim.BoxExtentX = 9f;
                prim.BoxExtentY = 9f;
                prim.Color = i == _dragging ? HandleActive : Handle;
                prim.SubElementId = (ushort)(i + 1);
                prim.BoxAnchorId = _networkId;   // §6.7 — the handle is addressed by the unit's network id (the tool armed only with one)
                draw.EmitRaw(in prim);
            }
        }

        public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos)
        {
            int i = (int)token.SubElementId - 1;
            _dragging = -1;
            if (i < 0 || !_repo.HasManagedComponent<ActiveMissionPlan>(_entity)) return;
            var places = MissionPlanPlaces.Of(((ISimulationView)_repo).GetManagedComponentRO<ActiveMissionPlan>(_entity));
            if (i >= places.Length || !MissionPlanPlaces.IsMovable(places[i])) return;
            _dragging = i;
            _dragAt = worldPos;
        }

        public void OnDragUpdate(Vector3 worldPos) { if (_dragging >= 0) _dragAt = worldPos; }

        public void OnCommit(Vector3 worldPos)
        {
            if (_dragging < 0) return;
            int i = _dragging;
            _dragging = -1;
            Commit(i, new Vector2(worldPos.X, worldPos.Y));
        }

        /// <summary>
        /// Task <paramref name="taskIndex"/>'s place moved to <paramref name="to"/>: the plan read from the facade (its version is the commit's
        /// precondition), that task's params rewritten, the whole plan committed. <c>false</c> when nothing was sent (no such task, no point place).
        /// </summary>
        public bool Commit(int taskIndex, Vector2 to)
        {
            if (!_repo.HasManagedComponent<ActiveMissionPlan>(_entity)) return false;
            var current = ((ISimulationView)_repo).GetManagedComponentRO<ActiveMissionPlan>(_entity);
            if (current?.Plan?.Tasks is not { } tasks || taskIndex < 0 || taskIndex >= tasks.Count) return false;
            var (plan, version) = _missions.GetMissionSnapshot(_networkId);
            if (plan == null) return false;
            var id = tasks[taskIndex].TaskId;
            var task = plan.Tasks.Find(t => t.TaskId == id);   // a user action, not a frame: allocation is fine
            if (task == null || !MissionPlanPlaces.MovePlace(task.BehaviorId, task.BehaviorParams, to, out var json)) return false;
            task.BehaviorParams = json;
            _pendingTask = taskIndex;
            _pendingAt = new Vector3(to.X, to.Y, _at[Math.Min(taskIndex, _at.Length - 1)].Z);
            _pendingFor = current;
            _pending = _missions.CommitMissionAsync(_networkId, plan, version);
            return true;
        }

        public void OnCancel() => _dragging = -1;

        public void OnMenuAction(int actionId) { }

        public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos)
        {
            if (button == MapMouseButton.Right && !isPressed) _onRemove();
        }

        public void OnKeyEvent(MapKeyboardKey key, bool isPressed)
        {
            if (key == MapKeyboardKey.Escape && isPressed) { OnCancel(); _onRemove(); }
        }

        public void Dispose() { }
    }
}
