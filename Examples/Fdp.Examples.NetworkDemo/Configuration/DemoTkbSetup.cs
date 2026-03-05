using System;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Replication.Components;
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
            
            // Task: Descriptors
            tank.MandatoryDescriptors.Add(new MandatoryDescriptor 
            {
                 PackedKey = PackedKey.Create(DemoDescriptors.Master, 0),
                 IsHard = true
            });
            
            tank.MandatoryDescriptors.Add(new MandatoryDescriptor 
            {
                 PackedKey = PackedKey.Create(DemoDescriptors.Physics, 0),
                 IsHard = false
            });
            
            tkb.Register(tank);
        }
    }
}
