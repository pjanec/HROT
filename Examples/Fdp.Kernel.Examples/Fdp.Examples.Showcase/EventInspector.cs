using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Kernel;
using ImGuiNET;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase
{
    /// <summary>
    /// Event inspector for debugging event bus activity using ImGui.
    /// Shows both unmanaged and managed events from current and previous frames.
    /// </summary>
    public class EventInspector
    {
        private readonly FdpEventBus _eventBus;
        
        // Event history tracking
        // Event history tracking
        private List<EventRecord> _currentFrameEvents = new();
        private List<EventRecord> _previousFrameEvents = new();
        private EventRecord? _selectedEvent = null;
        
        public EventInspector(FdpEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }
        
        public void CaptureFrameEvents()
        {
            // Move current to previous
            _previousFrameEvents = _currentFrameEvents;
            _currentFrameEvents = new List<EventRecord>();
            
            // Iterate all inspectors
            foreach (var inspector in _eventBus.GetDebugInspectors())
            {
                if (inspector.Count == 0) continue;

                bool isManaged = !inspector.EventType.IsValueType;
                
                foreach (var evt in inspector.InspectReadBuffer())
                {
                    _currentFrameEvents.Add(new EventRecord
                    {
                        TypeName = inspector.EventType.Name + (isManaged ? " (Managed)" : ""),
                        IsManaged = isManaged,
                        Summary = GetEventSummary(evt),
                        Details = GetEventDetails(evt, inspector.EventType)
                    });
                }
            }
        }
        
        public void DrawImGui()
        {
            // Position the inspector window on the right side, below entity inspector
            ImGui.SetNextWindowPos(new Vector2(10, 600), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(600, 470), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Event Inspector", ImGuiWindowFlags.NoCollapse))
            {
                // Stats header
                ImGui.TextColored(new Vector4(1, 1, 0, 1), 
                    $"Events - Current: {_currentFrameEvents.Count}, Previous: {_previousFrameEvents.Count}");
                ImGui.Separator();
                
                // Two-panel layout: Event list on left, details on right
                if (ImGui.BeginTable("EventInspectorLayout", 2, ImGuiTableFlags.Resizable))
                {
                    ImGui.TableSetupColumn("Events", ImGuiTableColumnFlags.WidthFixed, 300);
                    ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
                    
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    DrawEventListPanel();
                    
                    ImGui.TableSetColumnIndex(1);
                    DrawEventDetailsPanel();
                    
                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }
        
        private void DrawEventListPanel()
        {
            ImGui.TextColored(new Vector4(0, 1, 1, 1), "Current Frame");
            ImGui.BeginChild("CurrentFrameEvents", new Vector2(0, 180));
            
            if (_currentFrameEvents.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No events this frame");
            }
            else
            {
                for (int i = 0; i < _currentFrameEvents.Count; i++)
                {
                    var evt = _currentFrameEvents[i];
                    bool isSelected = (evt == _selectedEvent);
                    
                    var color = evt.IsManaged 
                        ? new Vector4(0.5f, 1f, 0.5f, 1f)  // Green for managed
                        : new Vector4(1f, 1f, 1f, 1f);      // White for unmanaged
                    
                    if (isSelected)
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
                    else
                        ImGui.PushStyleColor(ImGuiCol.Text, color);
                    
                    if (ImGui.Selectable($"{evt.TypeName}##current{i}", isSelected))
                        _selectedEvent = evt;
                    
                    ImGui.PopStyleColor();
                    
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(evt.Summary);
                }
            }
            
            ImGui.EndChild();
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Previous Frame");
            ImGui.BeginChild("PreviousFrameEvents", new Vector2(0, 180));
            
            if (_previousFrameEvents.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No events");
            }
            else
            {
                for (int i = 0; i < _previousFrameEvents.Count; i++)
                {
                    var evt = _previousFrameEvents[i];
                    bool isSelected = (evt == _selectedEvent);
                    
                    var color = evt.IsManaged 
                        ? new Vector4(0.4f, 0.7f, 0.4f, 1f)  // Dimmed green
                        : new Vector4(0.7f, 0.7f, 0.7f, 1f); // Dimmed white
                    
                    if (isSelected)
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
                    else
                        ImGui.PushStyleColor(ImGuiCol.Text, color);
                    
                    if (ImGui.Selectable($"{evt.TypeName}##prev{i}", isSelected))
                        _selectedEvent = evt;
                        
                    ImGui.PopStyleColor();
                    
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(evt.Summary);
                }
            }
            
            ImGui.EndChild();
        }
        
        private void DrawEventDetailsPanel()
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Event Details");
            ImGui.BeginChild("EventDetails", new Vector2(0, 0));
            
            if (_selectedEvent == null)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No event selected");
            }
            else
            {
                var evt = _selectedEvent;
                
                ImGui.TextColored(new Vector4(0, 1, 1, 1), evt.TypeName);
                ImGui.Separator();
                
                ImGui.TextWrapped(evt.Summary);
                ImGui.Spacing();
                ImGui.Separator();
                
                if (ImGui.BeginTable("EventDetailsTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();
                    
                    foreach (var detail in evt.Details)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextColored(new Vector4(0, 1, 1, 1), detail.Key);
                        ImGui.TableSetColumnIndex(1);
                        ImGui.TextWrapped(detail.Value);
                    }
                    
                    ImGui.EndTable();
                }
            }
            
            ImGui.EndChild();
        }
        
        private string GetEventSummary(object evt)
        {
            return evt switch
            {
                CollisionEvent collision => $"Collision: Entity {collision.EntityA.Index} <-> {collision.EntityB.Index}",
                ProjectileFiredEvent fired => $"Projectile fired by {fired.ShooterType} [{fired.Shooter.Index}]",
                ProjectileHitEvent hit => $"Projectile hit target [{hit.Target.Index}] for {hit.Damage:F1} damage",
                DamageEvent dmg => $"{dmg.AttackerType} [{dmg.Attacker.Index}] damaged [{dmg.Target.Index}] for {dmg.Damage:F1}",
                DeathEvent death => $"{death.Type} [{death.Entity.Index}] died",
                EntityDamagedEvent mDmg => mDmg.ToString() ?? "EntityDamagedEvent",
                EntityDeathEvent mDeath => mDeath.ToString() ?? "EntityDeathEvent",
                _ => evt?.ToString() ?? "Unknown event"
            };
        }
        
        private Dictionary<string, string> GetEventDetails(object evt, Type type)
        {
            var details = new Dictionary<string, string>();
            if (evt == null) return details;
            
            // Unmanaged events use Fields (structs)
            if (type.IsValueType)
            {
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    var value = field.GetValue(evt);
                    details[field.Name] = value?.ToString() ?? "null";
                }
            }
            // Managed events use Properties (classes)
            else
            {
                foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        // Skip IgnoreMember properties for cleaner view
                        if (prop.GetCustomAttributes(typeof(MessagePack.IgnoreMemberAttribute), true).Length > 0)
                            continue;
                        
                        var value = prop.GetValue(evt);
                        details[prop.Name] = value?.ToString() ?? "null";
                    }
                    catch
                    {
                        details[prop.Name] = "<error>";
                    }
                }
            }
            
            return details;
        }
        
        private class EventRecord
        {
            public string TypeName { get; set; } = "";
            public bool IsManaged { get; set; }
            public string Summary { get; set; } = "";
            public Dictionary<string, string> Details { get; set; } = new();
        }
    }
}
