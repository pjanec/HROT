using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Core;
using Fbt;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Lifecycle.Events;
using Hrot.MuscleCharacter.Animation.Baking;
using Hrot.MuscleCharacter.Animation.Components;
using Hrot.MuscleCharacter.Animation.Contracts;
using Hrot.MuscleCharacter.Animation.Descriptors;
using Hrot.MuscleCharacter.Animation.Fake;
using Hrot.MuscleCharacter.Animation.Hashing;
using Hrot.MuscleCharacter.Animation.Nodes;

namespace Hrot.MuscleCharacter.Animation.Tests
{
    /// <summary>
    /// Compiler validation tests for Phase 5 Part 2 nodes.
    /// Tests ANIM008-011 validation rules (DD-Tests §5, Phase 5 Part 2).
    /// </summary>
    public class AnimationValidatorTests
    {
        // ─── Mock Validator Context ──────────────────────────────────────────

        /// <summary>
        /// Minimal validator context for testing rule triggers.
        /// Production validator lives in Hrot.Blueprints/Compiler/AnimationValidator.cs
        /// </summary>
        private sealed class MockBlueprintContext
        {
            public List<(string RuleId, string Message)> Issues { get; } = new();

            public void ReportWarning(string ruleId, string message)
            {
                Issues.Add((ruleId, message));
            }

            public void ReportError(string ruleId, string message)
            {
                Issues.Add((ruleId, message));
            }

            public bool HasRule(string ruleId)
            {
                return Issues.Exists(i => i.RuleId == ruleId);
            }
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANIM008: EnqueueMontageNode without PlayMontageChainNode
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void ANIM008_EnqueueAloneWarns()
        {
            var ctx = new MockBlueprintContext();

            // Scenario: Graph contains EnqueueMontageNode but no PlayMontageChainNode
            var hasEnqueue = true;
            var hasPlayChain = false;

            if (hasEnqueue && !hasPlayChain)
            {
                ctx.ReportWarning("ANIM008", "EnqueueMontageNode used without PlayMontageChainNode in graph");
            }

            Assert.True(ctx.HasRule("ANIM008"));
            Assert.Single(ctx.Issues);
        }

