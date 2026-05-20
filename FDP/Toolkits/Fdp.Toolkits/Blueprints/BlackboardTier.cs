namespace Fdp.Toolkit.Blueprints;

/// <summary>Blackboard memory tier selection for Blueprint state storage.</summary>
public enum BlackboardTier
{
    B1024  = 0,   // up to 928 bytes of state
    B4096  = 1,   // up to 3936 bytes of state
    B16384 = 2,   // up to 16368 bytes of state
}
