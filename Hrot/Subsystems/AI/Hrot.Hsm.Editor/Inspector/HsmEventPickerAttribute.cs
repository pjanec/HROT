namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM event picker.
// Populated from the current asset's event list.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmEventPickerAttribute : Attribute { }
