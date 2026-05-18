using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Fdp.Kernel;
using ImGuiNET;
using Fdp.Examples.Showcase.Components;

namespace Fdp.Examples.Showcase
{
    /// <summary>
    /// Interactive entity inspector for debugging using ImGui.
    /// Shows entities in 3 panels: Top (entity list), Middle (component list), Bottom (component details).
    /// </summary>
    public class EntityInspector
    {
        private readonly EntityRepository _repo;
        
        // Selection state
        private List<Entity> _entities = new();
        private int _selectedEntityIndex = 0;
        private List<ComponentInfo> _components = new();
        private int _selectedComponentIndex = 0;
        
        public EntityInspector(EntityRepository repo)
        {
            _repo = repo;
        }
        
        public void Update()
        {
            // Refresh entity list
            _entities.Clear();
            var index = _repo.GetEntityIndex();
            for (int i = 0; i <= index.MaxIssuedIndex; i++)
            {
                ref var header = ref index.GetHeader(i);
                if (header.IsActive)
                {
                    _entities.Add(new Entity(i, header.Generation));
                }
            }
            
            // Clamp selection
            if (_selectedEntityIndex >= _entities.Count)
                _selectedEntityIndex = Math.Max(0, _entities.Count - 1);
            
            // Refresh component list for selected entity
            if (_entities.Count > 0 && _selectedEntityIndex < _entities.Count)
            {
                var selectedEntity = _entities[_selectedEntityIndex];
                _components = GetComponentsForEntity(selectedEntity);
                
                if (_selectedComponentIndex >= _components.Count)
                    _selectedComponentIndex = Math.Max(0, _components.Count - 1);
            }
            else
            {
                _components.Clear();
            }
        }
        
        public void DrawImGui()
        {
            Update(); // Refresh data before drawing
            
            // Position the inspector window on the right side
            ImGui.SetNextWindowPos(new Vector2(1920 - 510, 10), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(500, 1060), ImGuiCond.FirstUseEver);
            
            if (ImGui.Begin("Entity Inspector", ImGuiWindowFlags.NoCollapse))
            {
                // TOP PANEL: Entity List
                DrawEntityListPanel();
                
                ImGui.Separator();
                
                // MIDDLE PANEL: Component List
                DrawComponentListPanel();
                
                ImGui.Separator();
                
                // BOTTOM PANEL: Component Details
                DrawComponentDetailsPanel();
            }
            ImGui.End();
        }
        
        private void DrawEntityListPanel()
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Entities ({_entities.Count})");
            ImGui.BeginChild("EntityListChild", new Vector2(0, 300));
            
            if (ImGui.BeginTable("EntityTable", 5,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Gen", ImGuiTableColumnFlags.WidthFixed, 40);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Health", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableHeadersRow();
                
                for (int i = 0; i < _entities.Count; i++)
                {
                    var entity = _entities[i];
                    bool isSelected = (i == _selectedEntityIndex);
                    
                    ImGui.TableNextRow();
                    
                    if (isSelected)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.5f, 1)));
                    
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable($"{entity.Index}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                        _selectedEntityIndex = i;
                    
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text($"{entity.Generation}");
                    
                    ImGui.TableSetColumnIndex(2);
                    ImGui.Text(GetEntityType(entity));
                    
                    ImGui.TableSetColumnIndex(3);
                    ImGui.Text(GetEntityHealth(entity));
                    
                    ImGui.TableSetColumnIndex(4);
                    ImGui.Text(GetEntityPosition(entity));
                }
                
                ImGui.EndTable();
            }
            
