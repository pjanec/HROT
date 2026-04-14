using System;

namespace Fdp.Core
{
    /// <summary>
    /// Component representing a node in the entity hierarchy.
    /// Implements a doubly-linked list for efficient parent/child traversal.
    /// </summary>
    [ComponentId(GlobalComponentIds.HierarchyNode)]
    public struct HierarchyNode
    {
        /// <summary>
        /// The parent entity (Entity.Null if root).
        /// </summary>
        public Entity Parent;
        
        /// <summary>
        /// The first child entity (Entity.Null if no children).
        /// </summary>
        public Entity FirstChild;
        
        /// <summary>
        /// The previous sibling entity (Entity.Null if first direct child).
        /// </summary>
        public Entity PreviousSibling;
        
        /// <summary>
        /// The next sibling entity (Entity.Null if last direct child).
        /// </summary>
        public Entity NextSibling;
        
        /// <summary>
        /// Checks if this entity has a parent.
        /// </summary>
        public readonly bool HasParent => Parent != Entity.Null;
        
        /// <summary>
        /// Checks if this entity has any children.
        /// </summary>
        public readonly bool HasChildren => FirstChild != Entity.Null;
    }
}
