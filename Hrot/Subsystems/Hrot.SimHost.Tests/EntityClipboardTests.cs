using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Macros;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ A5 / LC-3 (docs/UX/DESIGN_Entity_Copy_Paste.md §2) — Copy / Paste over the ONE slice expansion the macro expander uses: a copied group's
    /// references between its members point at the COPIES (ids pre-allocated); a reference to an entity not copied points at the original; every
    /// position moves with the anchor and turns with the heading; each copy gets a fresh derived GUID, new mission task ids, and is saved (not transient).
    /// </summary>
    public sealed class EntityClipboardTests
    {
        static EntityClipboardTests()
        {
            using var repo = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(repo);
            Hrot.CGF.CgfComponentRegistry.RegisterAll(repo);
        }

        private static readonly Guid Hq = Guid.Parse("bc000002-0000-4000-8000-000000000001");
        private static readonly Guid Leader = Guid.Parse("bc000002-0000-4000-8000-000000000002");

        private const string Group = """
        { "Header": { "SubsystemType": "%TYPE%" }, "Entities": {
          "bc000002-0000-4000-8000-000000000001": { "SimTransform": { "Position": [100, 100, 0], "Rotation": [0, 0, 0, 1] }, "TkbIdentity": { "TkbType": 2002 },
            "EntityInfo": { "Name": "Blue HQ", "ForceId": "Friend" } },
          "bc000002-0000-4000-8000-000000000002": { "SimTransform": { "Position": [110, 100, 0], "Rotation": [0, 0, 0, 1] }, "TkbIdentity": { "TkbType": 2002 },
            "EntityInfo": { "Name": "Blue Leader", "ForceId": "Friend" },
            "UnitSubordinate": { "CommanderGuid": "bc000002-0000-4000-8000-000000000001", "Designation": "SquadLeader" } } } }
        """;

        private sealed class Rig : IDisposable
        {
            public readonly EntityRepository World = new();
            public readonly MacroExpander Expander;
            public readonly EntityClipboard Clipboard;
            public readonly Dictionary<Guid, Entity> ByGuid = new();
            public readonly Dictionary<Guid, int> NetOf = new();

            public Rig()
            {
                HrotSharedComponentRegistry.RegisterAll(World);
                Hrot.CGF.CgfComponentRegistry.RegisterAll(World);
                var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(
                    new Fdp.Toolkit.Behavior.BehaviorRegistry(), null, UnknownComponentPolicy.WarnAndSkip);
                Expander = new MacroExpander(new InMemoryMacroLibrary(), new StagingEntityExtractor(), serializer);
                serializer.Deserialize(World, Group.Replace("%TYPE%", serializer.SubsystemType));
                int net = 9000;
                foreach (var e in World.Query().With<EntityGuid>().Build())
                {
                    var g = World.GetComponentRO<EntityGuid>(e).Value;
                    ByGuid[g] = e;
                    NetOf[g] = ++net;
                    World.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = net });
                }
                // the running world's command link (the intent the load wrote is applied by a system in a host): the leader under the HQ
                if (!World.IsComponentTypeRegistered<Fdp.Core.CommandHierarchy.UnitSubordinate>()) World.RegisterComponent<Fdp.Core.CommandHierarchy.UnitSubordinate>();
                World.AddComponent(ByGuid[Leader], new Fdp.Core.CommandHierarchy.UnitSubordinate { Commander = ByGuid[Hq] });
                var ids = new SequentialIdAllocator();
                Clipboard = new EntityClipboard(() => Expander, () => ids);
            }

            public void Dispose() => World.Dispose();
        }

        private static T One<T>(EntityCreationRequest r) => r.InitialComponents!.OfType<T>().Single();
        private static EntityCreationRequest Named(IReadOnlyList<EntityCreationRequest> rs, string name) => rs.Single(r => One<Fdp.Core.EntityInfo>(r).Name.ToString() == name);

        [Fact]
        public void A5_ACopiedGroup_ItsReferencesPointAtTheCopies_FreshGuids_Saved_Relocated()
        {
            using var rig = new Rig();
            Assert.True(rig.Clipboard.Copy(rig.World, new[] { rig.ByGuid[Hq], rig.ByGuid[Leader] }, rig.ByGuid[Hq]));
            Assert.Equal(2, rig.Clipboard.Count);
            var paste = Guid.NewGuid();
            var reqs = rig.Clipboard.Requests(rig.World, new WorldPoint(500, 500, 0), rig.Clipboard.Heading, paste)!;
            Assert.Equal(2, reqs.Count);
            var hq = Named(reqs, "Blue HQ"); var leader = Named(reqs, "Blue Leader");

            Assert.All(reqs, r => Assert.False(r.IsTransient));                                        // saved with the scenario, unlike a macro child
            Assert.All(reqs, r => Assert.NotEqual(0, r.PreAllocatedNetworkId));
            Assert.Equal(SliceExpander.ChildGuid(paste, "copy:", Hq), One<EntityGuid>(hq).Value);       // a fresh, derived identity
            Assert.NotEqual(Leader, One<EntityGuid>(leader).Value);
            Assert.Equal(hq.PreAllocatedNetworkId, One<Hrot.Common.Serializers.InitialUnitSubordinateIntent>(leader).CommanderNetworkId.Ref.NetworkId);   // the COPY commands the copy

            Assert.Equal(new WorldPoint(500, 500, 0), One<SimTransform>(hq).Position);                  // the anchor lands where placed
            Assert.Equal(new WorldPoint(510, 500, 0), One<SimTransform>(leader).Position);              // the layout kept (same heading)
        }

        /// <summary>🔴 Red-proof (compiles): the clipboard passing no <c>Outside</c> scope ⇒ the lone copy's commander resolves to nothing ⇒ red.</summary>
        [Fact]
        public void A5_ACopiedSubordinateAlone_KeepsItsCommander_TheOriginal()
        {
            using var rig = new Rig();
            Assert.True(rig.Clipboard.Copy(rig.World, new[] { rig.ByGuid[Leader] }, rig.ByGuid[Leader]));
            var leader = Assert.Single(rig.Clipboard.Requests(rig.World, new WorldPoint(0, 0, 0), rig.Clipboard.Heading, Guid.NewGuid())!);
            Assert.Equal(rig.NetOf[Hq], One<Hrot.Common.Serializers.InitialUnitSubordinateIntent>(leader).CommanderNetworkId.Ref.NetworkId);
        }

        [Fact]
        public void A5_ATurnedPaste_TurnsTheGroupAboutTheAnchor()
        {
            using var rig = new Rig();
            rig.Clipboard.Copy(rig.World, new[] { rig.ByGuid[Hq], rig.ByGuid[Leader] }, rig.ByGuid[Hq]);
            var turned = new Bearing(rig.Clipboard.Heading.Degrees - 90f);                             // a quarter turn to the left
            var leader = Named(rig.Clipboard.Requests(rig.World, new WorldPoint(500, 500, 0), turned, Guid.NewGuid())!, "Blue Leader");
            var p = One<SimTransform>(leader).Position;
            Assert.Equal(500f, p.X, 3); Assert.Equal(510f, p.Y, 3);                                    // 10 m east of the HQ became 10 m north
        }

        [Fact]
        public void A5_APastedPlan_GetsItsOwnTaskIds_ActiveAndOrderMapped()
        {
            var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid();
            var plan = new Fdp.Toolkit.Behavior.Components.ActiveMissionPlan
            {
                Plan = new Fdp.Toolkit.Behavior.Components.DomainMissionPlan { ActiveTaskId = t2, Tasks =
                {
                    new Fdp.Toolkit.Behavior.Components.DomainMissionTask { TaskId = t1, BehaviorName = "Order.Move" },
                    new Fdp.Toolkit.Behavior.Components.DomainMissionTask { TaskId = t2, BehaviorName = "Patrol",
                        Triggers = { new Fdp.Toolkit.Behavior.Components.DomainMissionTrigger { Type = "TimerElapsed", Params = "30" } } },
                } },
                TaskOrder = new[] { t1, t2 },
            };
            var copy = plan.WithNewTaskIds();
            Assert.NotSame(plan, copy);
            Assert.DoesNotContain(copy.Plan.Tasks, t => t.TaskId == t1 || t.TaskId == t2);
            Assert.Equal(copy.Plan.Tasks[1].TaskId, copy.Plan.ActiveTaskId);
            Assert.Equal(copy.Plan.Tasks.Select(t => t.TaskId), copy.TaskOrder);
            Assert.Equal("30", copy.Plan.Tasks[1].Triggers[0].Params);
            Assert.Equal(t1, plan.Plan.Tasks[0].TaskId);                                                // the original untouched
        }

        // ── C3: the paste tool, the actions, the keys ─────────────────────────────────────────────────────────────────────────────

        private static (Rig Rig, Hrot.ScenarioEditor.Gizmos.PasteGizmo Gizmo, int[] Removed) Tool()
        {
            var rig = new Rig();
            if (!rig.World.Bus.IsRegisteredManaged<EntityCreationRequest>()) rig.World.RegisterManagedEvent<EntityCreationRequest>();
            rig.Clipboard.Copy(rig.World, new[] { rig.ByGuid[Hq], rig.ByGuid[Leader] }, rig.ByGuid[Hq]);
            var removed = new int[1];
            return (rig, new Hrot.ScenarioEditor.Gizmos.PasteGizmo(rig.Clipboard, rig.World, () => removed[0]++), removed);
        }

        /// <summary>🔴 Red-proof (compiles): <c>PendingAt</c> ignoring the drag (always the copied heading) ⇒ the dragged heading is not 0 ⇒ red.</summary>
        [Fact]
        public void A5_ThePasteTool_FollowsTheCursor_ThenAPressAndDragTurnsAboutThePress_AReleasePastesOnce()
        {
            var (rig, tool, removed) = Tool();
            using var _ = rig;
            var copied = rig.Clipboard.Heading.Degrees;

            tool.OnDragUpdate(new System.Numerics.Vector3(300, 300, 0));
            var (at, heading) = tool.PendingAt(new System.Numerics.Vector3(300, 300, 0));
            Assert.Equal(new System.Numerics.Vector3(300, 300, 0), at);                 // follows the cursor
            Assert.Equal(copied, heading, 3);                                          // keeps its own heading

            tool.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, true, new System.Numerics.Vector3(300, 300, 0));
            (at, heading) = tool.PendingAt(new System.Numerics.Vector3(300, 320, 0));   // dragged 20 m north
            Assert.Equal(new System.Numerics.Vector3(300, 300, 0), at);                 // the anchor stays at the press
            Assert.Equal(0f, heading, 3);                                              // facing the drag: north

            tool.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, false, new System.Numerics.Vector3(300, 320, 0));
            Assert.Equal(1, tool.Pasted);
            Assert.Equal(1, removed[0]);                                               // one paste per arming
        }

        /// <summary>⭐ U-8 (CE-4339) — the Paste tool's feature suite: while the press is dragged the tool draws a direction line from the press point (the group's anchor) toward the cursor; button up (a plain follow) draws none. 0 B steady while dragging.
        /// 🔴 Red-proof (compiles): <c>PasteGizmo.UpdateAndDraw</c> not calling <c>PlacementDirectionLine.Draw</c> ⇒ no amber line ⇒ red.</summary>
        [Fact]
        public void U8_ThePasteTool_DrawsADirectionLine_WhileThePressIsDragged()
        {
            var (rig, tool, _) = Tool();
            using var _r = rig;
            var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(256);
            var amber = Hrot.ScenarioEditor.Gizmos.PlacementDirectionLine.Colour;
            int Count(Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer b, out System.Numerics.Vector3 start, out System.Numerics.Vector3 end)
            {
                int n = 0; start = end = default;
                foreach (var p in b.GetFrame())
                    if (p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Line && p.Color.R == amber.R && p.Color.G == amber.G && p.Color.B == amber.B)
                    { if (n == 0) { start = p.LineStart; end = p.LineEnd; } n++; }
                return n;
            }
            tool.OnDragUpdate(new System.Numerics.Vector3(300, 300, 0));
            tool.UpdateAndDraw(rig.World, 0f, buffer);
            Assert.Equal(0, Count(buffer, out _, out _));                                // following the cursor: no direction yet

            tool.OnMouseEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton.Left, true, new System.Numerics.Vector3(300, 300, 0));
            tool.OnDragUpdate(new System.Numerics.Vector3(300, 340, 0));                  // dragged 40 m north
            for (int i = 0; i < 5; i++) { buffer.Clear(); tool.UpdateAndDraw(rig.World, 0f, buffer); }
            Assert.Equal(3, Count(buffer, out var start, out var end));                  // the line and its two head strokes
            Assert.Equal(new System.Numerics.Vector3(300, 300, 0), start);               // from the group's anchor (the press)
            Assert.Equal(new System.Numerics.Vector3(300, 340, 0), end);                 // toward the cursor
            long before = GC.GetAllocatedBytesForCurrentThread();
            buffer.Clear(); tool.UpdateAndDraw(rig.World, 0f, buffer);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);            // R-2084: still a steady frame
        }

        [Fact]
        public void A5_ThePasteToolsFrame_AllocatesNothing_EscCancelsWithoutPasting()
        {
            var (rig, tool, removed) = Tool();
            using var _ = rig;
            var buffer = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer(256);
            tool.OnDragUpdate(new System.Numerics.Vector3(10, 10, 0));
            for (int i = 0; i < 5; i++) { tool.UpdateAndDraw(rig.World, 0f, buffer); buffer.Clear(); }   // warm: one frame does not settle the one-time costs (measured: 88 B on the 2nd)
            long before = GC.GetAllocatedBytesForCurrentThread();
            tool.UpdateAndDraw(rig.World, 0f, buffer);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);          // R-2084: a steady frame
            tool.OnKeyEvent(Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey.Escape, true);
            Assert.Equal(1, removed[0]);
            Assert.Equal(0, tool.Pasted);
        }

        [Fact]
        public void A5_CopyTakesTheSelection_WhenTheClickedOneIsInIt_ElseTheClickedOneAlone()
        {
            using var rig = new Rig();
            var selection = new Fdp.Toolkit.Vis2D.Defaults.DefaultSelectionState();
            selection.Add(rig.ByGuid[Hq]); selection.Add(rig.ByGuid[Leader]);
            var actions = new Hrot.UI.Common.AddEntity.CopyPasteActions(() => null, selection);
            Assert.Equal(2, actions.TargetsOf(rig.ByGuid[Leader]).Count);
            selection.Clear(); selection.Add(rig.ByGuid[Hq]);
            Assert.Equal(new[] { rig.ByGuid[Leader] }, actions.TargetsOf(rig.ByGuid[Leader]));
        }

        [Fact]
        public void A5_ACtrlShortcut_MatchesOnlyAModifiedPress_APlainLetterOnlyAPlainOne()
        {
            Assert.Equal('C', Hrot.ScenarioEditor.Map.MenuShortcuts.KeyOf("Ctrl+C", out bool ctrl)); Assert.True(ctrl);
            Assert.Null(Hrot.ScenarioEditor.Map.MenuShortcuts.KeyOf("Ctrl+C"));                       // the plain reading: not a plain letter
            var items = new[]
            {
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 1, Label = "Center", Shortcut = "C" },
                new Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto { Id = 2, Label = "Copy", Shortcut = "Ctrl+C" },
            };
            Assert.Equal(1, Hrot.ScenarioEditor.Map.MenuShortcuts.ActionFor(items, 'C', modified: false));
            Assert.Equal(2, Hrot.ScenarioEditor.Map.MenuShortcuts.ActionFor(items, 'C', modified: true));
        }
    }
}
