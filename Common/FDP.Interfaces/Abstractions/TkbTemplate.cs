using System;
using System.Collections.Generic;
using Fdp.Kernel;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Represents a blueprint for spawning entities.
    /// Contains a list of components to apply to the new entity.
    /// </summary>
    public class TkbTemplate
    {
        /// <summary>
        /// Unique type identifier (primary key).
        /// </summary>
        public long TkbType { get; }

        /// <summary>
        /// Unique identifier for this template (Name).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// List of descriptors that must be present before ghost promotion.
        /// EntityMaster is implicitly always hard-required.
        /// </summary>
        public List<MandatoryDescriptor> MandatoryDescriptors { get; } = new();

        /// <summary>
        /// List of child entities (sub-parts) to spawn when this template is instantiated.
        /// </summary>
        public List<ChildBlueprintDefinition> ChildBlueprints { get; } = new();

        // We use delegates to abstract the type-specific SetComponent calls.
        private readonly List<Action<EntityRepository, Entity, bool>> _applicators = new();

        public TkbTemplate(string name, long tkbType)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));
            if (tkbType == 0)
                throw new ArgumentException("TkbType cannot be zero", nameof(tkbType));
            
            Name = name;
            TkbType = tkbType;
        }

        /// <summary>
        /// Checks if all hard mandatory descriptors are present in the given set.
        /// </summary>
        public bool AreHardRequirementsMet(IReadOnlyCollection<long> availableKeys)
        {
            foreach (var req in MandatoryDescriptors)
            {
                // Note: assuming Contains is efficient enough or availableKeys is a HashSet
                // IReadOnlyCollection doesn't imply Contains is O(1).
                // But usage is typically with HashSet.
                // Linq.Contains might be used if explicit Contains not on interface?
                // IReadOnlyCollection<T> does not have Contains directly. System.Linq has it.
                // I should assume using System.Linq;
                bool found = false;
                foreach (var k in availableKeys)
                {
                    if (k == req.PackedKey)
                    {
                        found = true;
                        break;
                    }
                }
                
                if (req.IsHard && !found)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Checks if all requirements (Hard and Soft) are met.
        /// Soft requirements are met if present OR if timeout has elapsed since identification.
        /// </summary>
        public bool AreAllRequirementsMet(IReadOnlyCollection<long> availableKeys, uint currentFrame, uint identifiedAtFrame)
        {
            foreach (var req in MandatoryDescriptors)
            {
                bool found = false;
                foreach (var k in availableKeys)
                {
                    if (k == req.PackedKey) { found = true; break; }
                }
                
                if (found) continue;

                if (req.IsHard) return false;

                // Check soft timeout
                // If not identified yet (identifiedAtFrame == 0), we treat as timeout not started?
                // But this method is generally called when identified.
                // Assuming identifiedAtFrame > 0.
                if (currentFrame - identifiedAtFrame <= req.SoftTimeoutFrames)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Adds an unmanaged component to the template.
        /// The value is copied when adding, and copied again when spawning.
        /// Components that are not registered in the target repository are silently skipped,
        /// allowing shared templates to be applied on both server (SimHost) and client (IG) worlds.
        /// </summary>
        public void AddComponent<T>(T component) where T : unmanaged
        {
            _applicators.Add((repo, entity, preserve) =>
            {
                if (!repo.IsComponentTypeRegistered<T>()) return; // skip simulation-only components on client worlds
                if (preserve && repo.HasComponent<T>(entity))
                {
                    return;
                }
                repo.AddComponent(entity, component);
            });
        }

        /// <summary>
        /// Adds a managed component using a factory function.
        /// The factory is called each time an entity is spawned, ensuring a fresh instance.
        /// </summary>
        public void AddManagedComponent<T>(Func<T> factory) where T : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            
            _applicators.Add((repo, entity, preserve) =>
            {
                if (preserve && repo.HasManagedComponent<T>(entity))
                {
                    return;
                }
                var instance = factory();
                repo.SetManagedComponent(entity, instance);
            });
        }

        /// <summary>
        /// Applies all components in this template to the target entity.
        /// </summary>
        /// <param name="repo">The repository to modify.</param>
        /// <param name="entity">The target entity.</param>
        /// <param name="preserveExisting">If true, existing components on the entity will NOT be overwritten.</param>
        public void ApplyTo(EntityRepository repo, Entity entity, bool preserveExisting = false)
        {
            foreach (var apply in _applicators)
            {
                apply(repo, entity, preserveExisting);
            }
        }
    }
}
