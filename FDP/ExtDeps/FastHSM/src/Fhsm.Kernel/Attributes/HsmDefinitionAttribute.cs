using System;

namespace Fhsm.Kernel.Attributes
{
    // Marks a static method returning HsmDefinitionBlob as a named HSM asset
    // to be catalogued by the HSM asset contributor.
    //
    // CE-371: the method may return EITHER HsmDefinitionBlob OR StateMachineGraph. A graph-returning
    // method lets HsmDefinitionGenerator run Normalize -> Flatten -> Emit in ONE place and emit the
    // MachineMetadata beside the blob, which is what closes CE-370 for every HSM at once. This
    // mirrors BTreeDefinitionAttribute, whose method may return a blob or a BTreeBuilder.
    // Method must be static and have zero parameters.
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class HsmDefinitionAttribute : Attribute
    {
        // The logical name of the state machine (used as the catalog key).
        public string MachineName { get; }

        // Optional stable asset GUID; used for identity across renames.
        // If null, the asset ID is derived from MachineName via FNV-1a-32.
        public string? AssetId { get; set; }

        // When true, signals that this asset uses an editor-managed companion blackboard file
        // (e.g. {AssetName}.Blackboard.cs). The runtime ignores this flag; it is read by the
        // HROT HSM editor. Default is false -- all existing assets are unaffected.
        public bool BlackboardManaged { get; set; }

        // When set, the source generator wires BehaviorIngressSystem to provision a
        // Blackboard1024 component for this behavior. Null means no heavy component is attached.
        // Default is null -- existing behavior is preserved.
        public Type? HeavyDtoType { get; set; }

        // CE-372: the params struct this machine's root params region is laid out as, carried onto
        // BehaviorDefinition.BlackboardLayoutType by the generated curated registrar.
        // Null means the behaviour takes no params (RootParamsBytes == 0), which is legitimate --
        // the Idle machine is exactly that case.
        // NOTE this is the LAYOUT type, not the authored JSON contract: the JSON contract is declared
        // by [BehaviorContract] on the DTO and bound by BehaviorSchemaDiscovery.AutoRegister.
        // Setting JsonParamsDtoType from here would be a SECOND producer for one slot (R-132, CE-235).
        public Type? ParamsType { get; set; }

        // CE-373: opt IN to being registered as a curated BEHAVIOUR by the generated
        // CuratedBehaviorRegistrar. Default false.
        // The JSON-authored machines already have generated registrars that own their topology, so
        // registering them again would hard-error on a duplicate name. The flag is the explicit
        // signal; see BTreeDefinitionAttribute.Curated for the measurement that made it necessary.
        public bool Curated { get; set; }

        public HsmDefinitionAttribute(string machineName)
        {
            MachineName = machineName;
        }
    }
}
