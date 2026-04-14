using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// Extensions for managing entity hierarchy relationships safely.
    /// Handles the complexity of updating doubly-linked lists.
    /// </summary>
    public static class HierarchyExtensions
    {
        /// <summary>
        /// Adds a child entity to a parent.
        /// Removes the child from its previous parent if any.
        /// </summary>
        public static void AddChild(this EntityRepository repo, Entity parent, Entity child)
        {
            #if FDP_PARANOID_MODE
            if (!repo.IsAlive(parent)) throw new ArgumentException("Parent entity is dead");
            if (!repo.IsAlive(child)) throw new ArgumentException("Child entity is dead");
            if (parent == child) throw new ArgumentException("Cannot parent entity to itself");
            #endif
            
            // Ensure components exist
            if (!repo.HasUnmanagedComponent<HierarchyNode>(parent))
                repo.AddComponent(parent, new HierarchyNode 
                { 
                    Parent = Entity.Null,
                    FirstChild = Entity.Null,
                    PreviousSibling = Entity.Null,
                    NextSibling = Entity.Null
                });
                
            if (!repo.HasUnmanagedComponent<HierarchyNode>(child))
                repo.AddComponent(child, new HierarchyNode 
                { 
                    Parent = Entity.Null,
                    FirstChild = Entity.Null,
                    PreviousSibling = Entity.Null,
                    NextSibling = Entity.Null
                });
            
            // Unlink from current parent deeply
            repo.RemoveFromParent(child);
            
            // Get references to modify
            ref var parentNode = ref repo.GetComponentRW<HierarchyNode>(parent);
            
            // Link as first child (prepend)
            // Strategy: NewChild.Next = OldFirstChild; OldFirstChild.Prev = NewChild; Parent.First = NewChild;
            
            Entity oldFirstChild = parentNode.FirstChild;
            
            ref var childNode = ref repo.GetComponentRW<HierarchyNode>(child);
            childNode.Parent = parent;
            childNode.NextSibling = oldFirstChild;
            childNode.PreviousSibling = Entity.Null;
            
            // Update old first child if it exists
            if (oldFirstChild != Entity.Null)
            {
                ref var oldFirstNode = ref repo.GetComponentRW<HierarchyNode>(oldFirstChild);
                oldFirstNode.PreviousSibling = child;
            }
            
            // Update parent
            // Re-fetch parent ref just in case of stale ref (though unlikely in single thread)
            ref var parentRef = ref repo.GetComponentRW<HierarchyNode>(parent);
            parentRef.FirstChild = child;
        }
        
        /// <summary>
        /// Removes an entity from its parent.
        /// </summary>
        public static void RemoveFromParent(this EntityRepository repo, Entity child)
        {
            if (!repo.HasUnmanagedComponent<HierarchyNode>(child))
                return;
                
            ref var childNode = ref repo.GetComponentRW<HierarchyNode>(child);
            Entity parent = childNode.Parent;
            
            if (parent == Entity.Null)
                return; // Already root
            
            // Unlink logic
            Entity prev = childNode.PreviousSibling;
            Entity next = childNode.NextSibling;
            
            // Update prev sibling
            if (prev != Entity.Null)
            {
                ref var prevNode = ref repo.GetComponentRW<HierarchyNode>(prev);
                prevNode.NextSibling = next;
            }
            else
            {
                // Is first child, update parent
                ref var parentNode = ref repo.GetComponentRW<HierarchyNode>(parent);
                parentNode.FirstChild = next;
            }
            
            // Update next sibling
            if (next != Entity.Null)
            {
                ref var nextNode = ref repo.GetComponentRW<HierarchyNode>(next);
                nextNode.PreviousSibling = prev;
            }
            
            // Clear child pointers
            childNode.Parent = Entity.Null;
            childNode.NextSibling = Entity.Null;
            childNode.PreviousSibling = Entity.Null;
        }
        
        /// <summary>
        /// Gets the children of an entity as an enumerable.
        /// </summary>
        public static ChildEnumerator GetChildren(this EntityRepository repo, Entity parent)
        {
            if (!repo.HasUnmanagedComponent<HierarchyNode>(parent))
                return new ChildEnumerator(repo, Entity.Null);

            var node = repo.GetComponentRW<HierarchyNode>(parent);
            return new ChildEnumerator(repo, node.FirstChild);
        }
        
        // Custom efficient enumerator structure
        public ref struct ChildEnumerator
        {
            private readonly EntityRepository _repo;
            private Entity _current;
            private Entity _next; // Cache next to strictly allow modification of current during iteration? 
            // Iterating while modifying needs care. Standard for each usually disallows mod.
            
            public ChildEnumerator(EntityRepository repo, Entity firstChild)
            {
                _repo = repo;
                _current = Entity.Null;
                _next = firstChild;
            }
            
            public Entity Current => _current;
            
            public bool MoveNext()
            {
                if (_next == Entity.Null)
                    return false;
                
                _current = _next;
                
                // Advance
                if (_repo.HasUnmanagedComponent<HierarchyNode>(_current))
                {
                    _next = _repo.GetComponentRW<HierarchyNode>(_current).NextSibling;
                }
                else
                {
                    _next = Entity.Null;
                }
                
                return true;
            }
            
            public ChildEnumerator GetEnumerator() => this;
        }
    }
}
