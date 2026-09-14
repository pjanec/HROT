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
        ///
        /// <para>⛔⛔ <b>A TKB FILE CANNOT FILL THIS LIST.</b> 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6: a
        /// TKB file describes an entity's <b>descriptors</b>; this is a statement about what those descriptors
        /// will PRODUCE <i>on a particular host</i>. <c>TkbDeserializer</c> builds a template purely from
        /// descriptor keys, so a file-loaded template arrives with this list <b>EMPTY</b> — deliberately.</para>
        ///
        /// <para>✅✅✅ <b>THIS IS NO LONGER THE PRIMARY SOURCE</b> <i>(<c>CE-265</c>, <c>2026-09-13</c>; an
        /// earlier version of this comment said "NOTHING DERIVES IT")</i>. <c>MandatoryComponentResolver</c>
        /// now DERIVES the requirements per host — <c>[PerInstanceValue] ∩ produced ∩ ingressible ∩
        /// registered</c> — and <c>GhostPromotionSystem</c> gates on <b>derived ∪ this list</b>.
        /// 📄 §6.6a (design) and §6.6b (as-built).</para>
        ///
        /// <para>⛔⛔ <b>WHY THE DERIVED SET IS NOT STORED HERE, unlike
        /// <see cref="BirthCriticalComponents"/>.</b> Three of its four inputs are HOST-LOCAL — this node's
        /// translators, its network stack, its component registry — while a <see cref="TkbTemplate"/> is a
        /// SHARED record: the same object serves CGF, SimHost, IG and the editor. ⇒ a host-local answer
        /// stored on it would be wrong for every other reader. 🔒 That is the same fact that rules a TKB file
        /// out, stated one level down.</para>
        ///
        /// <para>⭐ <b>WHAT THIS LIST IS FOR NOW — the AUTHORING ESCAPE HATCH.</b> It carries requirements the
        /// derivation cannot see: a component <b>no translator produces</b> (managed state such as
        /// <c>ActiveMissionPlan</c>), or a host-specific network gate. ⛔ Do NOT restate a derived
        /// requirement here — 📐 measured <c>2026-09-13</c>, that is exactly how the catalogues drifted:
        /// <c>NedTkbBuilder.DefineVehicle</c> declared <c>EntityInfo</c>+<c>SimTransform</c> hard while
        /// <c>UrbanCombatTkbCatalog</c>'s five identically-shaped templates declared none.</para>
        ///
        /// <para>🔴 <b>OVER-DECLARING HERE IS FATAL, and in the opposite direction to every other list on this
        /// type.</b> This is the PROMOTION GATE: a hard requirement that never arrives means a ghost that
        /// <b>never promotes</b>, every frame, forever. ⚠ <c>GhostPromotionSystem</c> now reports such a stall
        /// after <c>600</c> frames instead of failing silently — but it still does not promote.</para>
        /// </summary>
        public List<MandatoryComponent> MandatoryComponents { get; } = new();

        /// <summary>
        /// ⭐⭐⭐ <b>Component type ids whose INITIAL VALUE the entity's CREATOR must own at birth,
        /// whatever role that creator holds.</b> 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.1.
        ///
        /// <para>⭐⭐ <b>Why this exists.</b> Role-affinity ownership says <i>"a node owns a component
        /// only if it holds the role that component belongs to"</i>. ⛔ Applied to spatial state that rule
        /// is WRONG: a Brain-role node creating a unit would produce <c>SimTransform</c> UNOWNED.
        /// ⇒ birth-critical components are the CREATOR'S BIRTHRIGHT; it keeps them and hands them off
        /// later through the existing <c>DeferredTakeOwnership</c> → <c>OwnershipUpdate</c> path.</para>
        ///
        /// <para>⛔⛔ <b>THE MECHANISM THIS COMMENT USED TO CITE IS RETRACTED</b> <i>(re-measured
        /// <c>2026-09-13</c>, <c>DESIGN_Role_Affinity_Ownership.md</c> §3.6)</i>. It said <i>"every egress
        /// translator gates on <c>HasAuthority</c>, so the coordinate is written and never published —
        /// every peer's ghost sits at the origin."</i> 🔴 <b>No egress translator reads the per-component
        /// <c>AuthorityMask</c> at all</b> — they call the <c>ISimulationView</c> extension, which consults
        /// <c>DescriptorOwnership</c>/<c>NetworkAuthority</c>. ⇒ declining a mask bit does not stop
        /// publication. ⭐ <b>What actually breaks is quieter:</b> <c>CarKinematicsSystem.cs:73</c> filters
        /// <c>.WithOwned&lt;SimTransform&gt;()</c> so <b>the entity never moves on the node that created
        /// it</b>, and <c>GeoSpatialIngressTranslator.cs:90</c> then treats it as remote and overwrites its
        /// position from the wire. ⚠ <b>The conclusion is unchanged; only the reason was wrong.</b></para>
        ///
        /// <para>⛔ <b>And no role may own one on the PROMOTE leg either</b> — the same
        /// <c>GeoSpatialIngressTranslator</c> check, mirrored: a promoting node that claimed
        /// <c>SimTransform</c> by role would declare itself owner of a position it does not simulate and
        /// stop accepting the real owner's updates ⇒ every ghost on it freezes. That is why the role
        /// tables exclude these ids entirely (<c>HrotRoleComponentSets</c>).</para>
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
        /// <para>✅ <b>THIS IS NOW LIVE</b> <i>(updated <c>2026-09-13</c>; an earlier version said "nothing
        /// READS this yet")</i>. <c>NetworkSpawningSystem.cs:237</c> ORs this set in for the creator and
        /// intersects with the live mask, and since P3 step 4 both CGF and SimHost hold real role
        /// policies ⇒ <b>a missing entry now changes behaviour on a live cluster.</b></para>
        ///
        /// <para>⭐⭐⭐ <b>DERIVED, NOT AUTHORED</b> (<c>2026-09-13</c>). This is a read-only view of every
        /// component type declaring <see cref="BirthCriticalAttribute"/> — it is not stored per template and
        /// cannot be edited. 📄 <c>docs/designs/tkb-1/DESIGN.md</c> §6.6a.</para>
        ///
        /// <para>🔒 User ruling: <i>"if it can be derived or defined via component attribute and it works for
        /// all todays or imaginable future use cases, the tkb in-memory record can be just a readonly
        /// cache."</i> ⛔ The hand-authored version could not survive the file path — <c>TkbDeserializer</c>
        /// builds templates purely from DESCRIPTOR keys, so a file-loaded template arrived EMPTY
        /// (<c>CE-259az</c>) — and 8 authoring sites across 3 producers had already drifted apart.</para>
        ///
        /// <para>⚠ <b>The effective set IS per TKB type, and this still delivers that.</b> Over-declaring is
        /// free because the create leg intersects with the entity's LIVE component mask, so a positionless
        /// entity never receives a bit. ⇒ the per-type answer is computed exactly, per entity, at runtime —
        /// which a stored list could only restate, and could restate wrongly.</para>
        ///
        /// <para>⭐ <b>This property is the seam for a future per-type OVERRIDE</b>, deliberately kept rather
        /// than deleted: a TKB-record override would change only how this view is produced, with no
        /// call-site churn. That override is DEFERRED, not rejected.</para>
        /// </summary>
        public IReadOnlyList<int> BirthCriticalComponents => ComponentAttributeSets.BirthCritical;

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
