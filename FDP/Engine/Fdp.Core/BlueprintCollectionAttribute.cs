using System;

namespace Fdp.Core
{
    /// <summary>
    /// Marks the <c>public static int Count(in TComp)</c> curated accessor for a virtual COLLECTION
    /// exposed by an ECS component (the "R1 curated-accessor" model for blueprint component
    /// collection reads -- CA-07a). Paired with <see cref="BlueprintCollectionItemAttribute"/>
    /// (same <see cref="ComponentType"/> + <see cref="Name"/>) to declare one collection whose
    /// ELEMENT TYPE is the paired <c>Item</c> accessor's return type.
    ///
    /// <para>
    /// <b>Why this exists (architect Q#5-C):</b> some components carry a raw <c>fixed</c>/inline-array
    /// buffer (e.g. <c>UnitRoster.SubordinateEntities</c>) that requires <c>unsafe</c> pointer access
    /// to read. The architect ruling keeps that raw access OUT of the visual graph entirely, confined
    /// to a tiny curated static helper class instead (see <c>UnitRosterOps</c>). This attribute pair
    /// makes such a helper EDITOR-DISCOVERABLE (the component-type picker finds it via reflection at
    /// EDITOR time only) and REFLECTION-FREE-BAKEABLE (the discovered FQNs are baked as plain strings
    /// onto the node -- <see cref="Hrot.Blueprints.Core.Assets.ComponentFieldDecl.CountAccessorFqn"/>/
    /// <c>ItemAccessorFqn</c> -- so the netstandard2.0 compiler analyzer, which can never load game
    /// assemblies to reflect a real CLR type, only ever emits the two FQNs textually). This exactly
    /// mirrors how <c>FlowForEachNode</c> bakes its own <c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c>
    /// pair off <c>UnitRosterOps.Count</c>/<c>Subordinate</c> today -- CA-07a formalizes that ad-hoc
    /// pattern into a discoverable, attribute-driven one so ANY component can offer one.
    /// </para>
    ///
    /// <para>
    /// <b>Contract:</b> the attributed method MUST be <c>public static</c>, return <c>int</c>, and
    /// take a single parameter that is <c>in TComp</c> (or an equivalent byref-readonly of the
    /// component type) where <c>TComp</c> matches <see cref="ComponentType"/> -- otherwise the pair
    /// is silently ignored (see <c>ComponentFieldReflector.TryReflectCollections</c>). A lone
    /// <see cref="BlueprintCollectionAttribute"/> with no matching
    /// <see cref="BlueprintCollectionItemAttribute"/> (same <see cref="ComponentType"/>+
    /// <see cref="Name"/>) declares NO collection at all.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// public static class UnitRosterOps
    /// {
    ///     [BlueprintCollection(typeof(UnitRoster), "Subordinates")]
    ///     public static int Count(in UnitRoster r) => r.Count;
    ///
    ///     [BlueprintCollectionItem(typeof(UnitRoster), "Subordinates")]
    ///     public static Entity Item(in UnitRoster r, int i) => ...;
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class BlueprintCollectionAttribute : Attribute
    {
        /// <summary>The ECS component struct/class this collection is exposed off. Must match the paired <see cref="BlueprintCollectionItemAttribute.ComponentType"/>.</summary>
        public Type ComponentType { get; }

        /// <summary>Logical collection name (e.g. "Subordinates") -- becomes the baked <c>ComponentFieldDecl.Name</c> / the collection's out-pin name.</summary>
        public string Name { get; }

        public BlueprintCollectionAttribute(Type componentType, string name)
        {
            ComponentType = componentType;
            Name = name;
        }
    }

    /// <summary>
    /// Marks the <c>public static TElement Item(in TComp, int i)</c> curated accessor for a virtual
    /// collection exposed by an ECS component. Paired with <see cref="BlueprintCollectionAttribute"/>
    /// (same <see cref="ComponentType"/> + <see cref="Name"/>) -- see that attribute's doc comment
    /// for the full contract and the "why" (architect Q#5-C).
    ///
    /// <para>
    /// <b>Contract:</b> the attributed method MUST be <c>public static</c>, return a NON-<c>void</c>
    /// type (this becomes the collection's element/pin type -- <c>ComponentFieldDecl.ElementTypeId</c>),
    /// and take exactly two parameters: <c>in TComp</c> (or an equivalent byref-readonly of the
    /// component type) followed by <c>int</c> (the 0-based index) -- otherwise the pair is silently
    /// ignored. A lone <see cref="BlueprintCollectionItemAttribute"/> with no matching
    /// <see cref="BlueprintCollectionAttribute"/> declares NO collection at all.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class BlueprintCollectionItemAttribute : Attribute
    {
        /// <summary>The ECS component struct/class this collection is exposed off. Must match the paired <see cref="BlueprintCollectionAttribute.ComponentType"/>.</summary>
        public Type ComponentType { get; }

        /// <summary>Logical collection name (e.g. "Subordinates") -- must match the paired <see cref="BlueprintCollectionAttribute.Name"/>.</summary>
        public string Name { get; }

        public BlueprintCollectionItemAttribute(Type componentType, string name)
        {
            ComponentType = componentType;
            Name = name;
        }
    }
}
