using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.ReplayBrowser.Search.Conditions;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules;
using Xunit;

namespace Fdp.Toolkits.Tests.ReplayBrowser.Search
{
    /// <summary>
    /// ⭐ U-14 / C2 (docs/UX/DESIGN_Condition_Editor.md §2, §6 C2) — the friendly rows are a PROJECTION of the one language: every catalogue row survives
    /// <c>FromDto(ToDto(x)) = x</c> under every WHO; an unknown DTO is ONE Custom row and comes back byte for byte; the ONE sentence renderer reads it;
    /// a per-frame summary allocates nothing. 🔴 Red-proof (compiles): <c>RowProjection.ToDto</c> wrapping All without the negation ⇒ the round trip fails.
    /// </summary>
    public sealed class ConditionProjectionTests
    {
        private static readonly EntityLink Bridge = new(new EntityRef(10), default);
        private static readonly EntityLink Alpha = new(new EntityRef(20), default);

        private static IEnumerable<FactValues> ValuesFor(string fact) => fact switch
        {
            "inside" => new[] { new FactValues(Area: AreaLink.Circle(new WorldPoint(5, 6, 0), 40)), new FactValues(Area: AreaLink.Rectangle(new WorldPoint(1, 2, 0), 10, 20, new Bearing(45))) },
            "strength" or "ammunition" => new[] { new FactValues(Number: 0.5f) },
            "under-fire" => new[] { new FactValues(Number: 0.5f) },
            "side" => new[] { new FactValues(Side: ForceId.Hostile) },
            "objective-state" => new[] { new FactValues(State: (int)ObjectiveState.Achieved) },
            "fired" => new[] { new FactValues(Number: 3) },
            "time" => new[] { new FactValues(Number: 60, Number2: double.MaxValue), new FactValues(Number: 0, Number2: 150), new FactValues(Number: 60, Number2: 150) },
            _ => new[] { default(FactValues) },
        };

        private static IEnumerable<Subject> SubjectsFor(FactEntry e)
        {
            if (e.WorldOnly) { yield return Subject.World; yield break; }
            yield return Subject.Thing(Bridge);
            yield return Subject.OfSide(SubjectKind.Any, ForceId.Hostile);
            yield return Subject.OfSide(SubjectKind.AtLeast, ForceId.Hostile, 3);
            yield return Subject.OfUnit(SubjectKind.All, Alpha);
            yield return Subject.OfUnit(SubjectKind.None, Alpha);
        }

        [Fact]
        public void C2_EveryCatalogueRow_RoundTrips_UnderEveryWho()
        {
            int n = 0;
            foreach (var e in FactCatalogue.Entries)
                foreach (var who in SubjectsFor(e))
                    foreach (var v in ValuesFor(e.Id))
                        foreach (bool neg in new[] { false, true })
                        {
                            if (neg && (!e.Negatable || who.Kind == SubjectKind.None)) continue;   // "none of S is not X" reads back as "all of S are X" (same DTO)
                            var row = new ConditionRow(who, e.Id, neg, v);
                            var (rows, op) = RowProjection.FromDto(RowProjection.ToDto(new[] { row }));
                            Assert.Equal(LogicalOperator.And, op);
                            Assert.Single(rows);
                            Assert.Equal(row, rows[0]);
                            n++;
                        }
            Assert.True(n > 60, $"the sweep covered {n} rows");
        }

        [Fact]
        public void C2_SeveralRows_UnderAnyOperator_RoundTrip()
        {
            var rows = new List<ConditionRow>
            {
                new(Subject.Thing(Bridge), "objective-state", Values: new FactValues(State: (int)ObjectiveState.Achieved)),
                new(Subject.OfSide(SubjectKind.AtLeast, ForceId.Hostile, 3), "in-the-fight", Negated: true),
                new(Subject.World, "time", Values: new FactValues(Number: 60)),
            };
            foreach (var op in new[] { LogicalOperator.And, LogicalOperator.Or, LogicalOperator.None })
            {
                var (back, backOp) = RowProjection.FromDto(RowProjection.ToDto(rows, op));
                Assert.Equal(op, backOp);
                Assert.Equal(rows, back);
            }
        }

        private static string Json(SearchPredicateDto d) => JsonSerializer.Serialize(d, typeof(SearchPredicateDto));

        [Fact]
        public void C2_AnUnknownShape_IsOneCustomRow_AndComesBackByteForByte()
        {
            var bound = new OnEntityPredicateDto { Ref = Bridge, Predicate = new InTheFightPredicateDto(), Bind = new Dictionary<string, string> { ["Ref"] = "target" } };
            var nested = new CompoundPredicateDto { Operator = LogicalOperator.Or, Conditions = { new CompoundPredicateDto { Conditions = { new InTheFightPredicateDto() } } } };
            var debug = new TraceBufferScanPredicateDto { OpCode = 7, MatchIndexField = true, IndexField = 3 };
            var selfFact = new InTheFightPredicateDto();   // a unit fact with no WHO: the rule asking about itself
            foreach (SearchPredicateDto dto in new SearchPredicateDto[] { bound, debug, selfFact })
            {
                var (rows, _) = RowProjection.FromDto(dto);
                Assert.Single(rows);
                Assert.True(rows[0].IsCustom);
                Assert.Equal(Json(dto), Json(RowProjection.ToDto(rows)));
            }
            var (nRows, nOp) = RowProjection.FromDto(nested);   // the nested group is ONE Custom row inside the top group
            Assert.Equal(LogicalOperator.Or, nOp);
            Assert.True(nRows[0].IsCustom);
            Assert.Equal(Json(nested), Json(RowProjection.ToDto(nRows, nOp)));
        }

        [Fact]
        public void C2_TheOneSentence_ReadsTheRows()
        {
            Assert.Equal("#10 is achieved", ConditionSentence.Of(RowProjection.ToDto(new ConditionRow(Subject.Thing(Bridge), "objective-state", Values: new FactValues(State: (int)ObjectiveState.Achieved)))));
            Assert.Equal("At least 3 of Red side are destroyed", ConditionSentence.Of(RowProjection.ToDto(new ConditionRow(Subject.OfSide(SubjectKind.AtLeast, ForceId.Hostile, 3), "in-the-fight", Negated: true))));
            Assert.Equal("It is after 1:00", ConditionSentence.Of(new ScenarioTimePredicateDto { FromSeconds = 60 }));
            Assert.Equal("#20 strength is below 50 %", ConditionSentence.Of(RowProjection.ToDto(new ConditionRow(Subject.Thing(Alpha), "strength", Negated: true, Values: new FactValues(Number: 0.5)))));
            // a debugging condition keeps its technical line (the breakpoints' column, CE-1285)
            Assert.Equal("Trace[0x07]", ConditionSentence.Of(new TraceBufferScanPredicateDto { OpCode = 7 }));
        }

        [Fact]
        public void C2_TheCachedSummary_FollowsAnEditInPlace_AndAllocatesNothingOnASteadyFrame()
        {
            var t = new ScenarioTimePredicateDto { FromSeconds = 60 };
            Assert.Equal("It is after 1:00", ConditionSentence.Summary(t));
            t.FromSeconds = 120;
            Assert.Equal("It is after 2:00", ConditionSentence.Summary(t));
            var dto = RowProjection.ToDto(new ConditionRow(Subject.OfSide(SubjectKind.AtLeast, ForceId.Hostile, 3), "in-the-fight", Negated: true));
            ConditionSentence.Summary(dto);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { ConditionSentence.Summary(dto); ConditionSentence.Summary(t); }
            Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        }
    }
}
