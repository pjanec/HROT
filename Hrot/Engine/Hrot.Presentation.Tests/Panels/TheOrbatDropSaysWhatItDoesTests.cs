using System.Collections.Generic;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Models;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ L-13 / CE-4313 (Scenario lifecycle LC-5) — the ORBAT drop SAYS what it will do before release, and the words are the SAME test the drop acts on:
/// a row that takes subordinates ⇒ <i>"Attach to …"</i> and the drop assigns; any other row ⇒ <i>"Board …"</i> and the drop embarks.
/// 🔴 Red-proof (compiles): <c>DropVerb</c> answering "Attach" for every row ⇒ the vehicle row ⇒ red.
/// </summary>
public sealed class TheOrbatDropSaysWhatItDoesTests
{
    private sealed class Recorder : IOrbatController
    {
        public readonly List<string> Calls = new();
        public void SelectEntity(int entityId) { }
        public void CreateUnit(long tkbType) { }
        public void ToggleExpanded(int entityId) { }
        public void RequestEmbark(int passengerEntityId, int vehicleEntityId) => Calls.Add($"embark {passengerEntityId}->{vehicleEntityId}");
        public void RequestDisembark(int passengerEntityId) { }
        public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId) => Calls.Add($"assign {subordinateEntityId}->{commanderEntityId}");
        public void RequestRemoveSubordinate(int subordinateEntityId) { }
    }

    [Fact]
    public void L13_TheVerbIsWhatTheDropDoes_AttachToAUnit_BoardAVehicle()
    {
        var platoon = new OrbatNodeViewModel(10, "1st Platoon", 0, true, false, CanAcceptSubordinates: true);
        var truck = new OrbatNodeViewModel(20, "Truck 2", 1, false, false, CanAcceptSubordinates: false);
        Assert.Equal("Attach to 1st Platoon", SharedOrbatPanel.DropVerb(platoon));
        Assert.Equal("Board Truck 2", SharedOrbatPanel.DropVerb(truck));

        var panel = new SharedOrbatPanel();
        var ctrl = new Recorder();
        panel.HandleHierarchyDropPayload(5, platoon, ctrl);
        panel.HandleHierarchyDropPayload(5, truck, ctrl);
        Assert.Equal(new[] { "assign 5->10", "embark 5->20" }, ctrl.Calls);      // the words and the act agree
    }
}
