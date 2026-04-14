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
    }

    [Collection("ImGui Sequential")]
    public class EntityInspectorPanelTests
    {
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
            
            // Search for ID of e2
            var results = EntityInspectorPanel.GetFilteredEntities(session, e2.Index.ToString(), 1000).ToList();
            
            Assert.Single(results);
            Assert.Equal(e2, results[0]);
        }
        
        [Fact]
        public void GetFilteredEntities_RespectsLimit()
        {
            using var repo = new EntityRepository();
            for(int i=0; i<10; i++) repo.CreateEntity();
            var session = new RepositoryAdapter(repo);
            
            var results = EntityInspectorPanel.GetFilteredEntities(session, "", 5).ToList();
            
            Assert.Equal(5, results.Count);
        }
        
        [Fact]
        public void GetFilteredEntities_InvalidSearch_ReturnsAllWithLimit()
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
            
            var results = EntityInspectorPanel.GetFilteredEntities(session, "abc", 1000).ToList();
            Assert.Equal(2, results.Count);
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
