using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Fdp.Kernel
{
    /// <summary>
    /// A system that manages and executes a collection of other systems.
    /// Automatically sorts systems based on their dependency attributes.
    /// </summary>
    public class SystemGroup : ComponentSystem
    {
        private readonly List<ComponentSystem> _systems = new List<ComponentSystem>();
        private bool _needsSort = true;
        
        /// <summary>
        /// Number of systems in this group.
        /// </summary>
        public int SystemCount => _systems.Count;
        
        /// <summary>
        /// Executes all enabled systems in this group.
        /// Systems are automatically sorted based on their dependencies.
        /// </summary>
        protected override void OnUpdate()
        {
            if (_needsSort)
            {
                SortSystems();
            }
            
            foreach (var system in _systems)
            {
                try
                {
                    system.InternalUpdate();
                }
                catch (Exception ex)
                {
                    // Log error but continue executing other systems
                    Console.Error.WriteLine($"Error in system {system.GetType().Name}: {ex}");
                }
            }
        }
        
        /// <summary>
        /// Adds a system to this group.
        /// The system will be automatically initialized and sorted.
        /// </summary>
        /// <param name="system">The system to add</param>
        public void AddSystem(ComponentSystem system)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }
            
            if (World == null)
            {
                throw new InvalidOperationException("SystemGroup must be created before adding systems");
            }
            
            system.InternalCreate(World);
            _systems.Add(system);
            _needsSort = true;
        }
        
        /// <summary>
        /// Sorts systems based on their UpdateBefore/UpdateAfter attributes.
        /// Uses a topological sort to respect dependencies.
        /// </summary>
        public void SortSystems()
        {
            if (_systems.Count <= 1)
            {
                _needsSort = false;
                return;
            }
            
            // Build dependency graph
            var graph = new Dictionary<ComponentSystem, HashSet<ComponentSystem>>();
            var inDegree = new Dictionary<ComponentSystem, int>();
            
            // Initialize
            foreach (var system in _systems)
            {
                graph[system] = new HashSet<ComponentSystem>();
                inDegree[system] = 0;
            }
            
            // Build edges based on attributes
            foreach (var system in _systems)
            {
                Type systemType = system.GetType();
                
                // Process UpdateBefore attributes
                var beforeAttrs = systemType.GetCustomAttributes<UpdateBeforeAttribute>(true);
                foreach (var attr in beforeAttrs)
                {
                    if (!typeof(ComponentSystem).IsAssignableFrom(attr.Target))
                    {
                        throw new ArgumentException($"Invalid UpdateBefore target: {attr.Target.Name} must inherit from ComponentSystem");
                    }
                    
                    // Find target system in our list
                    var targetSystem = _systems.FirstOrDefault(s => s.GetType() == attr.Target);
                    if (targetSystem != null)
                    {
                        // system -> targetSystem (system must come before target)
                        if (graph[system].Add(targetSystem))
                        {
                            inDegree[targetSystem]++;
                        }
                    }
                }
                
                // Process UpdateAfter attributes
                var afterAttrs = systemType.GetCustomAttributes<UpdateAfterAttribute>(true);
                foreach (var attr in afterAttrs)
                {
                    if (!typeof(ComponentSystem).IsAssignableFrom(attr.Target))
                    {
                        throw new ArgumentException($"Invalid UpdateAfter target: {attr.Target.Name} must inherit from ComponentSystem");
                    }
                    
                    // Find target system in our list
                    var targetSystem = _systems.FirstOrDefault(s => s.GetType() == attr.Target);
                    if (targetSystem != null)
                    {
                        // targetSystem -> system (target must come before system)
                        if (graph[targetSystem].Add(system))
                        {
                            inDegree[system]++;
                        }
                    }
                }
            }
            
            // Topological sort using Kahn's algorithm
            var sorted = new List<ComponentSystem>();
            var queue = new Queue<ComponentSystem>();
            
            // Start with nodes that have no dependencies
            foreach (var system in _systems)
            {
                if (inDegree[system] == 0)
                {
                    queue.Enqueue(system);
                }
            }
            
            // Process queue
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                sorted.Add(current);
                
                // Reduce in-degree for all neighbors
                foreach (var neighbor in graph[current])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
            
            // Check for cycles
            if (sorted.Count != _systems.Count)
            {
                // Find systems involved in cycle
                var cycleMembers = _systems.Except(sorted).Select(s => s.GetType().Name);
                throw new InvalidOperationException(
                    $"Circular dependency detected in system ordering: {string.Join(", ", cycleMembers)}");
            }
            
            // Update the systems list with sorted order
            _systems.Clear();
            _systems.AddRange(sorted);
            _needsSort = false;
        }
        
        /// <summary>
        /// Gets all systems in this group (in current sorted order).
        /// </summary>
        public IReadOnlyList<ComponentSystem> GetSystems()
        {
            if (_needsSort)
            {
                SortSystems();
            }
            return _systems.AsReadOnly();
        }
        
        /// <summary>
        /// Cleanup all systems in this group.
        /// </summary>
        protected override void OnDestroy()
        {
            foreach (var system in _systems)
            {
                system.Dispose();
            }
            _systems.Clear();
        }
    }
}
