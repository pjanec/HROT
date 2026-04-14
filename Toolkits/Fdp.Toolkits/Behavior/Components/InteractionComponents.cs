using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// Fixed passenger roster on a vehicle entity.
    /// Managed by <c>EmbarkExecutor</c> (add) and <c>EjectPassengersExecutor</c> (clear).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.PassengerBuffer)]
    public struct PassengerBuffer
    {
        public const int Capacity = 8;

        /// <summary>Inline array of <see cref="Entity"/> handles for passengers.</summary>
        public PassengerSlots Passengers;

        /// <summary>Number of passengers currently aboard (0–<see cref="Capacity"/>).</summary>
        public int Count;
    }

    /// <summary>
    /// Inline array of 8 <see cref="Entity"/> slots for <see cref="PassengerBuffer"/>.
    /// </summary>
    [InlineArray(PassengerBuffer.Capacity)]
    public struct PassengerSlots
    {
#pragma warning disable IDE0044 // backing field required by InlineArray
        private Entity _element;
#pragma warning restore IDE0044
    }

    /// <summary>
    /// Tag component placed on a soldier entity that is currently embarked in a vehicle.
    /// Added by <c>EmbarkExecutor</c>; removed by <c>EjectPassengersExecutor</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.IsEmbarkedTag)]
    public struct IsEmbarkedTag
    {
        /// <summary>The vehicle entity this soldier is currently aboard.</summary>
        public Entity VehicleEntity;
    }
}
