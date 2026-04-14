using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Directed graph for system dependencies.
    /// Used for topological sorting to determine execution order.
    /// </summary>
    internal class DependencyGraph
    {
        private readonly HashSet<IEcsModuleSystem> _nodes = new();
        private readonly Dictionary<IEcsModuleSystem, HashSet<IEcsModuleSystem>> _edges = new();
        
        public IReadOnlyCollection<IEcsModuleSystem> Nodes => _nodes;
        
        public void AddNode(IEcsModuleSystem system)
        {
            _nodes.Add(system);
            if (!_edges.ContainsKey(system))
                _edges[system] = new HashSet<IEcsModuleSystem>();
        }
        
        /// <summary>
        /// Add edge: from -> to (from must execute before to).
        /// </summary>
        public void AddEdge(IEcsModuleSystem from, IEcsModuleSystem to)
        {
            if (!_nodes.Contains(from))
                throw new ArgumentException($"System {from.GetType().Name} not in graph");
            if (!_nodes.Contains(to))
                throw new ArgumentException($"System {to.GetType().Name} not in graph");
            
            _edges[from].Add(to);
        }
        
        /// <summary>
        /// Get all systems that depend on this system (outgoing edges).
        /// </summary>
        public IEnumerable<IEcsModuleSystem> GetOutgoingEdges(IEcsModuleSystem system)
        {
            return _edges.TryGetValue(system, out var deps) ? deps : Enumerable.Empty<IEcsModuleSystem>();
        }
        
        /// <summary>
        /// Get all systems this system depends on (incoming edges).
        /// </summary>
        public IEnumerable<IEcsModuleSystem> GetIncomingEdges(IEcsModuleSystem system)
        {
            return _edges.Where(kvp => kvp.Value.Contains(system))
                         .Select(kvp => kvp.Key);
        }
        
        /// <summary>
        /// Get count of incoming edges (dependencies).
        /// </summary>
        public int GetInDegree(IEcsModuleSystem system)
        {
            return GetIncomingEdges(system).Count();
        }
    }
}
