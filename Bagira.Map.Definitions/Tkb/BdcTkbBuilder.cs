using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;

namespace Bagira.Map.Definitions.Tkb
{
    public class BdcTkbBuilder
    {
        private readonly TkbDatabase _db;
        
        public BdcTkbBuilder(TkbDatabase db)
        {
            _db = db;
        }
        
        /// <summary>
        /// Define new vehicle entity type.
        /// </summary>
        public BdcTkbBuilder DefineVehicle(long tkbId, string name)
        {
            var template = new TkbTemplate(name, tkbId);
            
            // Override: RegisterTemplate -> Register
            _db.Register(template);
            return this;
        }
        
        /// <summary>
        /// Add visual properties (IG).
        /// </summary>
        public BdcTkbBuilder WithVisual(long tkbId, Action<IgVisualDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            // Override: factory provides fresh instance
            template.AddManagedComponent(() => 
            {
                var visualDef = new IgVisualDef();
                configure(visualDef);
                return visualDef;
            });
            return this;
        }
        
        /// <summary>
        /// Add physics properties (SimHost).
        /// </summary>
        public BdcTkbBuilder WithPhysics(long tkbId, Action<SimVehicleDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            template.AddManagedComponent(() => 
            {
                var physicsDef = new SimVehicleDef();
                configure(physicsDef);
                return physicsDef;
            });
            return this;
        }
        
        /// <summary>
        /// Add combat properties (future).
        /// </summary>
        public BdcTkbBuilder WithCombat(long tkbId, Action<SimCombatDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            template.AddManagedComponent(() => 
            {
                var combatDef = new SimCombatDef();
                configure(combatDef);
                return combatDef;
            });
            return this;
        }
        
        /// <summary>
        /// Add composite (ORBAT) definition.
        /// </summary>
        public BdcTkbBuilder AsComposite(long tkbId, Action<TkbCompositionDef> configure)
        {
            var template = _db.GetByType(tkbId);
            if (template == null)
                throw new InvalidOperationException($"Template {tkbId} not found");
            
            template.AddManagedComponent(() => 
            {
                var compositionDef = new TkbCompositionDef();
                configure(compositionDef);
                return compositionDef;
            });
            return this;
        }
    }
}
