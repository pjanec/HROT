namespace Hrot.Hsm.Editor.Inspector;

// Marker attribute for StructEdit fields that render as an HSM sync-group picker.
// Populated from all distinct SyncGroupIds in the current asset's transitions.
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmSyncGroupPickerAttribute : Attribute { }
