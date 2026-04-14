using System;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Replication.Components;
using Fdp.Examples.NetworkDemo.Descriptors;

namespace Fdp.Examples.NetworkDemo.Configuration
{
    public class DemoTkbSetup
    {
        public void Load(ITkbDatabase tkb)
        {
            // Task 3: Register "Tank" template (Type=100)
            var tank = new TkbTemplate("Tank", 100);
            
            // Add Components with defaults
            tank.AddComponent(new NetworkIdentity()); // Value set by replicator
            tank.AddComponent(new NetworkTransform());
            tank.AddComponent(new NetworkVelocity());
            
            // HARD REQUIREMENT: TkbIdentity must be present (set when EntityMaster arrives)
            tank.AddMandatoryComponent<TkbIdentity>(isHard: true);
            
            // SOFT REQUIREMENT: NetworkTransform (set when physics data arrives)
            tank.AddMandatoryComponent<NetworkTransform>(isHard: false);
            
            tkb.Register(tank);
        }
    }
}
