// Type-forwarding aliases: re-export the canonical GizmoMap.Contracts primitive types
// so all code in this assembly uses the single CLR type from GizmoMap.Contracts, eliminating
// the CLR type-identity fracture that previously existed between the two assemblies.
//
// NOTE: FixedString32 is intentionally NOT aliased here. The FDP stack uses
// Fdp.Core.FixedString32 throughout its interface layer for backward compatibility.
// DebugPrimitiveBuffer.cs performs Unsafe.As reinterpret at write boundaries.
extern alias GizmoMapContracts;

global using CoordinateSpace    = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.CoordinateSpace;
global using DebugPrimitive     = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitive;
global using DebugPrimitiveShape = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape;
global using PipelineTarget     = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.PipelineTarget;
global using Rgba32             = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.Rgba32;
global using ScreenAnchor       = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.ScreenAnchor;
global using SizeMode           = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.SizeMode;
global using StringInternMap    = GizmoMapContracts::Fdp.Toolkit.Diagnostics.Gizmos.StringInternMap;
