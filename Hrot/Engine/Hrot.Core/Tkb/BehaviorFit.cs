using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐ <c>CE-4350</c> (docs/DESIGN_Orders_Params_Entity_Classes.md §14) — THE one answer to "does this behaviour FIT this entity": the
    /// mission editor's "+ Add task", ExCon's list (<c>BehaviorCatalog</c>) and the HTTP list all ask it, so a tank is never offered an infantry drill
    /// and a squad never a vehicle one. It ends at the SAME class table the orders use (<see cref="OrderResolver"/>).
    /// <para>A behaviour's classes, first match wins: ① <see cref="BehaviorAttribute.Classes"/>; ② an order behaviour's name prefix
    /// (<c>PersonMove</c> → <c>Person</c>, the split <see cref="OrderResolver.ClassOfBehaviour"/> is the inverse of); ③ its
    /// <see cref="BehaviorAttribute.Categories"/> through <see cref="CategoryClasses"/>. A behaviour with NONE of the three — an authored BTree / HSM /
    /// blueprint, or one the registry does not declare — fits every entity (§14: authored assets cannot declare classes yet, and hiding them would
    /// take away what the user wrote).</para>
    /// </summary>
    public static class BehaviorFit
    {
        /// <summary>⭐ the ONE category → class translation (the categories are the pre-class-table filter, <see cref="BehaviorCategory"/>).
        /// 📐 Measured against the built-in TKB (the golden): military land vehicles are <c>Armor</c> (tank 1.1.*.1, IFV/APC *.2), <c>WheeledUtility</c>
        /// (HMMWV, the technicals *.6) and <c>SelfPropelledArtillery</c> (*.4) — NOT <c>Car</c> (*.81, the civilian traffic), which is why no single
        /// class (<c>LandPlatform</c> holds the cars) does — plus <c>AirPlatform</c>: the category also meant "military aircraft" (EjectAndEvadeBehaviors.cs:145,
        /// "the aircraft behaviours' category"), and <c>MoveToLocation</c> / <c>Idle</c> (AllMilitary) were offered to a helicopter before. Security forces (3.1.*.3) run the infantry drills.</summary>
        public static IReadOnlyList<string> CategoryClasses(BehaviorCategory category)
        {
            var list = new List<string>(8);
            void Add(params string[] names) { foreach (var n in names) if (!list.Contains(n)) list.Add(n); }
            if ((category & BehaviorCategory.MilitaryApc) != 0) Add("Armor", "WheeledUtility", "SelfPropelledArtillery", "AirPlatform");
            if ((category & (BehaviorCategory.Infantry | BehaviorCategory.Insurgent)) != 0) Add("Infantry", "SecurityForce");
            if ((category & BehaviorCategory.Civilian) != 0) Add("Civilian");
            if ((category & BehaviorCategory.Commander) != 0) Add("Unit");
            if ((category & BehaviorCategory.Objective) != 0) Add(ObjectiveClass);
            if ((category & BehaviorCategory.Trigger) != 0) Add(TriggerClass);
            return list;
        }

        /// <summary>⭐ An Objective / Trigger entity is a brain with NO body and NO DIS type (TKB 8860 / 8861) — no class of the table can name it, so
        /// these two pseudo-classes match on the entity's TKB type id instead (<see cref="TkbTypeOfPseudoClass"/>).</summary>
        public const string ObjectiveClass = "Objective";
        public const string TriggerClass = "Trigger";

        /// <summary>The TKB type a pseudo-class names, or 0 when <paramref name="name"/> is an ordinary class.</summary>
        public static long TkbTypeOfPseudoClass(string name)
            => name == ObjectiveClass ? TkbEntityTypes.Objective : name == TriggerClass ? TkbEntityTypes.Trigger : 0;

        private static readonly ConditionalWeakTable<ParamsDtoRegistry, ConcurrentDictionary<string, string[]>> s_default = new();

        /// <summary>
        /// The classes <paramref name="behaviour"/> suits (see the type's rule order); EMPTY ⇒ it names none, an authored asset ⇒ fits everyone.
        /// Cached per registry for the shipped table (the "+ Add task" popup asks it for every behaviour while it is open).
        /// </summary>
        public static IReadOnlyList<string> ClassesOf(ParamsDtoRegistry registry, EntityClassTable table, string behaviour)
        {
            if (!ReferenceEquals(table, EntityClassTable.Default)) return Compute(registry, table, behaviour);
            var cache = s_default.GetValue(registry, _ => new ConcurrentDictionary<string, string[]>(StringComparer.Ordinal));
            if (cache.TryGetValue(behaviour, out var hit)) return hit;
            return cache.GetOrAdd(behaviour, Compute(registry, table, behaviour));
        }

        private static string[] Compute(ParamsDtoRegistry registry, EntityClassTable table, string behaviour)
        {
            if (!registry.TryGetBehavior(behaviour, out var decl)) return Array.Empty<string>();
            if (decl.Classes.Count > 0) { var a = new string[decl.Classes.Count]; for (int i = 0; i < a.Length; i++) a[i] = decl.Classes[i]; return a; }
            if (registry.TryGetDto(decl.ParamsType, out var dto) && dto.OrderName is { } order
                && OrderResolver.ClassOfBehaviour(table, behaviour, order) is { } prefix)
                return new[] { prefix };
            if (decl.Categories != BehaviorCategory.None)
            {
                var c = CategoryClasses(decl.Categories);
                var a = new string[c.Count]; for (int i = 0; i < a.Length; i++) a[i] = c[i];
                return a;
            }
            return Array.Empty<string>();
        }

        /// <summary>
        /// True when <paramref name="behaviour"/> fits the entity: one of its classes is in the class chain of <paramref name="dis"/> (or names
        /// <paramref name="tkbType"/>, the pseudo-classes) AND every <see cref="BehaviorAttribute.Requires"/> component is carried (the ONE AND,
        /// <see cref="ComponentRequirements"/>). No classes at all ⇒ fits (an authored asset). <paramref name="probe"/> null ⇒ a declared
        /// requirement cannot be asked, so it is not met.
        /// </summary>
        public static bool Fits(ParamsDtoRegistry registry, EntityClassTable table, DISEntityType dis, long tkbType, string behaviour,
            Func<Entity, Type, bool>? probe, Entity entity)
        {
            var classes = ClassesOf(registry, table, behaviour);
            if (classes.Count > 0)
            {
                bool any = false;
                for (int i = 0; i < classes.Count && !any; i++)
                {
                    long pseudo = TkbTypeOfPseudoClass(classes[i]);
                    any = pseudo != 0 ? pseudo == tkbType : table.ChainContains(dis, classes[i]);
                }
                if (!any) return false;
            }
            if (registry.TryGetBehavior(behaviour, out var decl) && decl.Requires.Count > 0)
                return probe is not null && ComponentRequirements.Met(probe, entity, decl.Requires);
            return true;
        }
    }
}
