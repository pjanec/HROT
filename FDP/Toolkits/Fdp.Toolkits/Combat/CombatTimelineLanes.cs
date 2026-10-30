using System.Collections.Generic;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>R-4079</c> (docs/DESIGN_Director_Timeline_Editor.md §9c ②) — the combat toolkit's timeline lanes, as DATA: each is a transient-
    /// event predicate on the recorded event (R0 ②: <c>ShotLog</c> / <c>DetonationLog</c> are side stores, never recorded) plus what to keep.
    /// <list type="bullet">
    /// <item>Shot — a <see cref="WeaponFireNotification"/> that FIRED (a refusal is not a shot); who, at whom, the muzzle, the rounds;</item>
    /// <item>Hit — a <see cref="HitEvent"/>; who was hit;</item>
    /// <item>Blast — a <see cref="DetonationNotification"/>; who fired, at whom, the point, the damage.</item>
    /// </list>
    /// <para>⭐ <c>CE-4117</c> — two hidden STATE lanes (group <c>State</c>, value-change lanes: an entry when a unit's state changes, its first
    /// seen state included — the director's <c>health</c> / <c>gesture</c> cues read them):</para>
    /// <list type="bullet">
    /// <item><c>state.health</c> — <see cref="Health"/>: Subject = the unit; Value = <c>Current</c>; X = <c>Max</c> (an entry when either changes);</item>
    /// <item><c>state.gesture</c> — <see cref="Gesture"/>: Subject = the unit; Value = the <see cref="GestureKind"/> number. The component being
    ///   removed emits Value 0 = <c>None</c> (absent = None).</item>
    /// </list>
    /// </summary>
    public sealed class CombatTimelineLanes : ITimelineLaneProvider
    {
        public const string Group = "Combat";
        public static readonly TimelineLaneDescriptor Shot = new("combat.shot", "Shot", Group: Group, Priority: 3, Color: 0xFF40D0E0);
        public static readonly TimelineLaneDescriptor Hit = new("combat.hit", "Hit", Group: Group, Priority: 4, Color: 0xFF3090F0);
        public static readonly TimelineLaneDescriptor Blast = new("combat.blast", "Blast", Group: Group, Priority: 5, Color: 0xFF2040FF);

        public const string StateGroup = "State";
        public static readonly TimelineLaneDescriptor HealthState = new("state.health", "Health", Group: StateGroup, Hidden: true);
        public static readonly TimelineLaneDescriptor GestureState = new("state.gesture", "Gesture", Group: StateGroup, Hidden: true);

        private static readonly IndexedLaneDefinition[] Definitions =
        {
            new(HealthState, new ValueChangePredicateDto { ComponentType = typeof(Health) },
                new ResultProjectionDto { SourceComponent = typeof(Health), ValuePath = nameof(Health.Current), XPath = nameof(Health.Max) }),
            new(GestureState, new ValueChangePredicateDto { ComponentType = typeof(Gesture) },
                new ResultProjectionDto { SourceComponent = typeof(Gesture), ValuePath = nameof(Gesture.Kind) }),
            new(Shot,
                new TransientEventPredicateDto { EventType = typeof(WeaponFireNotification), AnyOccurrence = false,
                    PropertyPath = nameof(WeaponFireNotification.Refusal), Operator = SearchOperator.Equals, TargetValue = nameof(FireRefusal.None) },
                new ResultProjectionDto { SubjectPath = nameof(WeaponFireNotification.Shooter), OtherPath = nameof(WeaponFireNotification.Target),
                    XPath = "Origin.X", YPath = "Origin.Y", ValuePath = nameof(WeaponFireNotification.Quantity) }),
            new(Hit,
                new TransientEventPredicateDto { EventType = typeof(HitEvent), AnyOccurrence = true },
                new ResultProjectionDto { SubjectPath = nameof(HitEvent.HitEntity) }),
            new(Blast,
                new TransientEventPredicateDto { EventType = typeof(DetonationNotification), AnyOccurrence = true },
                new ResultProjectionDto { SubjectPath = nameof(DetonationNotification.Shooter), OtherPath = nameof(DetonationNotification.Target),
                    XPath = nameof(DetonationNotification.HitX), YPath = nameof(DetonationNotification.HitY), ValuePath = nameof(DetonationNotification.Damage) }),
        };

        public IReadOnlyList<TimelineLaneDescriptor> Lanes { get; } = new[] { Shot, Hit, Blast, HealthState, GestureState };
        public IReadOnlyList<IndexedLaneDefinition> Indexed => Definitions;
        public bool Applies(TimelineLaneContext context) => context.Indexing;   // ⭐ CE-3330 — a recording's index or the live one
    }
}
