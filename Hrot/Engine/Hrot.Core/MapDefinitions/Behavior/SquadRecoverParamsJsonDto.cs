using System.Numerics;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 3c (📄 docs/designs/air-attack/DESIGN_Air_Operations.md §9j) — the authored contract of the commander's
    /// library <c>SquadRecover</c>: the squad covers while its medic takes <see cref="Casualty"/> — a man who is down, of this squad or
    /// not (a downed pilot) — and carries him to <see cref="To"/>, where he KEEPS him (for <c>SquadEmbark</c> to load) unless
    /// <see cref="PutDown"/>.
    /// </summary>
    [ParamsDto]
    public struct SquadRecoverParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.SquadRecover;

        /// <summary>The man to bring in.</summary>
        [JsonPropertyName("casualty")]
        public EntityRef Casualty;

        /// <summary>Where to bring him (sim metres, Z up); zero ⇒ the nearest cover around him, as <c>CasualtyCare</c> does.</summary>
        [JsonPropertyName("to")]
        public Vector3 To;

        /// <summary>Put him down at the point instead of keeping him on the carrier.</summary>
        [JsonPropertyName("putDown")]
        public bool PutDown;

        /// <summary>Walking speed (m/s); 0 ⇒ 3 m/s.</summary>
        [JsonPropertyName("speed")]
        public float Speed;
    }
}
