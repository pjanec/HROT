namespace Fdp.Kernel
{
    /// <summary>
    /// Defines write permissions for a specific phase.
    /// </summary>
    public enum PhasePermission
    {
        /// <summary>
        /// Read-only access. No structural changes or component updates.
        /// </summary>
        ReadOnly = 0,
        
        /// <summary>
        /// Unrestricted read/write access.
        /// </summary>
        ReadWriteAll = 1,
        
        /// <summary>
        /// Can only modify components where HasAuthority() is true.
        /// </summary>
        OwnedOnly = 2,
        
        /// <summary>
        /// Can only modify components where HasAuthority() is false.
        /// </summary>
        UnownedOnly = 3
    }
    
    /// <summary>
    /// Registry for phase IDs to avoid string comparisons on hot paths.
    /// </summary>
    internal static class PhaseRegistry
    {
        private static int _nextId = 0;
        private static readonly System.Collections.Generic.Dictionary<string, int> _nameToId 
            = new System.Collections.Generic.Dictionary<string, int>();
        private static readonly object _lock = new object();
        
        public static int GetOrCreateId(string name)
        {
            lock (_lock)
            {
                if (_nameToId.TryGetValue(name, out int id))
                    return id;
                
                id = _nextId++;
                _nameToId[name] = id;
                return id;
            }
        }
    }
    
    /// <summary>
    /// Represents an execution phase in the engine loop.
    /// Phases are defined by their name and attributes (permissions, transitions).
    /// Uses integer IDs internally for O(1) comparisons on hot paths.
    /// </summary>
    public class Phase : System.IEquatable<Phase>
    {
        public string Name { get; }
        
        /// <summary>
        /// Unique ID for this phase. Used for fast comparisons on hot paths.
        /// </summary>
        internal int Id { get; }
        
        // Common phase names (convenience, not required)
        public static readonly Phase Initialization = new Phase("Initialization");
        public static readonly Phase NetworkReceive = new Phase("NetworkReceive");
        public static readonly Phase Simulation = new Phase("Simulation");
        public static readonly Phase NetworkSend = new Phase("NetworkSend");
        public static readonly Phase Presentation = new Phase("Presentation");
        
        public Phase(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new System.ArgumentException("Phase name cannot be null or empty", nameof(name));
            
            Name = name;
            Id = PhaseRegistry.GetOrCreateId(name);
        }
        
        public override string ToString() => Name;
        
        // HOT PATH: Use integer comparison
        public override int GetHashCode() => Id;
        public override bool Equals(object? obj) => obj is Phase other && Equals(other);
        public bool Equals(Phase? other) => other != null && Id == other.Id;
        
        // HOT PATH: Integer comparison, not string
        public static bool operator ==(Phase? left, Phase? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            return left.Id == right.Id;  // Fast integer comparison
        }
        
        public static bool operator !=(Phase? left, Phase? right) => !(left == right);
    }
    
    public class WrongPhaseException : System.Exception
    {
        public WrongPhaseException(Phase current, Phase required) 
            : base($"Operation required phase {required} but current phase is {current}") { }
            
        public WrongPhaseException(string message) : base(message) { }
    }
    
    /// <summary>
    /// Configuration for the Phase System.
    /// Defines valid transitions and permissions for each phase.
    /// Uses string names for configuration, but internally converts to Phase objects with IDs.
    /// </summary>
    public class PhaseConfig
    {
        /// <summary>
        /// Valid phase transitions: from phase name -> allowed destination phase names.
        /// String-based for easy configuration.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>> ValidTransitions { get; set; } 
            = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
        
        /// <summary>
        /// Permission level for each phase name.
        /// String-based for easy configuration.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, PhasePermission> Permissions { get; set; } 
            = new System.Collections.Generic.Dictionary<string, PhasePermission>();
        
        // Internal cache: Phase ID -> Permission (for hot path)
        private System.Collections.Generic.Dictionary<int, PhasePermission>? _idToPermissionCache;
        
        // Internal cache: Phase ID -> Allowed destination IDs (for hot path)
        private System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>? _idTransitionsCache;
        
        /// <summary>
        /// Returns the default strict configuration.
        /// Follows the pattern: Initialization -> NetworkReceive -> Simulation -> NetworkSend -> Presentation -> (loop)
        /// </summary>
        public static PhaseConfig Default
        {
            get
            {
                var config = new PhaseConfig();
                
                // Strict Transitions
                config.ValidTransitions["Initialization"] = new System.Collections.Generic.HashSet<string> { "NetworkReceive" };
                config.ValidTransitions["NetworkReceive"]  = new System.Collections.Generic.HashSet<string> { "Simulation" };
                config.ValidTransitions["Simulation"]      = new System.Collections.Generic.HashSet<string> { "NetworkSend" };
                config.ValidTransitions["NetworkSend"]     = new System.Collections.Generic.HashSet<string> { "Presentation" };
                config.ValidTransitions["Presentation"]    = new System.Collections.Generic.HashSet<string> { "NetworkReceive" };
                
                // Strict Permissions
                config.Permissions["Initialization"] = PhasePermission.ReadWriteAll;
                config.Permissions["NetworkReceive"] = PhasePermission.UnownedOnly;
                config.Permissions["Simulation"]     = PhasePermission.OwnedOnly;
                config.Permissions["NetworkSend"]    = PhasePermission.ReadOnly;
                config.Permissions["Presentation"]   = PhasePermission.ReadOnly;
                
                return config;
            }
        }
        
        /// <summary>
        /// Returns a relaxed configuration (allows all transitions and writes).
        /// Useful for simpler apps or tests.
        /// </summary>
        public static PhaseConfig Relaxed
        {
            get
            {
                var config = new PhaseConfig();
                
                // Populate common phases as fully permissive
                var commonPhases = new[] { "Initialization", "NetworkReceive", "Simulation", "NetworkSend", "Presentation" };
                var allCommonSet = new System.Collections.Generic.HashSet<string>(commonPhases);
                
                foreach (var phaseName in commonPhases)
                {
                    config.Permissions[phaseName] = PhasePermission.ReadWriteAll;
                    config.ValidTransitions[phaseName] = allCommonSet;
                }
                
                return config;
            }
        }
        
        /// <summary>
        /// Builds internal caches for fast lookups. Called by EntityRepository.
        /// HOT PATH optimization: Converts string-based config to ID-based lookups.
        /// </summary>
        internal void BuildCache()
        {
            _idToPermissionCache = new System.Collections.Generic.Dictionary<int, PhasePermission>();
            _idTransitionsCache = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>();
            
            // Cache permissions by phase ID
            foreach (var kvp in Permissions)
            {
                var phase = new Phase(kvp.Key);
                _idToPermissionCache[phase.Id] = kvp.Value;
            }
            
            // Cache transitions by phase ID
            foreach (var kvp in ValidTransitions)
            {
                var fromPhase = new Phase(kvp.Key);
                var allowedIds = new System.Collections.Generic.HashSet<int>();
                
                foreach (var toName in kvp.Value)
                {
                    var toPhase = new Phase(toName);
                    allowedIds.Add(toPhase.Id);
                }
                
                _idTransitionsCache[fromPhase.Id] = allowedIds;
            }
        }
        
        /// <summary>
        /// HOT PATH: Gets permission for a phase using cached ID lookup.
        /// Returns ReadWriteAll if not specified (permissive default).
        /// </summary>
        internal PhasePermission GetPermissionById(int phaseId)
        {
            return _idToPermissionCache != null && _idToPermissionCache.TryGetValue(phaseId, out var perm) 
                ? perm 
                : PhasePermission.ReadWriteAll;
        }
        
        /// <summary>
        /// HOT PATH: Checks if a transition is valid using cached ID lookup.
        /// Returns true if not configured (permissive by default for unknown phases).
        /// </summary>
        internal bool IsTransitionValidById(int fromPhaseId, int toPhaseId)
        {
            if (_idTransitionsCache == null || !_idTransitionsCache.TryGetValue(fromPhaseId, out var allowed))
                return true;  // If no transitions configured, allow all
            
            return allowed.Contains(toPhaseId);
        }
        
        /// <summary>
        /// Helper to get permission for a phase by name (used for configuration/debugging).
        /// NOT for hot path - use GetPermissionById instead.
        /// </summary>
        public PhasePermission GetPermission(string phaseName)
        {
            return Permissions.TryGetValue(phaseName, out var perm) ? perm : PhasePermission.ReadWriteAll;
        }
        
        /// <summary>
        /// Helper to check if a transition is valid by names (used for configuration/debugging).
        /// NOT for hot path - use IsTransitionValidById instead.
        /// </summary>
        public bool IsTransitionValid(string fromPhase, string toPhase)
        {
            if (!ValidTransitions.TryGetValue(fromPhase, out var allowed))
                return true;
            
            return allowed.Contains(toPhase);
        }
    }
}
