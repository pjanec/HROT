namespace Bagira.SimHost.Components
{
    /// <summary>
    /// Maps an ECS entity to its allocated network entity ID.
    /// This is a local runtime component — not replicated over DDS directly.
    /// The actual replication key is carried by FDP.Kernel's built-in NetworkIdentity.
    /// </summary>
    public struct NetworkIdComponent
    {
        public int NetworkId;
    }
}
