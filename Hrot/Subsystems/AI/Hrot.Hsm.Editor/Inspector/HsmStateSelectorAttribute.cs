namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM state selector.
// Populated from the current asset's AllStates list.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmStateSelectorAttribute : Attribute { }
