using Xunit;
using Fdp.Kernel;
using System.Collections.Generic;

namespace Fdp.Tests
{
    [Collection("ComponentTests")]
    public class HierarchyTests
    {
        public HierarchyTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void AddChild_SetsParentAndChildren()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var parent = repo.CreateEntity();
            var child = repo.CreateEntity();
            
            repo.AddChild(parent, child);
            
            var parentNode = repo.GetComponentRW<HierarchyNode>(parent);
            var childNode = repo.GetComponentRW<HierarchyNode>(child);
            
            Assert.Equal(child, parentNode.FirstChild);
            Assert.Equal(parent, childNode.Parent);
            Assert.Equal(Entity.Null, childNode.NextSibling);
            Assert.Equal(Entity.Null, childNode.PreviousSibling);
        }
        
        [Fact]
        public void AddChild_MultipleChildren_LinkedCorrectly()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var parent = repo.CreateEntity();
            var child1 = repo.CreateEntity();
            var child2 = repo.CreateEntity();
            var child3 = repo.CreateEntity();
            
            repo.AddChild(parent, child1);
            repo.AddChild(parent, child2);
            repo.AddChild(parent, child3);
            
            // Order is LIFO (prepend) in current implementation for O(1)
            // Parent -> Child3 -> Child2 -> Child1
            
            var parentNode = repo.GetComponentRW<HierarchyNode>(parent);
            Assert.Equal(child3, parentNode.FirstChild);
            
            var c3Node = repo.GetComponentRW<HierarchyNode>(child3);
            Assert.Equal(child2, c3Node.NextSibling);
            Assert.Equal(Entity.Null, c3Node.PreviousSibling);
            Assert.Equal(parent, c3Node.Parent);
            
            var c2Node = repo.GetComponentRW<HierarchyNode>(child2);
            Assert.Equal(child1, c2Node.NextSibling);
            Assert.Equal(child3, c2Node.PreviousSibling);
            Assert.Equal(parent, c2Node.Parent);
            
            var c1Node = repo.GetComponentRW<HierarchyNode>(child1);
            Assert.Equal(Entity.Null, c1Node.NextSibling);
            Assert.Equal(child2, c1Node.PreviousSibling);
            Assert.Equal(parent, c1Node.Parent);
        }
        
        [Fact]
        public void RemoveFromParent_UnlinksCorrectly()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var parent = repo.CreateEntity();
            var child1 = repo.CreateEntity();
            var child2 = repo.CreateEntity();
            
            repo.AddChild(parent, child1);
            repo.AddChild(parent, child2);
            
            // Current user state: Parent -> Child2 -> Child1
            repo.RemoveFromParent(child2);
            
            // Should be: Parent -> Child1
            var parentNode = repo.GetComponentRW<HierarchyNode>(parent);
            Assert.Equal(child1, parentNode.FirstChild);
            
            var c1Node = repo.GetComponentRW<HierarchyNode>(child1);
            Assert.Equal(Entity.Null, c1Node.PreviousSibling);
            Assert.Equal(Entity.Null, c1Node.NextSibling);
            
            var c2Node = repo.GetComponentRW<HierarchyNode>(child2);
            Assert.Equal(Entity.Null, c2Node.Parent);
        }
        
        [Fact]
        public void Reparent_MovesEntity()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var parent1 = repo.CreateEntity();
            var parent2 = repo.CreateEntity();
            var child = repo.CreateEntity();
            
            repo.AddChild(parent1, child);
            repo.AddChild(parent2, child);
            
            var p1Node = repo.GetComponentRW<HierarchyNode>(parent1);
            var p2Node = repo.GetComponentRW<HierarchyNode>(parent2);
            var cNode = repo.GetComponentRW<HierarchyNode>(child);
            
            Assert.Equal(Entity.Null, p1Node.FirstChild);
            Assert.Equal(child, p2Node.FirstChild);
            Assert.Equal(parent2, cNode.Parent);
        }
        
        [Fact]
        public void GetChildren_IteratesAll()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var parent = repo.CreateEntity();
            var children = new List<Entity>();
            
            for (int i = 0; i < 5; i++)
            {
                var c = repo.CreateEntity();
                children.Add(c);
                repo.AddChild(parent, c);
            }
            
            // Collect via enumerator
            var collected = new List<Entity>();
            foreach (var child in repo.GetChildren(parent))
            {
                collected.Add(child);
            }
            
            Assert.Equal(5, collected.Count);
            // Verify all present (order is reverse creation)
            foreach (var c in children)
            {
                Assert.Contains(c, collected);
            }
        }
        
        [Fact]
        public void AutoComponentAddition_Works()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var parent = repo.CreateEntity();
            var child = repo.CreateEntity();
            
            // Components added automatically
            repo.AddChild(parent, child);
            
            Assert.True(repo.HasUnmanagedComponent<HierarchyNode>(parent));
            Assert.True(repo.HasUnmanagedComponent<HierarchyNode>(child));
        }
        
        [Fact]
        public void RemoveFromParent_RootEntity_DoesNothing()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<HierarchyNode>();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new HierarchyNode 
            { 
                Parent = Entity.Null,
                FirstChild = Entity.Null,
                PreviousSibling = Entity.Null,
                NextSibling = Entity.Null
            });
            
            repo.RemoveFromParent(entity); // Should not crash
            
            var node = repo.GetComponentRW<HierarchyNode>(entity);
            Assert.Equal(Entity.Null, node.Parent);
        }
    }
}
