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
    }
}
