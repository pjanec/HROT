using System;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Marker attribute for StructEdit fields that should render as a behavior-method picker
/// dropdown populated from the editor's BehaviorRegistry.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BehaviorHashPickerAttribute : Attribute { }
