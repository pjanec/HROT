using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Schedules system execution using topological sorting of dependencies.
    /// Systems execute in deterministic order based on Fdp.Core [UpdateAfter]/[UpdateBefore] attributes.
    /// </summary>
    public class SystemScheduler : ISystemRegistry
    {
        private readonly Dictionary<SystemPhase, List<IEcsModuleSystem>> _systemsByPhase = new();
        private readonly Dictionary<SystemPhase, List<IEcsModuleSystem>> _sortedSystems = new();
        private readonly Dictionary<IEcsModuleSystem, SystemPhase> _systemPhases = new();
        
        // Profiling data
        private readonly Dictionary<IEcsModuleSystem, SystemProfileData> _profileData = new();

        // CE-165 — every [SingleInstance] type seen so far, including those found inside registered groups.
        private readonly HashSet<Type> _singleInstanceTypes = new();
        
        /// <summary>
        /// Register a system for execution.
        /// System's phase is determined by [UpdateInPhase] attribute.
        /// </summary>
        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            var phase = GetPhaseAttribute(system);

            if (!_systemsByPhase.ContainsKey(phase))
                _systemsByPhase[phase] = new List<IEcsModuleSystem>();

            EnforceSingleInstance(system);

            _systemsByPhase[phase].Add(system);
            _systemPhases[system] = phase;
            _profileData[system] = new SystemProfileData(GetSystemProfileName(system));
        }
        
        /// <summary>
        /// <c>CE-165</c> — refuses a second registration of a type marked
        /// <see cref="SingleInstanceAttribute"/>.
        /// </summary>
        /// <remarks>
        /// Checked across ALL phases, not just the one being registered into: a system that ends up in two
        /// different phases still ticks twice per frame, which is the thing the attribute forbids. The
        /// scheduler is the right place because it is the single choke point every registration path reaches
        /// — module registration, manual registration and the packs all land here — so no composition root
        /// can opt out by wiring systems its own way.
        /// </remarks>
        private void EnforceSingleInstance<T>(T system) where T : IEcsModuleSystem
        {
            CollectSingleInstance(system!, system!.GetType().Name);
        }

        /// <summary>
        /// Walks a registered system, descending into <see cref="ISystemGroup"/> members, and records every
        /// <see cref="SingleInstanceAttribute"/> type it finds — throwing if one was already seen.
        /// </summary>
        /// <remarks>
        /// <b>The descent is the whole point, and it was measured.</b> A first version checked only the
        /// system handed to <c>RegisterSystem</c> and could not see the defect it was written for: the
        /// editor wraps its fused Brain+MuscleGround lists in a <c>TogglableSimulationGroup</c> and registers
        /// that ONE group, so the duplicated <c>UnitHierarchySystem</c> instances inside it never reach this
        /// method individually. Reverting the editor's deduplication and booting it proved the guard silent.
        /// A guard that cannot see the composition it exists to police is worse than none, because it reads
        /// as coverage.
        /// </remarks>
        private void CollectSingleInstance(IEcsModuleSystem system, string registrationRoot)
        {
            if (system == null) return;

            var type = system.GetType();
            if (Attribute.IsDefined(type, typeof(SingleInstanceAttribute)))
            {
                if (!_singleInstanceTypes.Add(type))
                {
                    throw new InvalidOperationException(
                        $"[SingleInstance] system '{type.FullName}' is registered more than once (reached "
                      + $"via '{registrationRoot}'). A second instance ticks every frame alongside the "
                      + "first, and this type is marked single-instance because that corrupts state rather "
                      + "than merely wasting time. Fix the COMPOSITION ROOT that registered it twice — most "
                      + "often a host concatenating two role packs that both carry it, without "
                      + "deduplicating by type. SystemComposition.DistinctByType is the shared helper "
                      + "(CE-165).");
                }
            }

            // Groups are registered as a single system but execute their members every frame, so a
            // duplicate hiding inside one is every bit as harmful as a duplicate registration.
            if (system is ISystemGroup group)
            {
                foreach (var member in group.GetSystems())
                    CollectSingleInstance(member, registrationRoot);
            }
        }

        /// <summary>
        /// Build execution orders for all phases.
        /// Must be called after all systems registered, before execution.
        /// </summary>
        public void BuildExecutionOrders()
        {
            foreach (var (phase, systems) in _systemsByPhase)
            {
                var graph = BuildDependencyGraph(systems);
                var sorted = TopologicalSort(graph);
                
                if (sorted == null)
                {
                    if (systems == null) throw new InvalidOperationException("Systems list is null");
                    
                    var systemNames = systems.Select(s => s?.GetType().Name ?? "null");
                    var message = $"Circular dependency detected in phase {phase}. Systems: {string.Join(", ", systemNames)}";
                    
                    // Console.WriteLine(message); // For debugging
                    throw new CircularDependencyException(message);
                }
                
                _sortedSystems[phase] = sorted;
            }
        }
        
        /// <summary>
        /// Execute all systems in a phase.
        /// </summary>
        public void ExecutePhase(SystemPhase phase, ISimulationView view, float deltaTime)
        {
            if (phase == SystemPhase.Manual) return;

            if (!_sortedSystems.TryGetValue(phase, out var systems))
                return;
            
            foreach (var system in systems)
            {
                ExecuteSystem(system, view, deltaTime);
            }
        }
        
        internal void ExecuteSystem(IEcsModuleSystem system, ISimulationView view, float deltaTime)
        {
            var profile = _profileData[system];
            var sw = Stopwatch.StartNew();
            
            //try
            {
                // Check if system is a group
                if (system is ISystemGroup group)
                {
                    ExecuteGroup(group, view, deltaTime);
                }
                else
                {
                    system.Execute(view, deltaTime);
                }
                
                sw.Stop();
                profile.RecordExecution(sw.Elapsed.TotalMilliseconds);
            }
            //catch (Exception ex)
            //{
            //    sw.Stop();
            //    profile.RecordError(ex);
            //    throw new SystemExecutionException(
            //        $"System {system.GetType().Name} failed", ex);
            //}
        }
        
        private void ExecuteGroup(ISystemGroup group, ISimulationView view, float deltaTime)
        {
            var groupProfile = _profileData[group];
            var groupSw = Stopwatch.StartNew();

            if (!group.Enabled)
            {
                groupSw.Stop();
                groupProfile.RecordExecution(groupSw.Elapsed.TotalMilliseconds);
                return;
            }

            foreach (var system in group.GetSystems())
            {
                // Ensure nested systems are profiled
                if (!_profileData.ContainsKey(system))
                    _profileData[system] = new SystemProfileData(GetSystemProfileName(system));
                if (!_systemPhases.ContainsKey(system) && _systemPhases.TryGetValue(group, out var groupPhase))
                    _systemPhases[system] = groupPhase;
                
                ExecuteSystem(system, view, deltaTime);
            }
            
            groupSw.Stop();
            groupProfile.RecordExecution(groupSw.Elapsed.TotalMilliseconds);
        }
        
        private SystemPhase GetPhaseAttribute(IEcsModuleSystem system)
        {
            var attr = (UpdateInPhaseAttribute?)Attribute.GetCustomAttribute(
                system.GetType(), typeof(UpdateInPhaseAttribute), inherit: true);
            
            if (attr == null)
            {
                throw new InvalidOperationException(
                    $"System {system.GetType().Name} must have [UpdateInPhase] attribute");
            }
            
            return attr.Phase;
        }

        private static string GetSystemProfileName(IEcsModuleSystem system)
        {
            if (system is IProfiledSystem profiled)
                return profiled.ProfileName;

            return system.GetType().Name;
        }
        
        private DependencyGraph BuildDependencyGraph(List<IEcsModuleSystem> systems)
        {
            var graph = new DependencyGraph();
            
            // CRITICAL: Create lookup for systems in THIS phase only
            var systemTypesInPhase = new HashSet<Type>(systems.Select(s => s.GetType()));
            
            // First pass: Add all nodes
            foreach (var system in systems)
            {
                graph.AddNode(system);
            }

            // Second pass: Add edges
            foreach (var system in systems)
            {
                // Extract [UpdateAfter] attributes (Using Fdp.Core Attribute)
                var afterAttrs = Attribute.GetCustomAttributes(
                    system.GetType(), typeof(Fdp.Core.UpdateAfterAttribute), inherit: true)
                    .Cast<Fdp.Core.UpdateAfterAttribute>();
                
                foreach (var attr in afterAttrs)
                {
                    // DEBUG: Console.WriteLine($"System {system.GetType().Name} has UpdateAfter({attr.Target.Name})");
                    
                    // CRITICAL FIX: Only add edge if dependency is in CURRENT phase
                    if (systemTypesInPhase.Contains(attr.Target))
                    {
                        var dependency = systems.First(s => s.GetType() == attr.Target);
                        graph.AddEdge(dependency, system); // dependency -> system
                        // DEBUG: Console.WriteLine($"Added edge: {dependency.GetType().Name} -> {system.GetType().Name}");
                    }
                }
                
                // Extract [UpdateBefore] attributes (Using Fdp.Core Attribute)
                var beforeAttrs = Attribute.GetCustomAttributes(
                    system.GetType(), typeof(Fdp.Core.UpdateBeforeAttribute), inherit: true)
                    .Cast<Fdp.Core.UpdateBeforeAttribute>();
                
                foreach (var attr in beforeAttrs)
                {
                    // DEBUG: Console.WriteLine($"System {system.GetType().Name} has UpdateBefore({attr.Target.Name})");

                    if (systemTypesInPhase.Contains(attr.Target))
                    {
                        var dependent = systems.First(s => s.GetType() == attr.Target);
                        graph.AddEdge(system, dependent); // system -> dependent
                        // DEBUG: Console.WriteLine($"Added edge: {system.GetType().Name} -> {dependent.GetType().Name}");
                    }
                }
            }
            
            return graph;
        }
        
        private List<IEcsModuleSystem>? TopologicalSort(DependencyGraph graph)
        {
            // Kahn's algorithm
            var sorted = new List<IEcsModuleSystem>();
            var inDegree = new Dictionary<IEcsModuleSystem, int>();
            var queue = new Queue<IEcsModuleSystem>();
            
            // Calculate in-degrees
            foreach (var node in graph.Nodes)
            {
                int degree = graph.GetIncomingEdges(node).Count();
                inDegree[node] = degree;
                
                if (degree == 0)
                    queue.Enqueue(node);
            }
            
            // Process nodes
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                sorted.Add(node);
                
                foreach (var neighbor in graph.GetOutgoingEdges(node))
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }
            
            // Cycle detection
            if (sorted.Count != graph.Nodes.Count)
                return null; // Cycle detected
            
            return sorted;
        }
        
        /// <summary>
        /// Get all registered systems in execution order.
        /// If execution orders have not been built yet, returns currently registered systems by phase.
        /// </summary>
        public IEnumerable<IEcsModuleSystem> GetAllSystems()
        {
            var source = _sortedSystems.Count > 0 ? _sortedSystems : _systemsByPhase;

            foreach (var phaseList in source.Values)
            {
                foreach (var system in phaseList)
                {
                    yield return system;
                }
            }
        }

        /// <summary>
        /// Get profiling data for a specific system.
        /// </summary>
        public SystemProfileData? GetProfileData(IEcsModuleSystem system)
        {
            return _profileData.TryGetValue(system, out var data) ? data : null;
        }
        
        /// <summary>
        /// Get profiling data for a specific system by type.
        /// </summary>
        public SystemProfileData? GetProfileData<T>() where T : IEcsModuleSystem
        {
            var system = _systemsByPhase.Values
                .SelectMany(list => list)
                .FirstOrDefault(s => s is T);
            
            return system != null ? GetProfileData(system) : null;
        }
        
        /// <summary>
        /// Get all profiling data grouped by phase, alongside the original system instance.
        /// </summary>
        public Dictionary<SystemPhase, List<(IEcsModuleSystem System, SystemProfileData Profile)>> GetAllProfileData()
        {
            var result = new Dictionary<SystemPhase, List<(IEcsModuleSystem System, SystemProfileData Profile)>>();

            foreach (var (system, profile) in _profileData)
            {
                if (!_systemPhases.TryGetValue(system, out var phase))
                    continue;

                if (!result.TryGetValue(phase, out var list))
                {
                    list = new List<(IEcsModuleSystem System, SystemProfileData Profile)>();
                    result[phase] = list;
                }

                list.Add((system, profile));
            }

            return result;
        }
        
        /// <summary>
        /// Debug output of execution order.
        /// </summary>
        public string ToDebugString()
        {
            var sb = new StringBuilder();
            
            foreach (var (phase, systems) in _sortedSystems.OrderBy(kvp => (int)kvp.Key))
            {
                sb.AppendLine($"PHASE: {phase}");
                
                for (int i = 0; i < systems.Count; i++)
                {
                    var system = systems[i];
                    var profile = _profileData[system];
                    
                    sb.AppendLine($"  {i + 1}. {system.GetType().Name}");
                    
                    if (profile.ExecutionCount > 0)
                    {
                        sb.AppendLine($"     Avg: {profile.AverageMs:F2}ms | " +
                                    $"Max: {profile.MaxMs:F2}ms | " +
                                    $"Runs: {profile.ExecutionCount}");
                    }
                    
                    // Show nested systems for groups
                    if (system is ISystemGroup group)
                    {
                        foreach (var nested in group.GetSystems())
                        {
                            if (_profileData.TryGetValue(nested, out var nestedProfile))
                            {
                                sb.AppendLine($"       -> {nested.GetType().Name} " +
                                            $"(Avg: {nestedProfile.AverageMs:F2}ms)");
                            }
                        }
                    }
                }
                
                sb.AppendLine();
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Registers a system in the Manual phase for diagnostics tracking.
        /// Returns a profiled wrapper that records execution time on each call.
        /// </summary>
        public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem
        {
            RegisterSystem(system);
            return new ProfiledManualSystemWrapper(system, this);
        }

        private sealed class ProfiledManualSystemWrapper : IEcsModuleSystem
        {
            private readonly IEcsModuleSystem _inner;
            private readonly SystemScheduler _scheduler;

            public ProfiledManualSystemWrapper(IEcsModuleSystem inner, SystemScheduler scheduler)
            {
                _inner     = inner;
                _scheduler = scheduler;
            }

            public void Execute(ISimulationView view, float deltaTime)
            {
                var profile = _scheduler.GetProfileData(_inner);
                var sw = Stopwatch.StartNew();
                try
                {
                    _inner.Execute(view, deltaTime);
                }
                finally
                {
                    sw.Stop();
                    profile?.RecordExecution(sw.Elapsed.TotalMilliseconds);
                }
            }
        }
    }
    
    /// <summary>
    /// Exception thrown when circular dependencies detected.
    /// </summary>
    public class CircularDependencyException : Exception
    {
        public CircularDependencyException(string message) : base(message) { }
    }
    
    /// <summary>
    /// Exception thrown when system execution fails.
    /// </summary>
    public class SystemExecutionException : Exception
    {
        public SystemExecutionException(string message, Exception inner) 
            : base(message, inner) { }
    }
}
