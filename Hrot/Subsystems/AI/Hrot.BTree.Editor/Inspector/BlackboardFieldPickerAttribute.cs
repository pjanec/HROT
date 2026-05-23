using System;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Marker attribute for StructEdit fields that should render as a blackboard field
/// picker dropdown constrained to fields compatible with the action method's expression-target type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BlackboardFieldPickerAttribute : Attribute { }
