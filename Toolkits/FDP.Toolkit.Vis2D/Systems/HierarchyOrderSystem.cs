using Fdp.Kernel;
using Fdp.Kernel.Collections;
using FDP.Toolkit.Vis2D.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Vis2D.Systems;

/// <summary>
/// Maintains a flattened list of entities sorted Bottom-Up (Children before Parents).
/// Uses dirty flag optimization - only re-sorts when hierarchy structure changes.
/// INCLUDES CYCLE DETECTION to prevent infinite loops.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
public class HierarchyOrderSystem : ComponentSystem
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

    public void MarkDirty() => _isDirty = true;

    protected override void OnCreate()
    {
        // Initial capacity (resize logic needed in real prod code)
        _buffer = new NativeArray<Entity>(10000, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        if (!_isDirty) return; // OPTIMIZATION: Skip when nothing changed

        // 1. Perform Topological Sort (Bottom-Up)
        var roots = World.Query().With<VisHierarchyNode>().Build();
        
        _bufferCount = 0;
        _visited.Clear();

        foreach (var entity in roots)
        {
            var node = World.GetComponentRO<VisHierarchyNode>(entity);
            if (node.Parent == Entity.Null) // Is Root
            {
                ProcessNode(entity);
            }
        }

        // Publish the result
        World.SetSingleton(new SortedHierarchyData 
        { 
            BottomUpList = _buffer, 
            Count = _bufferCount,
            TopologyVersion = World.GlobalVersion
        });

        _isDirty = false;
    }

    /// <summary>
    /// Post-order traversal with CYCLE DETECTION.
    /// </summary>
    private void ProcessNode(Entity entity)
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
        var node = World.GetComponentRO<VisHierarchyNode>(entity);
        Entity child = node.FirstChild;
        
        while (World.IsAlive(child))
        {
            ProcessNode(child); // Recurse
            var childNode = World.GetComponentRO<VisHierarchyNode>(child);
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

    protected override void OnDestroy()
    {
        _buffer.Dispose();
    }
}
