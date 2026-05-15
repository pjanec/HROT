using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Represents a blueprint for spawning entities.
    /// Contains descriptor DTOs and mandatory component requirements.
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
        /// File-system category derived from the VFS path when loading from TKB files.
        /// Empty string for programmatically-registered templates.
        /// </summary>
        public string CategoryPath { get; }

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

        private readonly Dictionary<(Type, int), object> _descriptors = new();

        /// <summary>
        /// DIS Entity Type associated with this template.
        /// Set by <see cref="NedTkbBuilder"/> during catalog registration.
        /// </summary>
        public DISEntityType DisType { get; set; }

        public TkbTemplate(string name, long tkbType, string categoryPath = "")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));
            if (tkbType == 0)
                throw new ArgumentException("TkbType cannot be zero", nameof(tkbType));

            Name         = name;
            TkbType      = tkbType;
            CategoryPath = categoryPath ?? "";
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
                ComponentTypeId   = ComponentTypeRegistry.GetOrRegisterManaged(typeof(T)),
                IsHard            = isHard,
                SoftTimeoutFrames = softTimeoutFrames
            });
        }

        /// <summary>
        /// Stores a descriptor DTO in the bag. Uses (Type, partId) as the key.
        /// Overwrites any previously stored descriptor with the same key.
        /// </summary>
        public void AddDescriptor<T>(T descriptor, int partId = 0) where T : notnull
        {
            _descriptors[(typeof(T), partId)] = descriptor;
        }

        /// <summary>
        /// Retrieves a descriptor of type T (for reference types).
        /// Returns null if not found.
        /// </summary>
        public T? GetDescriptor<T>(int partId = 0) where T : class
        {
            _descriptors.TryGetValue((typeof(T), partId), out var obj);
            return obj as T;
        }

        /// <summary>
        /// Tries to retrieve a descriptor of type T (for value types).
        /// Returns false if not found.
        /// </summary>
        public bool TryGetDescriptor<T>(out T descriptor, int partId = 0) where T : struct
        {
            if (_descriptors.TryGetValue((typeof(T), partId), out var obj) && obj is T typed)
            {
                descriptor = typed;
                return true;
            }
            descriptor = default;
            return false;
        }

        /// <summary>
        /// Returns true if a descriptor of type T (with the given partId) is present.
        /// </summary>
        public bool HasDescriptor<T>(int partId = 0)
        {
            return _descriptors.ContainsKey((typeof(T), partId));
        }

        /// <summary>
        /// Enumerates all stored descriptors as (Type, PartId, Data) tuples.
        /// </summary>
        public IEnumerable<(Type Type, int PartId, object Data)> GetAllDescriptors()
        {
            foreach (var kv in _descriptors)
                yield return (kv.Key.Item1, kv.Key.Item2, kv.Value);
        }
    }
}
