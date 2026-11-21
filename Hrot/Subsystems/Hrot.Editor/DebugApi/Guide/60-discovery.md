---

## 6. Discover before you guess

The API is self-describing — prefer discovery over assumptions:
- `GET /commands` before `POST /entities/command`
- `GET /components` before `POST /entities/{networkId}/component`
- `GET /attributes/schema` before `POST /entities/{networkId}/attribute`
- `GET /tkb/types` before `POST /entities/spawn`
- `GET /status` / `GET /sim/state` whenever a command "did nothing" — you are probably in the wrong run state.
