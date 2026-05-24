using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Search;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Search
{
    /// <summary>
    /// SR-T01: serialization round-trip for all SearchPredicateDto subtypes.
    /// </summary>
    public class SearchPredicateDtoSerializationTests
    {
        public SearchPredicateDtoSerializationTests()
        {
            ComponentTypeRegistry.Clear();
        }

        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = false,
            IncludeFields  = true
        };

        // ── SR-T01: no Fdp.Presentation reference ──────────────────────────

        [Fact]
        public void SR_T01_SearchAssembly_DoesNotReference_FdpPresentation()
        {
            var assemblyName = typeof(RecordingSearchService).Assembly.GetName();
            var refs = typeof(RecordingSearchService).Assembly.GetReferencedAssemblies();

            foreach (var r in refs)
                Assert.DoesNotContain("Fdp.Presentation", r.Name ?? "");
        }

        // ── SR-T01b: CompoundPredicateDto round-trip ─────────────────────────

        [Fact]
        public void SR_T01b_CompoundPredicate_RoundTrip()
        {
            var dto = new CompoundPredicateDto
            {
                Operator = LogicalOperator.Or,
                Conditions = new List<SearchPredicateDto>
                {
                    new CompoundPredicateDto
                    {
                        Operator = LogicalOperator.And,
                        Conditions = new List<SearchPredicateDto>
                        {
                            new NumericPredicateDto { MinValue = 1.0, MaxValue = 99.0 },
                            new StringPredicateDto  { Substring = "hello", StartsWith = true }
                        }
                    }
                }
            };

            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as CompoundPredicateDto;

            Assert.NotNull(back);
            Assert.Equal(LogicalOperator.Or, back!.Operator);
            Assert.Single(back.Conditions);
            var inner = Assert.IsType<CompoundPredicateDto>(back.Conditions[0]);
            Assert.Equal(LogicalOperator.And, inner.Operator);
            Assert.Equal(2, inner.Conditions.Count);
        }

        // ── SR-T01c: NumericPredicateDto round-trip ──────────────────────────

        [Fact]
        public void SR_T01c_NumericPredicate_RoundTrip()
        {
            var dto = new NumericPredicateDto { MinValue = -5.5, MaxValue = 100.0 };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as NumericPredicateDto;
            Assert.NotNull(back);
            Assert.Equal(-5.5, back!.MinValue, 6);
            Assert.Equal(100.0, back.MaxValue, 6);
        }

        // ── SR-T01d: StringPredicateDto round-trip ───────────────────────────

        [Fact]
        public void SR_T01d_StringPredicate_RoundTrip()
        {
            var dto = new StringPredicateDto { Substring = "Alpha", StartsWith = false, ExactMatch = true };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as StringPredicateDto;
            Assert.NotNull(back);
            Assert.Equal("Alpha", back!.Substring);
            Assert.True(back.ExactMatch);
            Assert.False(back.StartsWith);
        }

        // ── SR-T01e: TransientEventPredicateDto round-trip ──────────────────

        [Fact]
        public void SR_T01e_TransientEventPredicate_RoundTrip()
        {
            var dto = new TransientEventPredicateDto
            {
                EventType  = typeof(int),
                AnyOccurrence = false,
                PropertyPath  = "Value",
                Operator      = SearchOperator.GreaterThan,
                TargetValue   = "42"
            };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as TransientEventPredicateDto;
            Assert.NotNull(back);
            Assert.Equal("Value", back!.PropertyPath);
            Assert.Equal(SearchOperator.GreaterThan, back.Operator);
            Assert.Equal("42", back.TargetValue);
        }

        // ── SR-T01f: LifecyclePredicateDto round-trip ────────────────────────

        [Fact]
        public void SR_T01f_LifecyclePredicate_RoundTrip()
        {
            var dto = new LifecyclePredicateDto
            {
                IdentifierType   = EntityIdentifierType.NameSubstring,
                TargetValue      = "Alpha",
                NamePropertyPath = "Name"
            };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as LifecyclePredicateDto;
            Assert.NotNull(back);
            Assert.Equal(EntityIdentifierType.NameSubstring, back!.IdentifierType);
            Assert.Equal("Alpha", back.TargetValue);
        }

        // ── SR-T01g: SpatialBoundingPredicateDto round-trip ─────────────────

        [Fact]
        public void SR_T01g_SpatialBoundingPredicate_RoundTrip()
        {
            var dto = new SpatialBoundingPredicateDto
            {
                Bounds        = new BoundingBox2D { Min = new Vector2(0f, 0f), Max = new Vector2(10f, 10f) },
                TriggerEvent  = BoundaryEvent.Entry,
                PositionXPath = "Position.X",
                PositionYPath = "Position.Y"
            };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as SpatialBoundingPredicateDto;
            Assert.NotNull(back);
            Assert.Equal(BoundaryEvent.Entry, back!.TriggerEvent);
            Assert.Equal(10f, back.Bounds.Max.X, 3);
        }

        // ── SR-T01h: StructuralPredicateDto with RequireGhost ────────────────

        [Fact]
        public void SR_T01h_StructuralPredicate_RequireGhost_RoundTrip()
        {
            var dto = new StructuralPredicateDto
            {
                ComponentType        = typeof(int),
                ModificationType     = StructuralModification.Added,
                AuthorityRequirement = AuthorityRequirement.RequireGhost
            };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as StructuralPredicateDto;
            Assert.NotNull(back);
            Assert.Equal(AuthorityRequirement.RequireGhost, back!.AuthorityRequirement);
        }

        // ── SR-T01i: StructuralPredicateDto with RequireAuthority ────────────

        [Fact]
        public void SR_T01i_StructuralPredicate_RequireAuthority_RoundTrip()
        {
            var dto = new StructuralPredicateDto
            {
                ComponentType        = typeof(int),
                ModificationType     = StructuralModification.Removed,
                AuthorityRequirement = AuthorityRequirement.RequireAuthority
            };
            string json = JsonSerializer.Serialize((SearchPredicateDto)dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as StructuralPredicateDto;
            Assert.NotNull(back);
            Assert.Equal(AuthorityRequirement.RequireAuthority, back!.AuthorityRequirement);
            Assert.Equal(StructuralModification.Removed, back.ModificationType);
        }

        // ── P6T1: BlueprintVariablePredicateDto round-trip ─────────────────

        /// <summary>
        /// P6T1 success condition: BlueprintVariablePredicateDto survives a JSON round-trip
        /// preserving all fields including the nested NumericPredicateDto.
        /// </summary>
        [Fact]
        public void BlueprintVariablePredicate_SerializesRoundTrip()
        {
            var assetId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
            var dto = new BlueprintVariablePredicateDto
            {
                TargetBlueprintAssetId = assetId,
                VariableName           = "AmmoCount",
                Operator               = SearchOperator.Equals,
                Predicate              = new NumericPredicateDto { MinValue = 0.0, MaxValue = 0.0 },
            };

            string json = JsonSerializer.Serialize<SearchPredicateDto>(dto, _options);
            var back = JsonSerializer.Deserialize<SearchPredicateDto>(json, _options) as BlueprintVariablePredicateDto;

            Assert.NotNull(back);
            Assert.Equal(assetId, back.TargetBlueprintAssetId);
            Assert.Equal("AmmoCount", back.VariableName);
            Assert.Equal(SearchOperator.Equals, back.Operator);
            var numPred = Assert.IsType<NumericPredicateDto>(back.Predicate);
            Assert.Equal(0.0, numPred.MinValue);
            Assert.Equal(0.0, numPred.MaxValue);
        }
    }
}
