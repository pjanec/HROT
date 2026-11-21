using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using StructEdit.Core;

namespace Fdp.Presentation.Editing
{
    /// <summary>Where a field's binding is written (MA-2) — decided by the DRAWER from the node, never by the host.</summary>
    public enum BindHome
    {
        /// <summary>Not bindable.</summary>
        None,
        /// <summary>① a reference leaf (<c>EntityLink</c> / <c>PointLink</c> / <c>AreaLink</c>): the value's own Param form (<c>"^name"</c>).</summary>
        RefParamForm,
        /// <summary>② a property of a <see cref="BindableNode"/>: that node's <see cref="BindableNode.Bind"/> map.</summary>
        BindableNode,
        /// <summary>③ any other scalar / enum field: the entity's <c>ParamBindings</c>, through <see cref="IParamBindContext.Bind"/>.</summary>
        ParamBindings,
    }

    /// <summary>What the drawer shows for one field: whether it is bindable, where its binding lives, the param it is bound to, why that is wrong, and which params fit.</summary>
    public sealed record BindView(bool Bindable, BindHome Home, string? BoundParam, string? Problem, IReadOnlyList<ParamInScope> Fitting)
    {
        /// <summary>The unbound-and-bindable row: a faint button at the right edge.</summary>
        public bool ShowsButton => Bindable && BoundParam is null;
        /// <summary>The bound row: the chip in front of the value.</summary>
        public bool ShowsChip => BoundParam is not null;
        /// <summary>The chip is red (its tooltip is <see cref="Problem"/>).</summary>
        public bool ChipIsRed => BoundParam is not null && Problem is not null;

        public static readonly BindView NotBindable = new(false, BindHome.None, null, null, Array.Empty<ParamInScope>());
    }

    /// <summary>
    /// ⭐⭐ CE-4210 P2 (MA-1, MA-2) — the PURE half of the bind affordance: WHAT a field offers and WHERE a binding is written, decided from the node's type and its container, so the
    /// rails call it without ImGui. <see cref="ComponentEditDrawer"/> only draws the answer.
    /// <para>⛔ Never a key inside a params JSON; never a second binder (SR-164): this WRITES the homes, the binder APPLIES them.</para>
    /// </summary>
    public static class ParamBindModel
    {
        /// <summary>True for the three reference types whose value carries the Param form (<c>EntityRef</c> / <c>WorldPoint</c> are not links and are not bindable here).</summary>
        public static bool IsReferenceLink(Type t) => t == typeof(EntityLink) || t == typeof(PointLink) || t == typeof(AreaLink);

        /// <summary>The home of a field of <paramref name="fieldType"/> whose nearest container's value is <paramref name="containerValue"/>.</summary>
        public static BindHome HomeOf(Type fieldType, object? containerValue)
            => IsReferenceLink(fieldType) ? BindHome.RefParamForm
             : containerValue is BindableNode ? BindHome.BindableNode
             : BindHome.ParamBindings;

        /// <summary>A node's path as the binding paths spell it: <c>"$.Speed"</c> → <c>"Speed"</c>.</summary>
        public static string PathOf(string jsonPath)
            => jsonPath == "$" ? "" : jsonPath.StartsWith("$.", StringComparison.Ordinal) ? jsonPath[2..] : jsonPath;

        /// <summary>The param a reference link is bound to (its Param form), or null.</summary>
        public static string? BoundParamOfLink(object? value) => value switch
        {
            EntityLink e when e.Spec.Form == Fdp.Core.RefForm.Param => e.Spec.ParamName,
            PointLink p when p.Spec.Form == Fdp.Core.RefForm.Param => p.Spec.ParamName,
            AreaLink a when a.Spec.Form == Fdp.Core.RefForm.Param => a.Spec.ParamName,
            _ => null,
        };

        /// <summary>
        /// What a field of <paramref name="fieldType"/> named <paramref name="propertyName"/> (path <paramref name="jsonPath"/>, current <paramref name="value"/>) offers.
        /// Bindable = at least one param in scope fits the type, or it is already bound; no context ⇒ not bindable.
        /// </summary>
        public static BindView View(Type fieldType, object? value, string jsonPath, string propertyName, object? containerValue, IParamBindContext? ctx)
        {
            if (ctx is null) return BindView.NotBindable;
            var home = HomeOf(fieldType, containerValue);
            string path = PathOf(jsonPath);
            string? bound = home switch
            {
                BindHome.RefParamForm => BoundParamOfLink(value),
                BindHome.BindableNode => containerValue is BindableNode n && n.Bind is { } b && b.TryGetValue(propertyName, out var pn) ? pn : null,
                _ => ctx.BoundTo(path),
            };
            if (bound is null && home == BindHome.ParamBindings && path.IndexOf('[') >= 0) return BindView.NotBindable;   // SR-161: no index in a path
            var fitting = ctx.Params.Where(p => ParamTypes.Fits(p.TypeId, fieldType)).ToArray();
            if (fitting.Length == 0 && bound is null) return BindView.NotBindable;
            return new BindView(true, home, bound, bound is null ? null : ProblemOf(bound, fieldType, path, home, ctx), fitting);
        }

