namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM action picker.
// Populated from HsmActionDispatcher.AllActions.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmActionPickerAttribute : Attribute { }
