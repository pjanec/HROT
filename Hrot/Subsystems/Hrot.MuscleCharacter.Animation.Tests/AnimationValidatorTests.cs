using System;
using System.Collections.Generic;
using Xunit;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Nodes;
using Hrot.MuscleCharacter.Animation.Validation;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Compiler validation tests for Phase 5 Part 2 nodes (OFX-006).
    /// Tests ANIM008-011 validation rules using the real BlueprintGraphIr
    /// and BlueprintAnimationValidators classes (DD-Tests Â§5, Phase 5 Part 2).
    /// </summary>
    public class AnimationValidatorTests
    {
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // ANIM008: EnqueueMontageNode without PlayMontageChainNode
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void ANIM008_EnqueueAloneWarns()
        {
            var graph = new BlueprintGraphIr(typeof(EnqueueMontageNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim008(graph);

            Assert.Single(msgs);
            Assert.Equal("ANIM008", msgs[0].RuleId);
            Assert.Equal(ValidationSeverity.Warning, msgs[0].Severity);
        }

        [Fact]
        public void ANIM008_EnqueueWithPlayChainDoesNotWarn()
        {
            var graph = new BlueprintGraphIr(typeof(PlayMontageChainNode), typeof(EnqueueMontageNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim008(graph);

            Assert.Empty(msgs);
        }

        [Fact]
        public void ANIM008_NoEnqueue_DoesNotWarn()
        {
            // Graph has no animation queue nodes at all â€” no ANIM008 warning.
            var graph = new BlueprintGraphIr(typeof(PlayMontageNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim008(graph);

            Assert.Empty(msgs);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // ANIM009: ReleaseLookNode without prior LookAt
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void ANIM009_ReleaseLookWithoutLookAtNodeWarns()
        {
            var graph = new BlueprintGraphIr(typeof(ReleaseLookNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim009(graph);

            Assert.Single(msgs);
            Assert.Equal("ANIM009", msgs[0].RuleId);
            Assert.Equal(ValidationSeverity.Warning, msgs[0].Severity);
        }

        [Fact]
        public void ANIM009_ReleaseLookWithLookAtPointNodeDoesNotWarn()
        {
            var graph = new BlueprintGraphIr(typeof(LookAtPointNode), typeof(ReleaseLookNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim009(graph);

            Assert.Empty(msgs);
        }

        [Fact]
        public void ANIM009_ReleaseLookWithLookAtEntityNodeDoesNotWarn()
        {
            var graph = new BlueprintGraphIr(typeof(LookAtEntityNode), typeof(ReleaseLookNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim009(graph);

            Assert.Empty(msgs);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // ANIM010: Codegen pattern self-check
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void ANIM010_SpanCastPatternIsAccepted()
        {
            var msgs = BlueprintAnimationValidators.ValidateAnim010("SpanCast");

            Assert.Empty(msgs);
        }

        [Fact]
        public void ANIM010_PointerCastPatternIsAccepted()
        {
            var msgs = BlueprintAnimationValidators.ValidateAnim010("PointerCast");

            Assert.Empty(msgs);
        }

        [Fact]
        public void ANIM010_UnknownPatternIsError()
        {
            var msgs = BlueprintAnimationValidators.ValidateAnim010("DirectPointer");

            Assert.Single(msgs);
            Assert.Equal("ANIM010", msgs[0].RuleId);
            Assert.Equal(ValidationSeverity.Error, msgs[0].Severity);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // ANIM011: Animation primitives used on entity without AnimationChannel
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void ANIM011_AnimPrimitiveWithNoAnimDef_IsError()
        {
            // Entity class has NO animation descriptor â€” using PlayMontageNode must error.
            var graph = new BlueprintGraphIr(typeof(PlayMontageNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim011(graph, "Turret", entityAnimDef: null);

            Assert.Single(msgs);
            Assert.Equal("ANIM011", msgs[0].RuleId);
            Assert.Equal(ValidationSeverity.Error, msgs[0].Severity);
        }

        [Fact]
        public void ANIM011_AnimPrimitiveWithAnimDef_IsOk()
        {
            // Entity class HAS animation descriptor â€” animation primitives are valid.
            var graph = new BlueprintGraphIr(typeof(PlayMontageNode), typeof(LookAtPointNode));
            var dto = new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>(),
                Montages = new List<MontageDefDto>(),
                SupportedStances = Array.Empty<Components.StanceId>(),
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };

            var msgs = BlueprintAnimationValidators.ValidateAnim011(graph, "Soldier", entityAnimDef: dto);

            Assert.Empty(msgs);
        }

        [Fact]
        public void ANIM011_MultipleAnimPrimitivesWithNoAnimDef_EachReported()
        {
            // Two animation primitives on an entity without animation config â€” both are errors.
            var graph = new BlueprintGraphIr(typeof(PlayMontageNode), typeof(SetStanceNode));

            var msgs = BlueprintAnimationValidators.ValidateAnim011(graph, "Projectile", entityAnimDef: null);

            Assert.Equal(2, msgs.Count);
            Assert.All(msgs, m => Assert.Equal("ANIM011", m.RuleId));
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Integration: Valid full graph passes all rules
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void RealisticGraph_AllValidatorsPass()
        {
            // Valid graph: PlayMontageChain + Enqueue + LookAt + ReleaseLook.
            var graph = new BlueprintGraphIr(
                typeof(PlayMontageChainNode),
                typeof(EnqueueMontageNode),
                typeof(LookAtPointNode),
                typeof(ReleaseLookNode));
            var dto = new CharacterAnimationDefDto
            {
                Slots = new List<SlotDefDto>(),
                Montages = new List<MontageDefDto>(),
                SupportedStances = Array.Empty<Components.StanceId>(),
                StanceTransitions = new List<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new List<NotifyMarkerDefDto>(),
            };

            Assert.Empty(BlueprintAnimationValidators.ValidateAnim008(graph));
            Assert.Empty(BlueprintAnimationValidators.ValidateAnim009(graph));
            Assert.Empty(BlueprintAnimationValidators.ValidateAnim010("SpanCast"));
            Assert.Empty(BlueprintAnimationValidators.ValidateAnim011(graph, "Soldier", dto));
        }
    }
}