        /// <summary>Why a binding to <paramref name="param"/> is wrong: the validator's reason (third home), else the name is not declared, else the type does not fit; null when fine.</summary>
        public static string? ProblemOf(string param, Type fieldType, string path, BindHome home, IParamBindContext ctx)
        {
            if (home == BindHome.ParamBindings && ctx.Problem(path) is { } validator) return validator;
            var declared = ctx.Params.FirstOrDefault(p => string.Equals(p.Name, param, StringComparison.Ordinal));
            if (declared.Name is null) return $"No parameter '{param}' is declared here.";
            return ParamTypes.Fits(declared.TypeId, fieldType) ? null : $"'{param}' is a {declared.TypeId}; this field is a {fieldType.Name}.";
        }

        /// <summary>
        /// Binds the field of <paramref name="node"/> to <paramref name="param"/> in its home. Returns true when the write changed the STRUCTURE the drawer shows (a
        /// <see cref="BindableNode.Bind"/> map — the caller then <c>MarkStructuralChange</c>s); a link's Param form is written through the node's binding, a scalar through
        /// <paramref name="ctx"/>.
        /// </summary>
        public static bool Bind(EditNode node, object? containerValue, IParamBindContext ctx, string param)
        {
            switch (HomeOf(node.ClrType, containerValue))
            {
                case BindHome.RefParamForm:
                    node.Binding?.SetBoxed(LinkWithParam(node.Binding.GetBoxed(), param));
                    return false;
                case BindHome.BindableNode:
                    var bn = (BindableNode)containerValue!;
                    (bn.Bind ??= new Dictionary<string, string>())[node.Name] = param;
                    return true;
                default:
                    ctx.Bind(PathOf(node.JsonPath), param);
                    return false;
            }
        }

        /// <summary>Removes the field's binding from its home (the literal stays); see <see cref="Bind"/> for the return.</summary>
        public static bool Unbind(EditNode node, object? containerValue, IParamBindContext ctx)
        {
            switch (HomeOf(node.ClrType, containerValue))
            {
                case BindHome.RefParamForm:
                    node.Binding?.SetBoxed(LinkWithoutParam(node.Binding.GetBoxed()));
                    return false;
                case BindHome.BindableNode:
                    var bn = (BindableNode)containerValue!;
                    if (bn.Bind is { } map) { map.Remove(node.Name); if (map.Count == 0) bn.Bind = null; }
                    return true;
                default:
                    ctx.Unbind(PathOf(node.JsonPath));
                    return false;
            }
        }

        /// <summary>A reference link in its Param form (<c>"^name"</c>), the live answer kept.</summary>
        public static object LinkWithParam(object? link, string param) => link switch
        {
            EntityLink e => e.BindToParam(param),
            PointLink p => p.BindToParam(param),
            AreaLink a => a.BindToParam(param),
            _ => throw new ArgumentException("not a reference link", nameof(link)),
        };

        /// <summary>A reference link with its binding removed (the literal stays).</summary>
        public static object LinkWithoutParam(object? link) => link switch
        {
            EntityLink e => e.UnbindParam(),
            PointLink p => p.UnbindParam(),
            AreaLink a => a.UnbindParam(),
            _ => throw new ArgumentException("not a reference link", nameof(link)),
        };

        /// <summary>The name a new parameter of a field gets: the field's name (a path's last segment, lower camel) made unique among <paramref name="taken"/>.</summary>
        public static string SuggestParamName(string fieldName, IEnumerable<string> taken)
        {
            string stem = string.IsNullOrEmpty(fieldName) ? "param" : char.ToLowerInvariant(fieldName[0]) + fieldName[1..];
            var set = new HashSet<string>(taken, StringComparer.Ordinal);
            if (!set.Contains(stem)) return stem;
            for (int i = 2; ; i++) if (!set.Contains(stem + i)) return stem + i;
        }
    }
}
