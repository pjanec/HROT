using Fdp.Core;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Vis2D.Abstractions;

/// <summary>
/// Adapter interface for hierarchy traversal.
/// Allows generic hierarchy rendering without coupling to specific component types.
/// </summary>
public interface IHierarchyAdapter
{
    /// <summary>
    /// Get the parent of an entity. Return Entity.Null if root.
    /// </summary>
    Entity GetParent(ISimulationView view, Entity entity);

    /// <summary>
    /// Get direct subordinates using zero-alloc iterator.
    /// </summary>
    ChildEnumerator GetChildren(ISimulationView view, Entity entity);
    
    /// <summary>
    /// Should this entity be treated as a leaf for position calculation?
    /// (e.g. A Tank is a leaf. A Platoon is not.)
    /// </summary>
    bool IsSpatialLeaf(ISimulationView view, Entity entity);

    /// <summary>
    /// ⭐ CE-4213 (DESIGN_Entity_Hierarchy_Views HV-4) — is <paramref name="entity"/> a member of THIS relation's tree at all?
    /// Default: it has an <see cref="EntityInfo"/> (a named entity). A relation that also shows nameless entities
    /// (the Structure view's parts) overrides it.
    /// </summary>
    bool Includes(ISimulationView view, Entity entity) => view.HasComponent<EntityInfo>(entity);

    /// <summary>⭐ CE-4213 — may <paramref name="child"/> be re-parented under <paramref name="newParent"/> in this relation? Default: no (read-only).</summary>
    bool CanReparent(ISimulationView view, Entity child, Entity newParent) => false;

    /// <summary>⭐ CE-4213 ⑥ — WHY <paramref name="child"/> may not be re-parented under <paramref name="newParent"/> (<c>Entity.Null</c> = removed from its
    /// parent), in words a person reads; null when nothing forbids it. The panel shows it where it shows a refused drop. Default: no reason.</summary>
    string? ReparentRefusal(ISimulationView view, Entity child, Entity newParent) => null;

    /// <summary>⭐ CE-4213 — re-parents (only when <see cref="CanReparent"/>); a read-only relation does nothing.</summary>
    void Reparent(ISimulationView view, Entity child, Entity newParent) { }
}

/// <summary>
/// Zero-allocation, Duck-Typed enumerator for hierarchy children.
/// Must contain: Current, MoveNext(), and GetEnumerator().
/// </summary>
public ref struct ChildEnumerator
{
    private readonly ISimulationView _view;
    private Entity _current;
    private Entity _next;
    private readonly ReadOnlySpan<Entity> _span;
    private int _spanIndex;
    private readonly bool _fromSpan;
    
    // Standard implementation using HierarchyNode linked list
    public ChildEnumerator(ISimulationView view, Entity firstChild)
    {
        _view = view;
        _current = Entity.Null;
        _next = firstChild;
        _span = default;
        _spanIndex = 0;
        _fromSpan = false;
    }

    /// <summary>
    /// ⭐ CE-4213 (HV-4) — enumerates an explicit child list (any relation that is not the <c>VisHierarchyNode</c> linked list:
    /// the Command and Structure views). The <see cref="VisHierarchyNode"/> constructor above is unchanged.
    /// </summary>
    public ChildEnumerator(ISimulationView view, ReadOnlySpan<Entity> children)
    {
        _view = view;
        _current = Entity.Null;
        _next = Entity.Null;
        _span = children;
        _spanIndex = 0;
        _fromSpan = true;
    }

    public Entity Current => _current;

    public bool MoveNext()
    {
        if (_fromSpan)
        {
            while (_spanIndex < _span.Length)
            {
                var e = _span[_spanIndex++];
                if (!_view.IsAlive(e)) continue;
                _current = e;
                return true;
            }
            return false;
        }

        if (!_view.IsAlive(_next)) return false;

        _current = _next;
        
        // Advance to next sibling
        if (_view.HasComponent<VisHierarchyNode>(_current))
        {
            _next = _view.GetComponentRO<VisHierarchyNode>(_current).NextSibling;
        }
        else
        {
            _next = Entity.Null;
        }
        return true;
    }

    public ChildEnumerator GetEnumerator() => this;
}
