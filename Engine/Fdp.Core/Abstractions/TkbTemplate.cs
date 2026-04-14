using System;
using System.Collections.Generic;
using Fdp.Core;

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
        /// List of ECS-native component requirements that must be physically present on the
        /// ghost entity before the <c>GhostPromotionSystem</c> can promote it.
        ///
        /// <para>The system checks each requirement against the entity's live
        /// <c>ComponentMask</c> (O(1) bitmask lookup), making this architecture completely
        /// decoupled from the DDS network layer.</para>
        ///
        /// <para><c>TkbIdentity</c> is always implicitly a hard requirement and does not
        /// need to be listed here explicitly.</para>
        /// </summary>
        public List<MandatoryComponent> MandatoryComponents { get; } = new();

        /// <summary>
        /// List of child entities (sub-parts) to spawn when this template is instantiated.
        /// </summary>
        public List<ChildBlueprintDefinition> ChildBlueprints { get; } = new();

        // We use delegates to abstract the type-specific SetComponent calls.
        private readonly List<Action<EntityRepository, Entity, bool>> _applicators = new();

        /// <summary>
        /// DIS Entity Type associated with this template.
        /// Set by <see cref="NedTkbBuilder"/> during catalog registration.
        /// When non-zero, <see cref="ApplyTo"/> stamps it onto the entity header via
        /// <see cref="EntityRepository.SetDisType"/> so that rendering systems can
        /// perform bitwise layer-mask evaluation without string look-ups.
        /// </summary>
        public DISEntityType DisType { get; set; }

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
        /// Registers an ECS component type as mandatory for ghost promotion.
        ///
        /// <para>Works for both unmanaged structs and managed class components.
        /// <c>ComponentTypeRegistry.GetId(typeof(T))</c> returns the correct ID for both.</para>
        /// </summary>
        /// <typeparam name="T">The component type to require.</typeparam>
        /// <param name="isHard">
        ///   <c>true</c> (default) — promotion is blocked indefinitely until the component
        ///   arrives.<br/>
        ///   <c>false</c> — promotion proceeds after <paramref name="softTimeoutFrames"/>
        ///   frames.
        /// </param>
        /// <param name="softTimeoutFrames">
        ///   For soft requirements: frames to wait after ghost creation before giving up.
        /// </param>
        public void AddMandatoryComponent<T>(bool isHard = true, uint softTimeoutFrames = 0)
        {
            MandatoryComponents.Add(new MandatoryComponent
            {
                ComponentTypeId  = ComponentTypeRegistry.GetOrRegisterManaged(typeof(T)),
                IsHard           = isHard,
                SoftTimeoutFrames = softTimeoutFrames
            });
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
        /// Also stamps <see cref="DisType"/> onto the entity header when it is non-zero.
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

            // Stamp DIS type directly into the entity header so the rendering hot-path
            // can evaluate layer membership via a single integer comparison.
            if (DisType.Value != 0)
                repo.SetDisType(entity, DisType);
        }
    }
}
