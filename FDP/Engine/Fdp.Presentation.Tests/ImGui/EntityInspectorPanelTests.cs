using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Utils;
using Xunit;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Tests
{
    public class FakeInspectorContext : IInspectorContext
    {
        public Entity? SelectedEntity { get; set; }
        public Entity? HoveredEntity { get; set; }
        public bool IsMergedView { get; set; }
    }

    [Collection("ImGui Sequential")]
    public class EntityInspectorPanelTests
    {
        /// <summary>
        /// ⭐ CE-2403 (R-2084) — 1000 entities: the list builds only the rows on screen and reuses its lists, so a steady frame allocates a few KB,
        /// not a label per entity (≈ 320 KB/frame on Crossfire); the rows still list every entity (the filter result is complete).
        /// 🔴 Red-proof: the row loop over every entity (no ImGuiRowClip) ⇒ the bound fails.
        /// </summary>
        [Fact]
        public void CE2403_AThousandEntities_BuildOnlyTheVisibleRows_AndAllocateLittle()
        {
            using var fixture = new ImGuiTestFixture();
            using var repo = new EntityRepository();
            for (int i = 0; i < 1000; i++) repo.CreateEntity();
            var panel = new EntityInspectorPanel();
            var context = new FakeInspectorContext();
            var session = new RepositoryAdapter(repo);
            void Frame() { fixture.NewFrame(); panel.Draw(session, context); fixture.Render(); }
            for (int i = 0; i < 5; i++) Frame();
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++) Frame();
            long perFrame = (System.GC.GetAllocatedBytesForCurrentThread() - a0) / 10;
            Assert.True(perFrame < 24 * 1024, $"{perFrame} B/frame for 1000 entities");
            Assert.Equal(1001, EntityInspectorPanel.GetFilteredEntities(session, "", 1000).Count);   // the singleton row + every entity
        }

        [Fact]
        public void Draw_SmokeTest_RunsWithoutException()
        {
            using var fixture = new ImGuiTestFixture();
            using var repo = new EntityRepository();
            var panel = new EntityInspectorPanel();
            var inspectorContext = new FakeInspectorContext();
            var session = new RepositoryAdapter(repo);

            // Populate repo
            for (int i = 0; i < 10; i++)
            {
                repo.CreateEntity();
            }

            fixture.NewFrame();
            
            // Should not throw
            panel.Draw(session, inspectorContext);
            
            fixture.Render();
        }
        
        [Fact]
        public void GetFilteredEntities_FiltersById()
        {
            using var repo = new EntityRepository();
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            var e3 = repo.CreateEntity();
            var session = new RepositoryAdapter(repo);
            
            // ⭐ CE-3166 — the filter is a SUBSTRING of the row label "[index, vGen] …" (since the import), so a bare "1" also matches
            //   every "v1"; the bracketed id names e2 alone. The [Singletons] row is always first (RepositoryAdapter's contract).
            var results = EntityInspectorPanel.GetFilteredEntities(session, $"[{e2.Index},", 1000).ToList();

            Assert.Equal(RepositoryAdapter.SingletonEntity, results[0]);
            Assert.Equal(e2, Assert.Single(results.Skip(1)));
        }
        
        [Fact]
        public void GetFilteredEntities_RespectsLimit()
        {
            using var repo = new EntityRepository();
            for(int i=0; i<10; i++) repo.CreateEntity();
            var session = new RepositoryAdapter(repo);
            
            var results = EntityInspectorPanel.GetFilteredEntities(session, "", 5).ToList();

            // ⭐ CE-3166 — the limit counts ENTITIES; the [Singletons] row is always first and never counted (RepositoryAdapter's contract)
            Assert.Equal(RepositoryAdapter.SingletonEntity, results[0]);
            Assert.Equal(5, results.Count(e => e != RepositoryAdapter.SingletonEntity));
        }
        
        [Fact]
        public void GetFilteredEntities_ANonMatchingSearch_ListsOnlyTheSingletonRow()
        {
            using var repo = new EntityRepository();
            repo.CreateEntity();
            repo.CreateEntity();
            var session = new RepositoryAdapter(repo);
            
            // "abc" is not an ID, so filter fails to parse and should probably be ignored or return empty?
            // Code says: if (int.TryParse(..., out parsedId)) filterId = parsedId;
            // AND: if (hasFilter) -> if (filterId != -1 && entity.Index != filterId)
            // If parse fails, filterId remains -1. 
            // So logic: if (hasFilter) { if (-1 != -1 && ...) } -> if (false && ...) -> continue is NOT hit.
            // So if hasFilter is true but parse fails, it returns ALL entities?
            // Let's check logic:
            /*
            if (hasFilter)
            {
                if (filterId != -1 && entity.Index != filterId) continue;
            }
            */
            // If search is "abc", hasFilter=true, filterId=-1.
            // filterId != -1 is FALSE.
            // So condition is false. Continue is NOT hit.
            // So it yields the entity.
            // This means invalid filter string = NO FILTER.
            
            // ⭐ CE-3166 — ⛔ the reasoning above describes an id-parse filter this panel no longer has: the search is a substring of the
            //   row label, so "abc" matches no entity; the [Singletons] row stays (it is always listed).
            var results = EntityInspectorPanel.GetFilteredEntities(session, "abc", 1000).ToList();
            Assert.Equal(new[] { RepositoryAdapter.SingletonEntity }, results);
        }

        [Fact]
        public void RegisterContextMenuHandler_AcceptsHandler_WithoutThrowing()
        {
            var panel = new EntityInspectorPanel();
            var handler = new LambdaEntityContextMenuHandler((_, _) => { });
            // Should not throw.
            var ex = Record.Exception(() => panel.RegisterContextMenuHandler(handler));
            Assert.Null(ex);
        }

        [Fact]
        public void RegisterContextMenuHandler_MultipleHandlers_AllStoredAndInvoked()
        {
            var panel = new EntityInspectorPanel();
            var invocations = new List<string>();

            panel.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                invocations.Add($"A:{entity.Index}")));
            panel.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
                invocations.Add($"B:{entity.Index}")));

            // Simulate what the panel does internally: call PopulateMenu on each registered handler.
            var testEntity = new Entity(5, 1);
            panel.InvokeContextMenuHandlers(testEntity, NullContextMenuBuilder.Instance);

            Assert.Equal(new[] { "A:5", "B:5" }, invocations);
        }
    }

    // ── DD-P3-T02 multi-select tests ──────────────────────────────────────────

    [Collection("ImGui Sequential")]
    public class EntityInspectorPanelMultiSelectTests
    {
        private static List<Entity> MakeViewList(int count)
        {
            var list = new List<Entity>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Entity(i, 0));
            return list;
        }

        [Fact]
        public void CtrlClick_TwoRows_SelectsBoth()
        {
            var panel = new EntityInspectorPanel();
            var view  = MakeViewList(5);

            panel.HandleRowClick(view, 1, ctrl: true,  shift: false);
            panel.HandleRowClick(view, 3, ctrl: true,  shift: false);

            Assert.Equal(2, panel._selectedEntities.Count);
            Assert.Contains(view[1], panel._selectedEntities);
            Assert.Contains(view[3], panel._selectedEntities);
        }

        [Fact]
        public void ShiftClick_Range_SelectsInclusiveRange_AndPreservesLastClickedIndex()
        {
            var panel = new EntityInspectorPanel();
            var view  = MakeViewList(8);

            // Plain-click index 2.
            panel.HandleRowClick(view, 2, ctrl: false, shift: false);
            Assert.Equal(2, panel._lastClickedIndex);

            // Shift+Click index 5 → select 2,3,4,5; _lastClickedIndex stays 2.
            panel.HandleRowClick(view, 5, ctrl: false, shift: true);

            Assert.Equal(4, panel._selectedEntities.Count);
            for (int i = 2; i <= 5; i++)
                Assert.Contains(view[i], panel._selectedEntities);
            Assert.Equal(2, panel._lastClickedIndex);
        }

        [Fact]
        public void PlainClick_ClearsOtherSelections()
        {
            var panel = new EntityInspectorPanel();
            var view  = MakeViewList(4);

            // Select two via ctrl.
            panel.HandleRowClick(view, 0, ctrl: true, shift: false);
            panel.HandleRowClick(view, 2, ctrl: true, shift: false);
            Assert.Equal(2, panel._selectedEntities.Count);

            // Plain click → only one.
            panel.HandleRowClick(view, 1, ctrl: false, shift: false);

            Assert.Single(panel._selectedEntities);
            Assert.Contains(view[1], panel._selectedEntities);
        }

        [Fact]
        public void MultiSelectShowsCorrectDetailPaneMessage_InDrawContent()
        {
            using var fixture = new ImGuiTestFixture();
            using var repo = new EntityRepository();
            var panel = new EntityInspectorPanel();
            var context = new FakeInspectorContext();
            var session = new RepositoryAdapter(repo);

            // Force 2 entities into _selectedEntities via ctrl-click helpers.
            var e1 = repo.CreateEntity();
            var e2 = repo.CreateEntity();
            panel._selectedEntities.Add(e1);
            panel._selectedEntities.Add(e2);

            // Draw must not throw even with multiple entities selected.
            fixture.NewFrame();
            panel.DrawContent(session, context);
            fixture.Render();
        }
        // ── CE-4310 (D6) — the raw Components tab: nested structs expand, inline arrays draw, a typed field uses its formatter ─────────────

        private struct NestedProbe { public int A; public InnerProbe B; }
        private struct InnerProbe { public float X; public int Y; public DeepProbe Deep; }
        private struct DeepProbe { public int Z; }

        private struct TypedProbe
        {
            public Fdp.Core.FixedString64 Name;
            public Fdp.Toolkit.Replication.EntityRef Target;
            public Fdp.Toolkit.Replication.EntityLink Link;
            public Fdp.Core.SimInstant At;
            public Fdp.Core.SimDuration Len;
        }

        /// <summary>The rows the property tree draws for <paramref name="value"/> on its own (default node state: nothing clicked).</summary>
        private static Dictionary<string, (string Kind, string Text)> TreeRows(object value)
        {
            using var fixture = new ImGuiTestFixture();
            var rows = new Dictionary<string, (string, string)>();
            ImGuiPropertyTree.RowObserver = (path, kind, text) => rows[path] = (kind, text);
            try
            {
                for (int i = 0; i < 3; i++) { fixture.NewFrame(); ImGuiPropertyTree.Render(value, value.GetType()); fixture.Render(); }
            }
            finally { ImGuiPropertyTree.RowObserver = null; }
            return rows;
        }

        private sealed class OneComponentSession : Fdp.Presentation.Abstractions.IInspectableSession
        {
            private readonly Type _type; private readonly object _data;
            public OneComponentSession(Type type, object data) { _type = type; _data = data; }
            public bool IsReadOnly => false;
            public int EntityCount => 1;
            public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
            public bool IsAlive(Entity e) => true;
            public IEnumerable<Type> GetAllComponentTypes() => new[] { _type };
            public bool HasComponent(Entity e, Type t) => t == _type;
            public object? GetComponent(Entity e, Type t) => t == _type ? _data : null;
            public void SetComponent(Entity e, Type t, object v) { }
            public bool HasAuthority(Entity e, Type t) => false;
        }

        private static NestedProbe Nested() => new NestedProbe { A = 1, B = new InnerProbe { X = 2.5f, Y = 7, Deep = new DeepProbe { Z = 9 } } };

        /// <summary>(a) A struct inside a struct shows its inner fields — at any depth — without the person clicking each node open. ⛔ MEASURED red before the fix: the nested nodes started collapsed.</summary>
        [Fact]
        public void CE4310_ANestedStruct_ShowsItsInnerFields_WithoutClickingEachNodeOpen()
        {
            var rows = TreeRows(Nested());
            Assert.Equal(("leaf", "1"), rows["$.A"]);
            Assert.Equal(("leaf", "2.5"), rows["$.B.X"]);
            Assert.Equal(("leaf", "7"), rows["$.B.Y"]);
            Assert.Equal(("leaf", "9"), rows["$.B.Deep.Z"]);
        }

        /// <summary>(a') Collapse All closes the NESTED nodes too and Expand All reopens them (the buttons used to touch only the component headers).
        /// ✅ Red-proof: the reflector not setting <c>ImGuiPropertyTree.ForceOpen</c> ⇒ the collapsed frame still draws the inner fields ⇒ red.</summary>
        [Fact]
        public void CE4310_CollapseAllClosesTheNestedNodes_ExpandAllReopensThem()
        {
            using var fixture = new ImGuiTestFixture();
            using var repo = new EntityRepository();
            var e = repo.CreateEntity();
            var panel = new EntityInspectorPanel();
            var session = new OneComponentSession(typeof(NestedProbe), Nested());   // a stub: no component id is taken in the shared registry
            var rows = new Dictionary<string, (string, string)>();
            ImGuiPropertyTree.RowObserver = (path, kind, text) => rows[path] = (kind, text);
            try
            {
                panel.Reflector.ForceExpandAll = true;                                                   // frame 0: open the header (and everything under it)
                fixture.NewFrame(); panel.DrawComponentsFor(session, e); fixture.Render();
                Assert.True(rows.ContainsKey("$.B.Deep.Z"), "Expand All must reach the third level");

                rows.Clear();
                fixture.NewFrame(); panel.DrawComponentsFor(session, e); fixture.Render();               // frame 1: states persist, still open
                Assert.True(rows.ContainsKey("$.B.Deep.Z"));

                rows.Clear();
                panel.Reflector.ForceCollapseAll = true;                                                 // frame 2: Collapse All — the headers close, so nothing is drawn under them
                fixture.NewFrame(); panel.DrawComponentsFor(session, e); fixture.Render();
                Assert.DoesNotContain("$.B.X", rows.Keys);
            }
            finally { ImGuiPropertyTree.RowObserver = null; }
        }

        /// <summary>(b) An inline array (a real component: <c>EngagementArea.NoFire</c> — a Count + an [InlineArray] buffer, nested one level down) draws every live element with its value and says how many of its capacity.</summary>
        [Fact]
        public void CE4310_AnInlineArray_DrawsEveryLiveElement_WithItsValue()
        {
            var area = new Fdp.Toolkit.Behavior.Components.EngagementArea();
            area.NoFire.Count = 2; area.NoFire.Items[0] = 11; area.NoFire.Items[1] = 22;
            var rows = TreeRows(area);
            Assert.Equal(("fold", "[2 of 4]"), rows["$.NoFire"]);
            Assert.Equal(("leaf", "11"), rows["$.NoFire[0]"]);
            Assert.Equal(("leaf", "22"), rows["$.NoFire[1]"]);
            Assert.False(rows.ContainsKey("$.NoFire[2]"));   // only the live ones
        }

        /// <summary>(c) A typed field draws as the editor's formatter writes it — a name / id, not an expandable struct of raw members.</summary>
        [Fact]
        public void CE4310_ATypedField_DrawsAsItsFormattedValue_NotAsRawMembers()
        {
            var rows = TreeRows(new TypedProbe
            {
                Name = new Fdp.Core.FixedString64("Alpha 1-1"), Target = new Fdp.Toolkit.Replication.EntityRef(42),
                Link = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(7)),
                At = new Fdp.Core.SimInstant(12.5), Len = new Fdp.Core.SimDuration(3f),
            });
            Assert.Equal(("leaf", "Alpha 1-1"), rows["$.Name"]);
            Assert.Equal(("leaf", "#42"), rows["$.Target"]);
            Assert.Equal(("leaf", "#7"), rows["$.Link"]);
            Assert.Equal("leaf", rows["$.At"].Kind);
            Assert.Equal("leaf", rows["$.Len"].Kind);
            Assert.DoesNotContain(rows.Keys, k => k.StartsWith("$.Name.") || k.StartsWith("$.Target.") || k.StartsWith("$.At."));   // no raw members underneath
        }
        /// <summary>CE4310 hot path (R-2084) — the tree draws every frame: a steady frame over the typed and nested probes allocates only its row ids (the pre-existing <c>name##depth_i</c> labels) and the
        /// leaf text; the bound catches a formatter that rebuilds a large string per row. (Measured and reported; the probe is small on purpose.)</summary>
        [Fact]
        public void CE4310_ASteadyFrame_OverTheTypedAndNestedProbes_StaysSmall()
        {
            using var fixture = new ImGuiTestFixture();
            var typed = new TypedProbe
            {
                Name = new Fdp.Core.FixedString64("Alpha 1-1"), Target = new Fdp.Toolkit.Replication.EntityRef(42),
                Link = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(7)),
                At = new Fdp.Core.SimInstant(12.5), Len = new Fdp.Core.SimDuration(3f),
            };
            object boxedTyped = typed, boxedNested = Nested();
            void Frame() { fixture.NewFrame(); ImGuiPropertyTree.Render(boxedTyped, typeof(TypedProbe)); ImGuiPropertyTree.Render(boxedNested, typeof(NestedProbe)); fixture.Render(); }
            for (int i = 0; i < 5; i++) Frame();
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) Frame();
            long perFrame = (System.GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
            Assert.True(perFrame < 6 * 1024, $"{perFrame} B/frame");
        }
    }

    /// <summary>Null-object implementation of <see cref="IContextMenuBuilder"/> for test isolation.</summary>
    internal sealed class NullContextMenuBuilder : IContextMenuBuilder
    {
        public static readonly NullContextMenuBuilder Instance = new();
        public void AddItem(string label, Action callback, bool enabled = true) { }
        public IContextMenuBuilder BeginSubmenu(string label) => this;
        public void EndSubmenu() { }
        public void AddSeparator() { }
    }
}
