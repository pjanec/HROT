using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-3219</c> part 3 (civilians B-1, R-3014; docs/DESIGN_Civilians_And_Crowd_Control.md §7.3b) — the TKB type of a gas / smoke cloud
    /// entity. Bare on purpose: what the cloud is (kind, radius, life) travels per instance on <c>dtAreaEffectCloud</c>; its position is the
    /// entity's ordinary transform. Hidden from the Add Entity palette — only a burst makes one.
    /// </summary>
    public static class AreaEffectCloudTkb
    {
        public const long Type = Fdp.Toolkit.Combat.AreaEffectClouds.TkbType;   // the toolkit requests it (AreaEffectClouds.Request, CE-3228)
        public const string Name = "Area effect cloud";

        public static bool Is(long tkbType) => tkbType == Type;

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));
            var t = new TkbTemplate(Name, Type);
            t.AddDescriptor(new TkbMasterDto { CustomName = Name, HideFromPalette = true });
            tkb.Register(t);
        }
    }
}
