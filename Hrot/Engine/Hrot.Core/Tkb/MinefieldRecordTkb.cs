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

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            Add(tkb, KnowledgeName, KnowledgeType);
            Add(tkb, StrikesName, StrikesType);
            Add(tkb, LanesName, LanesType);   // ⭐ MS-T4
        }

        private static void Add(ITkbDatabase tkb, string name, long type)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(new TkbMasterDto { CustomName = name, HideFromPalette = true });
            tkb.Register(t);
        }
    }
}
