using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Vis2D.Abstractions;

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
    
    // Standard implementation using HierarchyNode linked list
    public ChildEnumerator(ISimulationView view, Entity firstChild)
    {
        _view = view;
        _current = Entity.Null;
        _next = firstChild;
    }

    public Entity Current => _current;

    public bool MoveNext()
    {
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
