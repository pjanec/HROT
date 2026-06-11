namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// Marker attribute for StructEdit fields that should render as a blackboard field
/// picker dropdown constrained to fields compatible with the transition's action
/// method's expression-target type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HsmBlackboardFieldPickerAttribute : Attribute { }