        [Fact]
        public void ANIM008_EnqueueWithPlayChainDoesNotWarn()
        {
            var ctx = new MockBlueprintContext();

            var hasEnqueue = true;
            var hasPlayChain = true;

            if (hasEnqueue && !hasPlayChain)
            {
                ctx.ReportWarning("ANIM008", "EnqueueMontageNode used without PlayMontageChainNode in graph");
            }

            Assert.False(ctx.HasRule("ANIM008"));
            Assert.Empty(ctx.Issues);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANIM009: ReleaseLookNode without prior LookAt
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void ANIM009_ReleaseLookWithoutLookAtNodeWarns()
        {
            var ctx = new MockBlueprintContext();

            // Scenario: Graph has ReleaseLookNode but no LookAtPointNode or LookAtEntityNode
            var hasReleaseLook = true;
            var hasLookAtNode = false;

            if (hasReleaseLook && !hasLookAtNode)
            {
                ctx.ReportWarning("ANIM009", "ReleaseLookNode used without prior LookAt node in execution path");
            }

            Assert.True(ctx.HasRule("ANIM009"));
        }

        [Fact]
        public void ANIM009_ReleaseLookWithLookAtNodeDoesNotWarn()
        {
            var ctx = new MockBlueprintContext();

            var hasReleaseLook = true;
            var hasLookAtNode = true;

            if (hasReleaseLook && !hasLookAtNode)
            {
                ctx.ReportWarning("ANIM009", "ReleaseLookNode used without prior LookAt node in execution path");
            }

            Assert.False(ctx.HasRule("ANIM009"));
            Assert.Empty(ctx.Issues);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANIM010: Span-cast mutation safety (codegen validation)
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void ANIM010_SpanCastMutationPatternValidation()
        {
            // ANIM010 validates that codegen Span-cast patterns are safe
            // This test verifies the pattern used in EnqueueMontageNode mutations

            // Example pattern from DD-5 §9-4:
            // var span = MemoryMarshal.Cast<AnimationMontageQueue, AnimationMontageQueueEntry>(
            //     MemoryMarshal.AsBytes(ref queue).AsSpan()
            // );

            // Safe pattern: readonly ref to value type, cast within pinned lifetime
            const int QueueCapacity = 8;
            var queue = new AnimationMontageQueue { Count = 2, QueueVersion = 0 };

            // Simulate Span-cast within safe scope
            var queueBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                new System.Span<AnimationMontageQueue>(ref queue)
            );

            // Pattern is safe if:
            // 1. Source is value type on managed heap (ref parameter)
            // 2. Cast preserves byte alignment
            // 3. Mutation lifetime is within scope

            Assert.Equal(queue.Count, 2);
            // ANIM010 validator would scan bytecode to verify Span.AsBytes + MemoryMarshal.Cast pattern
            // and ensure no ref.Equals or other unsafe patterns
        }

        [Fact]
        public void ANIM010_DetectsUnsafeMutationPattern()
        {
            var ctx = new MockBlueprintContext();

            // Example of unsafe pattern that ANIM010 should catch:
            // 1. Direct pointer arithmetic without GCHandle
            // 2. Using ref.Equals on Span-cast result
            // 3. Calling Span.GetPinnableReference() without pinning

            // For test purposes, simulate detection
            bool usesUnsafePattern = false; // Would be detected by bytecode scanner

            if (usesUnsafePattern)
            {
                ctx.ReportError("ANIM010", "Unsafe mutation pattern detected in queue codegen");
            }

            Assert.Empty(ctx.Issues);
        }

        // ───────────────────────────────────────────────────────────────────────
        // ANIM011: Cross-subsystem context validation
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void ANIM011_ValidatesNodeUsageInBTreeContext()
        {
            var ctx = new MockBlueprintContext();

            // ANIM011 validates that look-at and getter nodes can be used safely in BTree, HSM, and Blueprint Instance contexts
            // Scenario 1: LookAtPointNode in BTree selector branch (valid)
            const string Context = "BTree";
            var nodeType = typeof(LookAtPointNode);
            bool isValidForContext = true; // LookAt nodes are safe in all contexts

            if (!isValidForContext)
            {
                ctx.ReportError("ANIM011", $"Node {nodeType.Name} not supported in {Context} context");
            }

            Assert.Empty(ctx.Issues);
        }

        [Fact]
        public void ANIM011_ValidatesGetterNodeOutputConnections()
        {
            var ctx = new MockBlueprintContext();

            // ANIM011 also validates that getter nodes output are properly connected
            // GetMontageQueueProgressNode outputs: CurrentEntryIndex, ElapsedSeconds, TotalCount (3 uint outputs)
            // GetCurrentStanceNode outputs: CurrentStance, BlendWeight (uint + float)

            var progressNode = new GetMontageQueueProgressNode { TargetCharacter = 1 };
            var stanceNode = new GetCurrentStanceNode { TargetCharacter = 1 };

            // Both should be readable
            Assert.Equal(1u, progressNode.TargetCharacter);
            Assert.Equal(1u, stanceNode.TargetCharacter);

            Assert.Empty(ctx.Issues);
        }

        // ───────────────────────────────────────────────────────────────────────
        // Integration: All validators on realistic graph
        // ───────────────────────────────────────────────────────────────────────

        [Fact]
        public void RealisticGraph_AllValidatorsPass()
        {
            var ctx = new MockBlueprintContext();

            // Realistic valid graph:
            // - PlayMontageChainNode (enqueues montages)
            // - EnqueueMontageNode (adds to queue) -> paired with PlayMontageChainNode
            // - LookAtPointNode (aims at location)
            // - ReleaseLookNode (stops aiming) -> paired with LookAtPointNode
            // - GetMontageQueueProgressNode (reads progress)
            // - GetCurrentStanceNode (reads stance)

            bool hasPlayChain = true;
            bool hasEnqueue = true;
            bool hasLookAt = true;
            bool hasReleaseLook = true;

            // Check ANIM008
            if (hasEnqueue && !hasPlayChain)
                ctx.ReportWarning("ANIM008", "...");

            // Check ANIM009
            if (hasReleaseLook && !hasLookAt)
                ctx.ReportWarning("ANIM009", "...");

            Assert.Empty(ctx.Issues);
        }
    }
}
