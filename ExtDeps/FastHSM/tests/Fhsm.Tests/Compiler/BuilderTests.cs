using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;

namespace Fhsm.Tests.Compiler
{
    public class BuilderTests
    {
        [Fact]
        public void Builder_Creates_Graph_With_Name()
        {
            var builder = new HsmBuilder("TestMachine");
            var graph = builder.GetGraph();
            Assert.Equal("TestMachine", graph.Name);
            Assert.NotNull(graph.RootState);
            Assert.Equal("__Root", graph.RootState.Name);
        }

        [Fact]
        public void Builder_State_Adds_State_To_Graph()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle");
            
            var graph = builder.GetGraph();
            var state = graph.FindState("Idle");
            
            Assert.NotNull(state);
            Assert.Equal("Idle", state.Name);
            Assert.Contains(state, graph.RootState.Children);
        }

        [Fact]
        public void Builder_Event_Registers_EventId()
        {
            var builder = new HsmBuilder("M");
            builder.Event("Ping", 100);
            
            var graph = builder.GetGraph();
            Assert.True(graph.EventNameToId.TryGetValue("Ping", out var id));
            Assert.Equal(100, id);
        }

        [Fact]
        public void Builder_OnEntry_OnExit_Activity_Set_Actions()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle")
                   .OnEntry("EnterIdle")
                   .OnExit("ExitIdle")
                   .Activity("DoIdle");
            
            var graph = builder.GetGraph();
            var state = graph.FindState("Idle");
            
