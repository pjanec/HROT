// Types moved to Hrot.Common.Events so both SimHost and ExCon can reference them
// without a cross-dependency.  Re-export via global using for backward compat.
global using MissionControlIntent   = Hrot.Common.Events.MissionControlIntent;
global using MissionControlAckEvent = Hrot.Common.Events.MissionControlAckEvent;
