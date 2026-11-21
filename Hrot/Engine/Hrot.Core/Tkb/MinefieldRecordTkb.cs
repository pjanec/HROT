using System;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-4624</c> (minefields §3a) — the TKB types of the minefield's two RECORD entities: the per-field strikes (<see cref="Fdp.Toolkit.Terrain.MinefieldStrikes"/>, 7207) and the
    /// per-side knowledge (<see cref="Fdp.Toolkit.Terrain.MinefieldKnowledge"/>, 7206). Bare, like <see cref="GroundDamageTkb"/>'s: the data travels on the components; hidden from the
    /// Add Entity palette — only the record writers make them.
    /// </summary>
    public static class MinefieldRecordTkb
    {
        public const long KnowledgeType = Fdp.Toolkit.Terrain.MinefieldKnowledge.TkbType, StrikesType = Fdp.Toolkit.Terrain.MinefieldStrikes.TkbType;
        public const string KnowledgeName = "Minefield knowledge", StrikesName = "Minefield strikes", LanesName = "Minefield lanes";
        public const long LanesType = Fdp.Toolkit.Terrain.MinefieldLanes.TkbType;
        /// <summary>⭐ CE-2468 (§9b) — the second place-knowledge kind's record (7211): bare like the others, made only by its writer.</summary>
        public const long UnderFireType = Fdp.Toolkit.Terrain.UnderFireKnowledge.TkbType;
        public const string UnderFireName = "Under-fire knowledge";

        /// <summary>Is <paramref name="tkbType"/> one of the place-knowledge records (drawn by nothing: the map shows what a side knows, never the record)?</summary>
        public static bool IsRecord(long tkbType) => tkbType == KnowledgeType || tkbType == StrikesType || tkbType == LanesType || tkbType == UnderFireType;

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            Add(tkb, KnowledgeName, KnowledgeType);
            Add(tkb, StrikesName, StrikesType);
            Add(tkb, LanesName, LanesType);   // ⭐ MS-T4
            Add(tkb, UnderFireName, UnderFireType);   // ⭐ CE-2468 UF-2
        }

        private static void Add(ITkbDatabase tkb, string name, long type)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(new TkbMasterDto { CustomName = name, HideFromPalette = true });
            tkb.Register(t);
        }
    }
}
