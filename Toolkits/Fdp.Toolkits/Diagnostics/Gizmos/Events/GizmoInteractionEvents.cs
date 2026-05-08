using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Events
{
    [EventId(8051)]
    public struct GizmoInteractionStartedEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
    }

    [EventId(8052)]
    public struct GizmoDragUpdateEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
        // GZ047: coordinate space in which WorldPos is expressed.
        public CoordinateSpace Space;
    }

    [EventId(8053)]
    public struct GizmoInteractionCommitEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
        // GZ047: coordinate space in which WorldPos is expressed.
        public CoordinateSpace Space;
    }

    [EventId(8054)]
    public struct GizmoInteractionCancelEvent
    {
        public PickToken Token;
    }
}
