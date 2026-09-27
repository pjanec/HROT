using System;

namespace Fbt
{
    /// <summary>
    /// Marks a static method returning a BTreeBuilder or BehaviorTreeBlob as a named tree
    /// to be auto-catalogued by the Fbt.SourceGen source generator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDefinitionAttribute : Attribute
    {
        /// <summary>The logical name of the behavior tree (used as the catalog key).</summary>
        public string TreeName { get; }

        /// <summary>Stable editor asset GUID (8-4-4-4-12). Set by the editor codegen; null for hand-authored.</summary>
        public string? AssetId { get; set; }

        /// <summary>
        /// When true, signals that this asset uses an editor-managed companion blackboard file
        /// (e.g. {AssetName}.Blackboard.cs). The runtime ignores this flag; it is read by the
        /// HROT BTree editor. Default is false -- all existing assets are unaffected.
        /// </summary>
        public bool BlackboardManaged { get; set; }

        /// <summary>
        /// When set, the source generator wires BehaviorIngressSystem to provision a
        /// Blackboard1024 component for this behavior. Null means no heavy component is attached.
        /// Default is null -- existing behavior is preserved.
        /// </summary>
        public Type? HeavyDtoType { get; set; }

        /// <summary>
        /// CE-372: the params struct this tree's root params region is laid out as, carried onto
        /// <c>BehaviorDefinition.BlackboardLayoutType</c> by the generated curated registrar.
        /// Null means the behaviour takes no params (<c>RootParamsBytes == 0</c>) -- WanderMilitary
        /// is exactly that case.
        /// <para>
        /// NOTE this is the LAYOUT type, not the authored JSON contract: the JSON contract is
        /// declared by <c>[BehaviorContract]</c> on the DTO and bound by
        /// <c>BehaviorSchemaDiscovery.AutoRegister</c>. Setting <c>JsonParamsDtoType</c> from here
        /// would be a SECOND producer for one slot (R-132, CE-235).
        /// </para>
        /// </summary>
        public Type? ParamsType { get; set; }

        /// <summary>
        /// CE-373: opt IN to being registered as a curated BEHAVIOUR by the generated
        /// <c>CuratedBehaviorRegistrar</c>. Default false.
        /// <para>
        /// MEASURED 2026-09-27, and this is why the flag exists rather than "every [BTreeDefinition]
        /// is a behaviour": Hrot.AI.Behaviors carries NINE [BTreeDefinition] methods and only FIVE
        /// are curated topologies.
        /// <list type="bullet">
        ///   <item><c>HideInCover_BT</c> / <c>_v2</c> -- in the catalog, registered as a behaviour
        ///         NOWHERE.</item>
        ///   <item><c>PlatoonHillAttack</c> / <c>HullDownAttackRun</c> -- topology owned by their
        ///         GENERATED JSON registrars; only their RESOLVERS are curated. Registering these
        ///         here would hard-error on a duplicate name.</item>
        /// </list>
        /// So presence of the attribute is not the signal, and neither is a null AssetId. The opt-in
        /// is explicit and greppable.
        /// </para>
        /// </summary>
        public bool Curated { get; set; }

        public BTreeDefinitionAttribute(string treeName)
        {
            TreeName = treeName;
        }
    }
}
