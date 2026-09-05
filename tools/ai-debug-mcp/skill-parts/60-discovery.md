---

## 6. Discover before you guess

The API is self-describing — prefer discovery over assumptions:
- `list_commands` before `send_entity_command`
- `list_component_types` before `edit_component`
- `get_attributes_schema` before `patch_attribute`
- `list_entity_types` before `spawn_entity`
- `get_status` / `get_sim_state` whenever a command "did nothing" — you are probably in the wrong run state.
