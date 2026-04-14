using System;
using CycloneDDS.Runtime;
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.Interfaces;
using Fdp.Kernel; // For IEventBus probably

namespace Fdp.ModuleHost.Network.Cyclone.Translators
{
    /// <summary>
    /// High-performance translator for pure data events (no entity references).
    /// Examples: SwitchTimeModeEvent, WeatherChange, GameStart.
    /// </summary>
    public class BlitEventTranslator<T> where T : unmanaged
    {
        protected readonly DdsReader<T> Reader;
        protected readonly DdsWriter<T> Writer;

        public BlitEventTranslator(DdsParticipant participant, string topicName)
        {
            Reader = new DdsReader<T>(participant);
            Writer = new DdsWriter<T>(participant);
        }

        /// <summary>
        /// Ingress: Read DDS events and publish to ECS event bus (1:1 copy).
        /// </summary>
        public void PollIngress(IEventBus bus)
        {
            using var loan = Reader.Take();
            
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;

                // Direct memory copy (zero allocation, zero transformation)
                T eventData = sample.Data;
                bus.Publish(eventData);
            }
        }

        /// <summary>
        /// Egress: Consume ECS events and send to DDS (1:1 copy).
        /// </summary>
        public void ScanAndPublish(ISimulationView view)
        {
            // Assuming ConsumeEvents exists on ISimulationView and returns Span or similar
            // Requires checks on ISimulationView
            var events = view.ConsumeEvents<T>();
            
            foreach (ref readonly var evt in events)
            {
                // Direct memory copy (zero allocation, zero transformation)
                Writer.Write(evt);
            }
        }
    }
}