            ImGui.EndChild();
        }
        
        private void DrawComponentListPanel()
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Components ({_components.Count})");
            ImGui.BeginChild("ComponentListChild", new Vector2(0, 250));
            
            if (_entities.Count == 0 || _selectedEntityIndex >= _entities.Count)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No entity selected");
            }
            else if (ImGui.BeginTable("ComponentTable", 2,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn("Component", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Summary", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();
                
                for (int i = 0; i < _components.Count; i++)
                {
                    var comp = _components[i];
                    bool isSelected = (i == _selectedComponentIndex);
                    
                    ImGui.TableNextRow();
                    
                    if (isSelected)
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.5f, 1)));
                    
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable(comp.TypeName, isSelected, ImGuiSelectableFlags.SpanAllColumns))
                        _selectedComponentIndex = i;
                    
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(comp.Summary);
                }
                
                ImGui.EndTable();
            }
            
            ImGui.EndChild();
        }
        
        private void DrawComponentDetailsPanel()
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Component Details");
            ImGui.BeginChild("ComponentDetailsChild", new Vector2(0, 0));
            
            if (_components.Count == 0 || _selectedComponentIndex >= _components.Count)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "No component selected");
            }
            else
            {
                var comp = _components[_selectedComponentIndex];
                
                ImGui.TextColored(new Vector4(0, 1, 1, 1), comp.TypeName);
                ImGui.Separator();
                
                if (ImGui.BeginTable("PropertiesTable", 2,
                    ImGuiTableFlags.Borders |
                    ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();
                    
                    foreach (var prop in comp.Properties)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextColored(new Vector4(0, 1, 1, 1), prop.Key);
                        ImGui.TableSetColumnIndex(1);
                        ImGui.Text(prop.Value);
                    }
                    
                    ImGui.EndTable();
                }
            }
            
            ImGui.EndChild();
        }
        
        private string GetEntityType(Entity entity)
        {
            if (_repo.HasComponent<UnitStats>(entity))
            {
                ref readonly var stats = ref _repo.GetComponentRO<UnitStats>(entity);
                return stats.Type.ToString();
            }
            return "Unknown";
        }
        
        private string GetEntityHealth(Entity entity)
        {
            if (_repo.HasComponent<UnitStats>(entity))
            {
                ref readonly var stats = ref _repo.GetComponentRO<UnitStats>(entity);
                return $"{stats.Health:F0}";
            }
            return "-";
        }
        
        private string GetEntityPosition(Entity entity)
        {
            if (_repo.HasComponent<Position>(entity))
            {
                ref readonly var pos = ref _repo.GetComponentRO<Position>(entity);
                return $"({pos.X:F1}, {pos.Y:F1})";
            }
            return "-";
        }
        
        private List<ComponentInfo> GetComponentsForEntity(Entity entity)
        {
            var result = new List<ComponentInfo>();
            
            // Check all known unmanaged component types
            CheckComponent<Position>(entity, result);
            CheckComponent<Velocity>(entity, result);
            CheckComponent<RenderSymbol>(entity, result);
            CheckComponent<UnitStats>(entity, result);
            CheckComponent<Projectile>(entity, result);
            CheckComponent<Particle>(entity, result);
            CheckComponent<HitFlash>(entity, result);
            CheckComponent<Corpse>(entity, result);
            
            // Check managed component types
            CheckManagedComponent<CombatHistory>(entity, result);
            
            return result;
        }
        
        private void CheckComponent<T>(Entity entity, List<ComponentInfo> list) where T : struct
        {
            if (_repo.HasComponent<T>(entity))
            {
                ref readonly var comp = ref _repo.GetComponentRO<T>(entity);
                var info = new ComponentInfo
                {
                    TypeName = typeof(T).Name,
                    Summary = GetComponentSummary(comp),
                    Properties = GetComponentProperties(comp)
                };
                list.Add(info);
            }
        }
        
        private void CheckManagedComponent<T>(Entity entity, List<ComponentInfo> list) where T : class
        {
            if (_repo.HasComponent<T>(entity))
            {
                var comp = _repo.GetComponentRO<T>(entity);
                var info = new ComponentInfo
                {
                    TypeName = typeof(T).Name + " (Managed)",
                    Summary = GetManagedComponentSummary(comp),
                    Properties = GetManagedComponentProperties(comp)
                };
                list.Add(info);
            }
        }
        
        private string GetComponentSummary<T>(in T component)
        {
            return component switch
            {
                Position pos => $"({pos.X:F1}, {pos.Y:F1})",
                Velocity vel => $"({vel.X:F1}, {vel.Y:F1})",
                RenderSymbol sym => $"{sym.Shape} RGB({sym.R},{sym.G},{sym.B})",
                UnitStats stats => $"{stats.Type} HP:{stats.Health:F0}/{stats.MaxHealth:F0}",
                Projectile proj => $"Life:{proj.Lifetime:F1}s",
                Particle part => $"Life:{part.LifeRemaining:F1}s",
                HitFlash flash => $"Remaining:{flash.Remaining:F2}",
                Corpse corpse => $"Ttl:{corpse.TimeRemaining:F1}s",
                _ => ""
            };
        }
        
        private string GetManagedComponentSummary<T>(T component) where T : class
        {
            return component switch
            {
                CombatHistory history => $"Dmg: {history.TotalDamageTaken}/{history.TotalDamageDealt}, Kills: {history.Kills}",
                _ => component?.ToString() ?? "null"
            };
        }
        
        private Dictionary<string, string> GetComponentProperties<T>(in T component)
        {
            var props = new Dictionary<string, string>();
            var type = typeof(T);
            
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var value = field.GetValue(component);
                props[field.Name] = value?.ToString() ?? "null";
            }
            
            return props;
        }
        
        private Dictionary<string, string> GetManagedComponentProperties<T>(T component) where T : class
        {
            var props = new Dictionary<string, string>();
            if (component == null) return props;
            
            var type = typeof(T);
            
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    var value = prop.GetValue(component);
                    if (value is System.Collections.IEnumerable enumerable && !(value is string))
                    {
                        var items = new List<string>();
                        foreach (var item in enumerable)
                        {
                            items.Add(item?.ToString() ?? "null");
                            if (items.Count >= 10) break; // Limit to 10 items
                        }
                        props[prop.Name] = $"[{string.Join(", ", items)}]";
                    }
                    else
                    {
                        props[prop.Name] = value?.ToString() ?? "null";
                    }
                }
                catch
                {
                    props[prop.Name] = "<error>";
                }
            }
            
            return props;
        }
        
        private class ComponentInfo
        {
            public string TypeName { get; set; } = "";
            public string Summary { get; set; } = "";
            public Dictionary<string, string> Properties { get; set; } = new();
        }
    }
}
