using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;

namespace Fhsm.Tests.Compiler
{
    /// <summary>Tests for BHU-014: HsmGraphValidator.ValidateChannelSafety.</summary>
    public class HsmGraphValidatorChannelSafetyTests
    {
        // Shared cleanup dictionary: "MoveAction" requires "ExitCleanup_MoveAction" exit action.
        private static readonly IReadOnlyDictionary<string, string> CleanupMap =
            new Dictionary<string, string>
            {
                ["MoveAction"]  = "ExitCleanup_MoveAction",
                ["ShootAction"] = "ExitCleanup_ShootAction",
            };

        private static StateMachineGraph BuildGraph(params StateNode[] extraStates)
        {
            var g = new StateMachineGraph("G");
            foreach (var s in extraStates) g.AddState(s);
            return g;
        }

        // CS1: State with OnEntryAction that writes a channel and the correct
        // OnExitAction -> no channel-safety errors.
        [Fact]
        public void OnEntry_ChannelWriter_CorrectExit_NoErrors()
        {
            var state = new StateNode("Moving")
            {
                OnEntryAction = "MoveAction",
                OnExitAction  = "ExitCleanup_MoveAction",
            };
            var graph = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Empty(errors);
        }

        // CS2: State with OnEntryAction that writes a channel but wrong OnExitAction
        // -> one channel-safety error for that state.
        [Fact]
        public void OnEntry_ChannelWriter_WrongExit_ReportsError()
        {
            var state = new StateNode("Moving")
            {
                OnEntryAction = "MoveAction",
                OnExitAction  = "SomeOtherExit",
            };
            var graph = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Single(errors);
            Assert.Contains("MoveAction", errors[0].Message);
        }

        // CS3: State with ActivityAction that writes a channel but missing OnExitAction
        // -> one channel-safety error.
        [Fact]
        public void ActivityAction_ChannelWriter_MissingExit_ReportsError()
        {
            var state = new StateNode("Shooting")
            {
                ActivityAction = "ShootAction",
                // OnExitAction intentionally absent
            };
            var graph = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Single(errors);
            Assert.Contains("ShootAction", errors[0].Message);
        }

        // CS4: State whose OnEntryAction is NOT in the cleanup map -> no channel-safety
        // errors (ordinary action, no channel responsibility).
        [Fact]
        public void OnEntry_NonChannelAction_NoErrors()
        {
            var state = new StateNode("Idle")
            {
                OnEntryAction = "SomeOrdinaryAction",
            };
            var graph = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Empty(errors);
        }

        // CS5: Validate(graph, null) must not throw and must return the same result
        // as Validate(graph) (no channel checks applied).
        [Fact]
        public void Validate_NullCleanupDict_NoCrash()
        {
            var state = new StateNode("S");
            var graph = BuildGraph(state);

            var errorsWithNull    = HsmGraphValidator.Validate(graph, (IReadOnlyDictionary<string, string>?)null);
            var errorsWithoutDict = HsmGraphValidator.Validate(graph);

            // Both paths should produce the same errors (no channel-safety additions).
            Assert.Equal(errorsWithoutDict.Count, errorsWithNull.Count);
        }
        // CS6: Empty cleanup dict -> no channel-safety errors regardless of state content.
        [Fact]
        public void Validate_EmptyCleanupDict_NoChannelErrors()
        {
            var state = new StateNode("Moving")
            {
                OnEntryAction = "MoveAction",
            };
            var graph  = BuildGraph(state);
            var empty  = new Dictionary<string, string>();
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, empty, errors);

            Assert.Empty(errors);
        }

        // ── CE-406 — the check RE-AIMED for the auto-bind world (Q74 section 9.5) ──────────

        // The fully-qualified name an editor-authored asset actually holds. The cleanup map
        // above is keyed on the SHORT name, exactly as HsmActionGenerator emits it (m.Name).
        private const string MoveActionFqn = "Hrot.AI.Behaviors.Brains.SomeNodes.MoveAction";

        /// <summary>
        /// CE406_R1 -- a state whose cleanup was bound by the AUTO-BIND must NOT be flagged.
        ///
        /// This is the false positive that repairing the key shape alone would have produced,
        /// and it is why this check could not simply be "wired up". Q74 D-B1/D-D1 bake
        /// .OnExitId(hash) into ExitActionId and leave OnExitAction null; the original rule
        /// demanded OnExitAction == required, so it would have reddened every correctly
        /// auto-bound state -- starting with HsmTwoChannelRegionsDemo, the asset auto-bind fixed.
        ///
        /// Red-proof: delete the `if (state.ExitActionId != 0) continue;` guard -> this reddens.
        /// </summary>
        [Fact]
        public void CE406_R1_AutoBoundExitActionId_IsNotFlagged()
        {
            var state = new StateNode("Driving")
            {
                ActivityAction = MoveActionFqn,
                OnExitAction   = null,      // the emitter leaves this null...
                ExitActionId   = 26097,     // ...and bakes the id instead
            };
            var graph  = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Empty(errors);
        }

        /// <summary>
        /// CE406_R2 -- the KEY-SHAPE fix: an activity named by its FQN is now seen.
        ///
        /// The cleanup map is keyed on the short method name while assets hold the FQN, so
        /// requiredExitCleanups.ContainsKey(state.ActivityAction) missed every real state --
        /// the check was silently inert even once RequiredExitCleanups became non-empty.
        /// Matching on the short name is what HsmActionDispatcher itself does, so this makes
        /// the validator agree with runtime rather than loosening it.
        ///
        /// Red-proof: revert ShortMemberName to a direct ContainsKey -> no error is produced.
        /// </summary>
        [Fact]
        public void CE406_R2_FullyQualifiedActivityName_IsMatchedAgainstTheShortKeyedMap()
        {
            var state = new StateNode("Driving")
            {
                ActivityAction = MoveActionFqn,
                // no OnExitAction and no ExitActionId -- nothing releases the channel
            };
            var graph  = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Single(errors);
            Assert.Contains("ExitCleanup_MoveAction", errors[0].Message);
        }

        /// <summary>
        /// CE406_R3 -- the ONE hole the auto-bind cannot fill, and the reason this check is
        /// kept rather than deleted.
        ///
        /// HsmEmitCore binds .OnExitId only when the state has no OnExitAction of its own
        /// (fill-an-empty-slot; HsmFlattener:173 makes the baked id win over the name, so the
        /// rule has to live at the emit site). A state that authors its own OnExitAction
        /// therefore silently loses its channel cleanup, and nothing else in the toolchain
        /// says so. Measured 2026-09-28: zero of the 28 OnExitAction entries across the six
        /// shipped .hsm.json are non-null, so this is a hole with no occupants yet.
        /// </summary>
        [Fact]
        public void CE406_R3_AnAuthoredOnExitDisplacesTheCleanup_AndIsReported()
        {
            var state = new StateNode("Driving")
            {
                ActivityAction = MoveActionFqn,
                OnExitAction   = "PlayDismountAnimation",   // the author's own exit action
                // ExitActionId stays 0: the emitter will not overwrite an authored name
            };
            var graph  = BuildGraph(state);
            var errors = new List<HsmGraphValidator.ValidationError>();

            HsmGraphValidator.ValidateChannelSafety(graph, CleanupMap, errors);

            Assert.Single(errors);
            Assert.Contains("PlayDismountAnimation", errors[0].Message);
            Assert.Contains("ExitCleanup_MoveAction", errors[0].Message);
        }
    }
}
