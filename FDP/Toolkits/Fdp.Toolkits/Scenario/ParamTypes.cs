using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>Which of the two spellings of a param's type a list uses.</summary>
    public enum ParamTypeSpelling
    {
        /// <summary>The short names a scenario / macro header's <c>Params</c> use (<c>"float"</c>, <c>"AreaLink"</c>, <c>"ForceId"</c>).</summary>
        ShortName,
        /// <summary>The CLR names a <c>.params.json</c> uses (<c>"System.Single"</c>, <c>"System.Numerics.Vector3"</c>).</summary>
        Clr,
    }

    /// <summary>
    /// ⭐ CE-4210 MA-3 (docs/DESIGN_Macro_Authoring_Editor.md §3) — THE table from a param's <c>TypeId</c> to a CLR type, and back. Before it, <c>ScenarioParamDecl.TypeId</c>
    /// was a free string with no meaning outside the binder (which converts to the TARGET field's type), so the bind menu could not filter params by type and an args form
    /// could not draw a typed row.
    /// <para>⭐ The three declaration lists spell types DIFFERENTLY — a scenario's / macro's header params use short names (<c>"float"</c>, <c>"AreaLink"</c>), a
    /// <c>.params.json</c> the CLR names (<c>"System.Single"</c>) — so the table READS BOTH and <see cref="IdOf"/> WRITES the list's own (no file changes).</para>
    /// <para>⭐ Enums: ONE rule — an enum is a param type when it is REGISTERED here (<see cref="RegisterEnum"/>), found by its short name (<c>ForceId</c>) or its full CLR
    /// name. <see cref="ForceId"/> (the one the shipped macros spell) is registered; a host or feature that declares another enum param registers it once. ⛔ Not a scan of every
    /// loaded assembly: two enums of one name would resolve by load order.</para>
    /// <para>An unknown id resolves to nothing and fits nothing; nothing here throws.</para>
    /// </summary>
    public static class ParamTypes
    {
        private static readonly (string Short, string Clr, Type Type)[] Plain =
        {
            ("EntityLink",    typeof(EntityLink).FullName!,    typeof(EntityLink)),
            ("PointLink",     typeof(PointLink).FullName!,     typeof(PointLink)),
            ("AreaLink",      typeof(AreaLink).FullName!,      typeof(AreaLink)),
            ("Bearing",       typeof(Bearing).FullName!,       typeof(Bearing)),
            ("WorldRotation", typeof(WorldRotation).FullName!, typeof(WorldRotation)),
            ("float",         typeof(float).FullName!,         typeof(float)),
            ("int",           typeof(int).FullName!,           typeof(int)),
            ("bool",          typeof(bool).FullName!,          typeof(bool)),
            ("string",        typeof(string).FullName!,        typeof(string)),
            ("double",        typeof(double).FullName!,        typeof(double)),
            ("Vector3",       typeof(Vector3).FullName!,       typeof(Vector3)),   // a .params.json field (T41Params.Home); no short name was ever written for it
        };

        private static readonly object Gate = new();
        private static readonly List<Type> Enums = new() { typeof(ForceId) };

        /// <summary>Makes <paramref name="enumType"/> a param type, spelled by its name (short) or full name (CLR). Idempotent; a non-enum is ignored.</summary>
        public static void RegisterEnum(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum) return;
            lock (Gate) if (!Enums.Contains(enumType)) Enums.Add(enumType);
        }

        /// <summary>⭐ CE-4210 MA-5 — every param type, by short id (the plain ones in declaration order, then the registered enums): what a params editor offers as a type choice.</summary>
        public static IReadOnlyList<(string Id, Type Type)> All()
        {
            var list = new List<(string, Type)>();
            foreach (var (s, _, t) in Plain) list.Add((s, t));
            lock (Gate) foreach (var e in Enums) list.Add((e.Name, e));
            return list;
        }

        /// <summary>The CLR type a <paramref name="typeId"/> names, in either spelling (case-insensitive); false for null, empty or unknown.</summary>
        public static bool TryResolve(string? typeId, out Type type)
        {
            type = null!;
            if (string.IsNullOrWhiteSpace(typeId)) return false;
            var id = typeId.Trim();
            foreach (var (s, c, t) in Plain)
                if (string.Equals(id, s, StringComparison.OrdinalIgnoreCase) || string.Equals(id, c, StringComparison.OrdinalIgnoreCase)) { type = t; return true; }
            lock (Gate)
                foreach (var e in Enums)
                    if (string.Equals(id, e.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(id, e.FullName, StringComparison.OrdinalIgnoreCase)) { type = e; return true; }
            return false;
        }

        /// <summary>The id of <paramref name="type"/> in the list's own <paramref name="spelling"/>; null when the type is not a param type.</summary>
        public static string? IdOf(Type? type, ParamTypeSpelling spelling = ParamTypeSpelling.ShortName)
        {
            if (type == null) return null;
            foreach (var (s, c, t) in Plain)
                if (t == type) return spelling == ParamTypeSpelling.ShortName ? s : c;
            lock (Gate)
                if (Enums.Contains(type)) return spelling == ParamTypeSpelling.ShortName ? type.Name : type.FullName;
            return null;
        }

        /// <summary>True when a param declared as <paramref name="typeId"/> can feed a field of type <paramref name="target"/> (a <see cref="Nullable{T}"/> of it too): the SAME type.
        /// Nothing is converted — a bind menu offers only the params that fit exactly. False for an unknown id.</summary>
        public static bool Fits(string? typeId, Type? target)
        {
            if (target == null || !TryResolve(typeId, out var declared)) return false;
            return declared == (Nullable.GetUnderlyingType(target) ?? target);
        }
    }
}
