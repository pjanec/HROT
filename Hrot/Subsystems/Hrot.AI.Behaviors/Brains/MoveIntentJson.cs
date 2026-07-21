using System.Text.Json;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Curated JSON-payload builder for <c>AssignTacticalIntentEvent{ IntentId="MoveToLocation" }</c>.
    /// Architect Q#6-C sanctioned this shape: a curated <c>FunctionCall</c> that serializes a typed DTO to
    /// the event's opaque <c>JsonParams</c> <b>string</b> field — the serialization/reflection stays in
    /// reviewable C#, and the visual graph publishes the resulting string via <c>PublishEvent</c> without
    /// any string-manipulation / JSON node. Mirrors the C# oracle
    /// <c>HillAttackCommanderNodes.Action_DispatchAllToBaseline</c>'s
    /// <c>JsonSerializer.Serialize(new CgfNodes.MoveToLocationParams{…}, FdpJsonOptionsRegistry.DefaultRelaxed)</c>.
    /// </summary>
    public static class MoveIntentJson
    {
        /// <summary>
        /// Serializes a <see cref="CgfNodes.MoveToLocationParams"/> (from the given target position + move
        /// tuning) to its relaxed-JSON string, byte-for-byte as the oracle does.
        /// </summary>
        [BlueprintCallable("Intent", DisplayName = "Build MoveToLocation Intent")]
        public static string Build(float x, float y, float speed, float arrivalRadius)
        {
            var dto = new CgfNodes.MoveToLocationParams
            {
                X = x,
                Y = y,
                Speed = speed,
                ArrivalRadius = arrivalRadius,
            };
            return JsonSerializer.Serialize(
                dto,
                Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        }
    }
}
