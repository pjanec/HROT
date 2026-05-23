namespace Hrot.Editor.AiShared.Debug;

[Flags]
public enum TraceLevel
{
    None      = 0,
    Lifecycle = 1 << 0,
    Decisions = 1 << 1,
    Values    = 1 << 2,
    Async     = 1 << 3,
    Errors    = 1 << 4,
    All       = Lifecycle | Decisions | Values | Async | Errors,
}
