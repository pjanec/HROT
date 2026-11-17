using System.Text.Json.Serialization;
using Fdp.Toolkit.Replication;
using Fdp.Core;

namespace Hrot.Map.Definitions.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> MS-B1 back-out (Minefields MR-5; 📄 docs/designs/minefields/DESIGN_Minefields.md §11) — the contract of <c>BackOut</c>: a man walks his own
    /// breadcrumbs back out of the struck field, then holds. Given by his unit's <c>MineStrike</c> drill.
    /// </summary>
    [ParamsDto]
    public struct BackOutParamsJsonDto
    {
        public const string BehaviorId = BehaviorNames.BackOut;

        /// <summary>The struck field — the walk stops at his first track point outside it (none = his whole track).</summary>
        [JsonPropertyName("Field")] public EntityLink Field;
    }
}
