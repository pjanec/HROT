using System;
using System.Numerics;
using Fdp.Presentation.Panels.ReplayBrowser.Drawers;

namespace Fdp.Presentation.Editing.Types;

/// <summary>
/// ⭐ S3 (CE-3378) — the engine's standard types, registered ONCE into <see cref="TypeCatalogue.Standard"/>: the list <c>ComponentEditServices.CreateBuilder</c> carried (CE-1023 F1 and
/// after) PLUS the cells only two hosts passed — Quaternion / WorldRotation as yaw · pitch · roll, Guid, and the filtered <c>Type</c> combo — so every host draws them.
/// <para>The reference leaves (EntityRef, EntityLink, PointLink, WorldPoint, AreaLink, Bearing, RefKey) keep their ONE drawer (<c>ReferenceLeafDrawers</c>), which the table calls by type;
/// here they register their Shape (a leaf).</para>
/// </summary>
internal static class StandardTypes
{
    internal static TypeCatalogue Build(TypeCatalogue c)
    {
        c.Add(new FieldTypeEditor(typeof(Fdp.Core.FixedString32), new FixedString32FieldEditor(), null));
        c.Add(new FieldTypeEditor(typeof(Fdp.Core.FixedString64), new FixedString64FieldEditor(), null));
        c.Add(new FieldTypeEditor(typeof(Fdp.Core.FixedString128), new FixedString128FieldEditor(), null));
        c.Add(new FieldTypeEditor(typeof(Quaternion), new QuaternionEulerFieldEditor(), new QuaternionEulerFieldDrawer()));
        c.Add(new FieldTypeEditor(typeof(Fdp.Core.WorldRotation), new QuaternionEulerFieldEditor(typeof(Fdp.Core.WorldRotation)), new QuaternionEulerFieldDrawer(typeof(Fdp.Core.WorldRotation))));
        Leaf(c, typeof(Fdp.Toolkit.Replication.EntityRef));
        Leaf(c, typeof(Fdp.Toolkit.Replication.EntityLink));
        Leaf(c, typeof(Fdp.Toolkit.Replication.PointLink));
        Leaf(c, typeof(Fdp.Core.WorldPoint));
        Leaf(c, typeof(Fdp.Core.Bearing));
        Leaf(c, typeof(Fdp.Toolkit.Scenario.RefKey));
        Leaf(c, typeof(Fdp.Toolkit.Replication.AreaLink));
        Leaf(c, typeof(Fdp.Toolkit.Utility.UtilityDecisionRef));
        c.Add(new FieldTypeEditor(typeof(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto), new PredicateValueFieldEditor(), null));
        c.Add(new FieldTypeEditor(typeof(Type), new TypeFieldEditor(), new FilteredTypeComboFieldDrawer(TypeComboMode.Auto)));
        c.Add(new FieldTypeEditor(typeof(System.Collections.Generic.List<Fdp.Toolkit.ScenarioRules.Actions.ActionStep>), new ActionListFieldEditor(), null));
        c.Add(new FieldTypeEditor(typeof(Guid), new StructEdit.Reflection.Editors.GuidFieldEditor(), new GuidFieldDrawer()));
        return c;
    }

    private static void Leaf(TypeCatalogue c, Type t) => c.Add(new FieldTypeEditor(t, new PickableLeafFieldEditor(t), null));
}
