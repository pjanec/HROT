using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-7023</c> (City Battle §6, §11 C-1b) — the authored contract of a company's <c>CompanyAttack</c>: take
    /// <see cref="Objective1"/> … <see cref="Objective4"/> IN ORDER (the scripted top's phase lines; a zero objective ends the list), each
    /// with a base of fire platoon, an assault platoon (<c>PlatoonAttack</c> on the objective's area) and a reserve held behind. A zero field
    /// keeps its default. ⭐ Four fixed objectives, not a list (lane default R-7000): the intent payload is a flat struct, as every platoon play's.
    /// </summary>
    [ParamsDto]
    public struct CompanyAttackParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.CompanyAttack;

        /// <summary>The first objective (sim metres) — required.</summary>
        [JsonPropertyName("Objective1")]
        public PointLink Objective1;

        /// <summary>The next objectives, in order; zero = none.</summary>
        [JsonPropertyName("Objective2")]
        public PointLink Objective2;
        [JsonPropertyName("Objective3")]
        public PointLink Objective3;
        [JsonPropertyName("Objective4")]
        public PointLink Objective4;

        /// <summary>Each objective's area radius, handed to the assault platoon's <c>PlatoonAttack</c> (m); 0 = 80.</summary>
        [JsonPropertyName("Radius")]
        public float Radius;

        /// <summary>The platoons' asked speed (m/s); 0 = their own.</summary>
        [JsonPropertyName("Speed")]
        public float Speed;

        /// <summary>⭐ <c>CE-7005</c> (tempo, City Battle §3a) — the time each objective is given (s); at half of it the company asks for smoke
        /// and fires, at all of it it commits its reserve. 0 = 480.</summary>
        [JsonPropertyName("DeadlineSeconds")]
        public float DeadlineSeconds;
        /// <summary>⭐ <c>CE-8307</c> (R-8392; the neutral <c>AttackOrder</c>, R-8300) — the order's place: the first objective when
        /// <see cref="Objective1"/> is zero.</summary>
        [JsonPropertyName("Position")]
        public PointLink Position;

        /// <summary>⭐ <c>CE-8307</c> — the order's entity: the first objective (its place when the play starts) when neither objective nor position is given.</summary>
        [JsonPropertyName("Target")]
        public EntityLink Target;

        /// <summary>⭐ <c>CE-8307</c> — the order's area radius: <see cref="Radius"/> when that is zero.</summary>
        [JsonPropertyName("SearchRadius")]
        public float SearchRadius;

        /// <summary>⭐ <c>CE-8307</c> — support by fire (the neutral order's field). ⚠ The company's attack has none: the reconciler never re-resolves a
        /// hold-back attack to this play (F-SOL-4).</summary>
        [JsonPropertyName("HoldBack")]
        public bool HoldBack;
    }
}
