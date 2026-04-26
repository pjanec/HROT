using Fdp.Core;
using Fdp.Core.Collections;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Vis2D.Systems;

/// <summary>
/// Maintains a flattened list of entities sorted Bottom-Up (Children before Parents).
/// Uses dirty flag optimization - only re-sorts when hierarchy structure changes.
/// INCLUDES CYCLE DETECTION to prevent infinite loops.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
public class HierarchyOrderSystem : IEcsModuleSystem, IDisposable
{
    /// <summary>
    /// Singleton holding the sorted list.
    /// </summary>
    public struct SortedHierarchyData
    {
        public NativeArray<Entity> BottomUpList; // Zero-Alloc storage
        public int Count;
        public uint TopologyVersion; // Dirty flag
    }

    private bool _isDirty = true;
    private NativeArray<Entity> _buffer;
    private int _bufferCount;
    
    // Cycle detection
    private readonly HashSet<Entity> _visited = new();

    public HierarchyOrderSystem()
    {
        // Initial capacity (resize logic needed in real prod code)
        _buffer = new NativeArray<Entity>(10000, Allocator.Persistent);
    }

    public void MarkDirty() => _isDirty = true;

    public void Execute(ISimulationView view, float deltaTime)
    {
        var repo = (EntityRepository)view;
        if (!_isDirty) return; // OPTIMIZATION: Skip when nothing changed

        // 1. Perform Topological Sort (Bottom-Up)
        var roots = repo.Query().With<VisHierarchyNode>().Build();
        
        _bufferCount = 0;
        _visited.Clear();

        foreach (var entity in roots)
        {
            var node = view.GetComponentRO<VisHierarchyNode>(entity);
            if (node.Parent == Entity.Null) // Is Root
            {
                ProcessNode(entity, view);
            }
        }

        // Publish the result
        repo.SetSingleton(new SortedHierarchyData 
        { 
            BottomUpList = _buffer, 
            Count = _bufferCount,
            TopologyVersion = repo.GlobalVersion
        });

        _isDirty = false;
    }

    /// <summary>
    /// Post-order traversal with CYCLE DETECTION.
    /// </summary>
    private void ProcessNode(Entity entity, ISimulationView view)
    {
        // SAFETY CHECK: Detect cycles
        if (_visited.Contains(entity))
        {
            // Log error and abort to prevent infinite loop
            Console.Error.WriteLine($"[HierarchyOrderSystem] ERROR: Cycle detected in hierarchy at entity {entity.Index}");
            return;
        }
        
        _visited.Add(entity);

        // 1. Process Children First (Post-Order)
        var node = view.GetComponentRO<VisHierarchyNode>(entity);
        Entity child = node.FirstChild;
        
        while (view.IsAlive(child))
        {
            ProcessNode(child, view); // Recurse
            var childNode = view.GetComponentRO<VisHierarchyNode>(child);
            child = childNode.NextSibling;
        }

        // 2. Add Self
        if (_bufferCount < _buffer.Length)
        {
            _buffer[_bufferCount++] = entity;
        }
        
        // 3. Remove from visited (for correct sibling handling)
        _visited.Remove(entity);
    }

    public void Dispose()
    {
        _buffer.Dispose();
    }
}
