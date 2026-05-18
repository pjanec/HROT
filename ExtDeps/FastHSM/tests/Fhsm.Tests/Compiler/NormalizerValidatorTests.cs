using System;
using System.Linq;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;

namespace Fhsm.Tests.Compiler
{
    public class NormalizerValidatorTests
    {
        [Fact]
        public void Normalizer_Index_Assigned_in_BFS()
        {
            // Root -> A -> B
            //      -> C
            // Order: Root(0), A(1), C(2), B(3)
            
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            var b = new StateNode("B");
            var c = new StateNode("C");
             
            graph.AddState(a);
            graph.AddState(b);
            graph.AddState(c);
             
            graph.RootState.AddChild(a);
            graph.RootState.AddChild(c);
            a.AddChild(b);
             
            HsmNormalizer.Normalize(graph);
             
            Assert.Equal(0, graph.RootState.FlatIndex);
            Assert.Equal(1, a.FlatIndex);
            Assert.Equal(2, c.FlatIndex);
            Assert.Equal(3, b.FlatIndex);
        }

        [Fact]
        public void Normalizer_Depth_Computed_Correctly()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            var b = new StateNode("B");
            
            graph.AddState(a);
            graph.AddState(b);
            graph.RootState.AddChild(a);
            a.AddChild(b);
            
            HsmNormalizer.Normalize(graph);
            
            Assert.Equal(0, graph.RootState.Depth);
            Assert.Equal(1, a.Depth);
            Assert.Equal(2, b.Depth);
        }
        
        [Fact]
        public void Normalizer_Resolves_Initial_States()
        {
            var graph = new StateMachineGraph("G");
            var parent = new StateNode("P");
            var child1 = new StateNode("C1");
            var child2 = new StateNode("C2");
            
            graph.AddState(parent);
            graph.AddState(child1);
            graph.AddState(child2);
            
            graph.RootState.AddChild(parent);
            parent.AddChild(child1);
            parent.AddChild(child2);
            
            // Neither marked initial initially
            Assert.False(child1.IsInitial);
            Assert.False(child2.IsInitial);
            
            HsmNormalizer.Normalize(graph);
            
            // Should resolve first child (C1) as initial
            Assert.True(child1.IsInitial);
            Assert.False(child2.IsInitial);
        }

        [Fact]
        public void Normalizer_Preserves_Explicit_Initial()
        {
            var graph = new StateMachineGraph("G");
            var parent = new StateNode("P");
            var child1 = new StateNode("C1");
            var child2 = new StateNode("C2");
            child2.IsInitial = true;
            
            graph.AddState(parent);
            graph.AddState(child1);
            graph.AddState(child2);
            
            graph.RootState.AddChild(parent);
            parent.AddChild(child1);
            parent.AddChild(child2);
            
            HsmNormalizer.Normalize(graph);
            
            Assert.False(child1.IsInitial);
            Assert.True(child2.IsInitial);
        }
        
        [Fact]
        public void Normalizer_Assigns_History_Slots_Sorted_By_StableId()
        {
            var graph = new StateMachineGraph("G");
            // Define two history states
            // Give them Guids to force order regardless of add order
            var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
            
            var h1 = new StateNode("H1", id2); // H1 has *higher* ID, should be slot 1
            var h2 = new StateNode("H2", id1); // H2 has *lower* ID, should be slot 0
            
            h1.IsHistory = true;
            h2.IsHistory = true;

            // Parents necessary for valid structure
            var p = new StateNode("P");
            p.AddChild(h1);
            p.AddChild(h2);
            // Need children for p to be composite? History state MUST have composite parent.
            // But HsmNormalizer doesn't validate, it just assigns.
            // Let's attach to root
            graph.RootState.AddChild(p); 
            
            graph.AddState(p);
            graph.AddState(h1);
            graph.AddState(h2);
            
            HsmNormalizer.Normalize(graph);
            
            Assert.Equal(1, h1.HistorySlotIndex);
            Assert.Equal(0, h2.HistorySlotIndex);
        }

        [Fact]
        public void Validator_Valid_Graph_Passes()
        {
            var builder = new HsmBuilder("Valid");
            builder.State("A");
            
            var graph = builder.GetGraph();
            
            // Validate expects normalized graph (resolved defaults)
            HsmNormalizer.Normalize(graph);
            
            var errors = HsmGraphValidator.Validate(graph);
            Assert.Empty(errors);
        }
        
        [Fact]
        public void Validator_Detects_Orphan_States()
        {
            var graph = new StateMachineGraph("G");
            var orphan = new StateNode("Orphan");
            graph.AddState(orphan); 
            // Not added to Root's children or any reachable parent's children
            
            var errors = HsmGraphValidator.Validate(graph);
            Assert.Contains(errors, e => e.Message.Contains("Orphan"));
        }
        
