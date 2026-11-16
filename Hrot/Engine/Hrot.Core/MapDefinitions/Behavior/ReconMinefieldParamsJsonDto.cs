using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-B2 (Minefields MF-13 ②③ + MR-7) — the contract of <c>ReconMinefield</c>: an engineer or scout walks at detector pace from where he
    /// stands toward <see cref="At"/> (the suspected area — usually a strike), and from the first mined cell traces the field's edge (left-hand rule on the
    /// knowledge grid's 4-neighbourhood, only ever stepping into cells he has swept clear), reporting every swept cell; a closed trace — or
    /// <see cref="Mark"/> — marks the field for his side. A zero field keeps its default.
    /// </summary>
    [ParamsDto]
    public struct ReconMinefieldParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.ReconMinefield;

        /// <summary>The field: a GUID (the scenario's), <c>"@Key"</c> or a bare network id.</summary>
        [JsonPropertyName("Field")] public EntityLink Field;

        /// <summary>The suspected point (world m) he works toward.</summary>
        [JsonPropertyName("At")]
        public Vector3 At;

        /// <summary>Mark the field when the walk ends, even unclosed (an order to mark).</summary>
        [JsonPropertyName("Mark")]
        public bool Mark;

        /// <summary>The most cells he sweeps; 0 = 200.</summary>
        [JsonPropertyName("MaxCells")]
        public int MaxCells;

        /// <summary>Detector pace (m/s); 0 = 0.2.</summary>
        [JsonPropertyName("Pace")]
        public float Pace;
    }
}
