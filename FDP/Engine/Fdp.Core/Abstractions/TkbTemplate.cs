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
        /// ⭐⭐⭐ <b>Component type ids whose INITIAL VALUE the entity's CREATOR must own at birth,
        /// whatever role that creator holds.</b> 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1.
        ///
        /// <para>⭐⭐ <b>Why this exists.</b> Role-affinity ownership says <i>"a node owns a component
        /// only if it holds the role that component belongs to"</i>. ⛔ Applied to spatial state that
        /// rule is WRONG, and the architect named the failure exactly: a Brain-role node creating a unit
        /// would produce <c>SimTransform</c> UNOWNED, and since <b>every egress translator gates on
        /// <c>HasAuthority</c></b>, the creator would write a correct spawn coordinate that is
        /// <b>never published</b> — every peer's ghost sits at the origin. ⇒ birth-critical components
        /// are the CREATOR'S BIRTHRIGHT; it keeps them and hands them off later through the existing
        /// <c>DeferredTakeOwnership</c> → <c>OwnershipUpdate</c> path.</para>
        ///
        /// <para>⭐ <b>The test is "can this component start empty?"</b> An idle blackboard on tick 0 is
        /// correct, so cognitive state is role-affine. <c>(0,0,0)</c> is an origin flash, a wrong
        /// spatial-hash cell and a bogus first path query, so position is not.</para>
        ///
        /// <para>⛔ <b>A COMPONENT property, never a descriptor one</b> — 🔒 user, <c>2026-09-01</c>:
        /// <i>"there are networkless systems as well… TKB should define what components are birth
        /// critical."</i> A descriptor is a networking concept; a node with no participant has no
        /// descriptor mapping, so a descriptor-keyed definition would be undefined exactly where the
        /// component still exists.</para>
        ///
        /// <para>⭐⭐ <b>OVER-DECLARING IS HARMLESS BY CONSTRUCTION, so declare it wherever it could
        /// apply.</b> The create leg intersects this set with the entity's <b>live component mask</b>
        /// (<c>AuthorityMask = componentMask ∧ OwnableMask(...)</c>), so naming a component the entity
        /// never receives contributes no bits. ⛔ UNDER-declaring is the dangerous direction — it is
        /// silent, and it surfaces as the origin flash above.</para>
        ///
        /// <para>⚠ <b>Deliberately a SECOND list rather than a flag on <see cref="MandatoryComponents"/></b>
        /// — they answer different questions: <i>"must be PRESENT before promotion"</i> versus
        /// <i>"the creator must OWN it at birth"</i>. Overloading the first would force a
        /// birth-critical-but-not-promotion-gating component to change promotion semantics in order to
        /// carry the flag.</para>
        ///
        /// <para>⚠ <b>Nothing READS this yet.</b> The consumers are steps 1–3 of that design
        /// (<c>IRoleAffinityPolicy</c>, then <c>NetworkSpawningSystem</c> and
        /// <c>GhostPromotionSystem</c>). Until they land this list is declarative only and ownership
        /// behaviour is unchanged.</para>
        /// </summary>
        public List<int> BirthCriticalComponents { get; } = new();

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
        /// ⭐ Declares an ECS component type <b>birth-critical</b>: the node that CREATES an entity of
        /// this template owns that component at birth regardless of its role.
        /// See <see cref="BirthCriticalComponents"/> for why, and for why over-declaring is safe.
        ///
        /// <para>Mirrors <see cref="AddMandatoryComponent{T}"/> — same authoring style, same id
        /// resolution (works for unmanaged structs and managed class components alike), and the same
        /// independence from any network layer.</para>
        ///
        /// <para>⭐ Idempotent: declaring the same component twice does not duplicate the entry, so a
        /// builder that both defines a template and later decorates it cannot double-register.</para>
        /// </summary>
        /// <typeparam name="T">The component type whose initial value the creator must own.</typeparam>
        public void AddBirthCriticalComponent<T>()
        {
            int id = ComponentTypeRegistry.GetOrRegisterManaged(typeof(T));
            if (!BirthCriticalComponents.Contains(id))
                BirthCriticalComponents.Add(id);
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