        [Fact]
        public void Validator_Detects_Circular_Parent_Chain()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            var b = new StateNode("B");
            
            graph.AddState(a);
            graph.AddState(b);
            
            graph.RootState.AddChild(a);
            
            // Create cycle: A -> B -> A
            a.AddChild(b);
            // Manually corrupt parent to point back to B?
            // "AddChild" sets parent. 
            // b.AddChild(a); // This would remove A from root list usually? No implementation of AddChild is simple List.Add
            
            // Let's hack it
            b.AddChild(a); // a.Parent = b. b.Parent = a.
            
            var errors = HsmGraphValidator.Validate(graph);
            Assert.Contains(errors, e => e.Message.Contains("Circular"));
        }
        
        [Fact]
        public void Validator_Detects_Null_Transition_Target()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            graph.AddState(a);
            graph.RootState.AddChild(a);
            
            var t = new TransitionNode(a, null!, 1);
            a.AddTransition(t);
            
            var errors = HsmGraphValidator.Validate(graph);
            Assert.Contains(errors, e => e.Message.Contains("null target"));
        }

        [Fact]
        public void Validator_Detects_Composite_No_Initial()
        {
             // Validator runs usually BEFORE normalizer fixes this?
             // Or AFTER?
             // If Normalizer runs, it auto-fixes missing initial by picking first child.
             // So Validator assumes Normalizer ran OR enforces explicit.
             // Instructions say: "11. Each composite has exactly one initial child"
             // But Normalizer says: "Resolve Initial States - Each composite needs initial child"
             // If we run Validator on raw graph, it might fail. If we run Normalize first, it passes.
             // Let's test Validator behavior on raw graph.
             
             var graph = new StateMachineGraph("G");
             var p = new StateNode("P");
             var c = new StateNode("C");
             
             graph.AddState(p);
             graph.AddState(c);
             graph.RootState.AddChild(p);
             p.AddChild(c);
             // No initial set
             
             var errors = HsmGraphValidator.Validate(graph);
             Assert.Contains(errors, e => e.Message.Contains("no initial child"));
        }
        
        [Fact]
        public void Validator_Detects_Unregistered_Actions()
        {
            var graph = new StateMachineGraph("G");
            var a = new StateNode("A");
            a.OnEntryAction = "UnknownFunc";
            
            graph.AddState(a);
            graph.RootState.AddChild(a);
            
            var errors = HsmGraphValidator.Validate(graph);
            Assert.Contains(errors, e => e.Message.Contains("not registered"));
        }

        [Fact]
        public void Validator_Detects_History_No_Parent()
        {
             var graph = new StateMachineGraph("G");
             var h = new StateNode("H");
             h.IsHistory = true;
             
             graph.AddState(h);
             // Make reachable so avoid orphan error masking this?
             graph.RootState.AddChild(h);
             
             // Root is parent, has children (H). So parent is composite.
             // Root is "__Root".
             // History needs parent. Root has children, so it's composite.
             // So this is actually valid?
             
             // Let's make H orphan but reachable via hack?
             // Or just verify "History state has no parent" logic.
             // HsmGraphValidator checks: if (state.Parent == null)
             
             // Manually set parent null
             h.Parent = null; 
             // But keep in States list for iteration
             
             var errors = HsmGraphValidator.Validate(graph);
             // Will also trigger Orphan error
             Assert.Contains(errors, e => e.Message.Contains("History state has no parent"));
        }

        [Fact]
        public void Validator_Detects_History_Parent_Not_Composite()
        {
             var graph = new StateMachineGraph("G");
             var p = new StateNode("P"); // Parent
             var h = new StateNode("H"); // History
             h.IsHistory = true;
             
             graph.AddState(p);
             graph.AddState(h);
             graph.RootState.AddChild(p);
             
             // Link H to P but P has no *other* children?
             // Actually, parent must be composite. If P has H as child, P IS composite?
             // Wait. If H is a child of P, P.Children contains H. So P.Children.Count > 0.
             // So P is composite.
             // The rule "History parent is composite" implies it must have sub-states *to return to*.
             // If the only child is the history state itself, where does it go?
             // The spec says: "14. History parent is composite (has children)"
             
             // If H is correctly added to P using AddChild, P has children.
             // If H says "Parent = P" but P.Children is empty (bad graph), that's the error.
             
             p.Children.Clear(); // Empty children manually
             h.Parent = p;
             
             var errors = HsmGraphValidator.Validate(graph);
             Assert.Contains(errors, e => e.Message.Contains("parent is not composite"));
        }
        
    }
}