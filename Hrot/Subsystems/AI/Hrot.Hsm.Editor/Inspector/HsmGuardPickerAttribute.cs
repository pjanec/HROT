namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM guard picker.
// Populated from HsmActionDispatcher.AllGuards.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmGuardPickerAttribute : Attribute { }