            Assert.Equal("EnterIdle", state.OnEntryAction);
            Assert.Equal("ExitIdle", state.OnExitAction);
            Assert.Equal("DoIdle", state.ActivityAction);
        }

        [Fact]
        public void Builder_Child_Creates_Hierarchy()
        {
            var builder = new HsmBuilder("M");
            builder.State("Parent")
                   .Child("Child", c => {});
            
            var graph = builder.GetGraph();
            var parent = graph.FindState("Parent");
            var child = graph.FindState("Child");
            
            Assert.NotNull(parent);
            Assert.NotNull(child);
            Assert.Contains(child, parent.Children);
            Assert.Equal(parent, child.Parent);
        }

        [Fact]
        public void Builder_On_GoTo_Creates_Transition()
        {
            var builder = new HsmBuilder("M");
            builder.Event("Next", 1);
            builder.State("A");
            builder.State("B");
            
            // Re-access builder for state A to add transition
            // Note: In current API, State() returns StateBuilder. 
            // We can't easily get back to a previous StateBuilder without calling State() again or storing it.
            // But we can just use the fluent chain.
            
            // Reconstruct logic:
            // builder.State("A").On("Next").GoTo("B");
            // But "B" must exist?
            // "GoTo" calls FindState. So B must be added to graph *before* GoTo is called.
            
            // Correct usage:
            // Define states first? Or rely on order.
            
            var builder2 = new HsmBuilder("M");
            builder2.Event("Next", 1);
            
            // We must add B first if we want to transition to it immediately?
            // The API implementation of GoTo checks: `_graph.FindState(targetStateName)`.
            // So indeed, target must exist.
            
            builder2.State("B"); // Add B
            builder2.State("A").On("Next").GoTo("B");
            
            var graph = builder2.GetGraph();
            var stateA = graph.FindState("A");
            var stateB = graph.FindState("B");
            
            Assert.Single(stateA.Transitions);
            var trans = stateA.Transitions[0];
            Assert.Equal(stateB, trans.Target);
            Assert.Equal(1, trans.EventId);
        }

        [Fact]
        public void Builder_Guard_Action_Configure_Transition()
        {
            var builder = new HsmBuilder("M");
            builder.Event("Next", 1);
            builder.State("B");
            builder.State("A")
                   .On("Next")
                   .Guard("Check")
                   .Action("Execute")
                   .GoTo("B");
                   
            var graph = builder.GetGraph();
            var stateA = graph.FindState("A");
            var trans = stateA.Transitions[0];
            
            Assert.Equal("Check", trans.GuardFunction);
            Assert.Equal("Execute", trans.ActionFunction);
        }

        [Fact]
        public void Builder_Multiple_Transitions_Work()
        {
            var builder = new HsmBuilder("M");
            builder.Event("E1", 1).Event("E2", 2);
            builder.State("B");
            builder.State("C");
            
            var a = builder.State("A");
            a.On("E1").GoTo("B");
            a.On("E2").GoTo("C");
            
            var graph = builder.GetGraph();
            var stateA = graph.FindState("A");
            
            Assert.Equal(2, stateA.Transitions.Count);
        }

        [Fact]
        public void Builder_FindState_Lookup_Works()
        {
            var builder = new HsmBuilder("M");
            builder.State("S1");
            Assert.NotNull(builder.GetGraph().FindState("S1"));
            Assert.Null(builder.GetGraph().FindState("DoesNotExist"));
        }

        [Fact]
        public void Builder_Duplicate_State_Names_Throw()
        {
            var builder = new HsmBuilder("M");
            builder.State("S1");
            
            Assert.Throws<InvalidOperationException>(() => builder.State("S1"));
        }

        [Fact]
        public void Builder_Unknown_Event_Throws()
        {
             var builder = new HsmBuilder("M");
             builder.State("A");
             
             Assert.Throws<InvalidOperationException>(() => builder.State("A").On("UnknownEvent"));
        }

        [Fact]
        public void Builder_Unknown_Target_State_Throws()
        {
             var builder = new HsmBuilder("M");
             builder.Event("E", 1);
             builder.State("A");
             
             Assert.Throws<InvalidOperationException>(() => builder.State("A").On("E").GoTo("UnknownState"));
        }

        [Fact]
        public void Builder_Initial_Flag_Sets_Correctly()
        {
            var builder = new HsmBuilder("M");
            builder.State("Init").Initial();
            
            var s = builder.GetGraph().FindState("Init");
            Assert.True(s.IsInitial);
        }

        [Fact]
        public void Builder_History_Flag_Sets_Correctly()
        {
            var builder = new HsmBuilder("M");
            builder.State("Hist").History();
            
            var s = builder.GetGraph().FindState("Hist");
            Assert.True(s.IsHistory);
        }
        
        [Fact]
        public void Builder_RegisterAction_Adds_To_Set()
        {
            var builder = new HsmBuilder("M");
            builder.RegisterAction("MyAction");
            
            Assert.Contains("MyAction", builder.GetGraph().RegisteredActions);
        }
        
        [Fact]
        public void Builder_RegisterGuard_Adds_To_Set()
        {
            var builder = new HsmBuilder("M");
            builder.RegisterGuard("MyGuard");
            
            Assert.Contains("MyGuard", builder.GetGraph().RegisteredGuards);
        }
        
        [Fact]
        public void Builder_Priority_Sets_Correctly()
        {
            var builder = new HsmBuilder("M");
            builder.Event("E", 1);
            builder.State("B");
            builder.State("A").On("E").Priority(10).GoTo("B");
            
            var s = builder.GetGraph().FindState("A");
            Assert.Equal(10, s.Transitions[0].Priority);
        }
        
        [Fact]
        public void TransitionBuilder_Allows_Config_Before_Target()
        {
            // Verify that we can set Guard/Action before GoTo
            var builder = new HsmBuilder("M");
            builder.Event("E", 1);
            builder.State("B");
            
            builder.State("A")
                   .On("E")
                   .Guard("G")
                   .Action("A")
                   .GoTo("B");
            
            var t = builder.GetGraph().FindState("A").Transitions[0];
            Assert.Equal("G", t.GuardFunction);
            Assert.Equal("A", t.ActionFunction);
            Assert.Equal("B", t.Target.Name);
        }
    }
}