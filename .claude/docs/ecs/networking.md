# Networking and prediction

How component data and events reach the client, and how prediction interacts with them. Network events/`RaisePredictiveEvent` are also summarised in `systems-engine.md`.

1. Server mutates data and calls `Dirty` → component gets a new `LastModifiedTick`.
2. The PVS/game-state system sends each client the changed state of components on entities visible to that client (PVS range). Visibility can be limited with visibility masks/eye settings. Only components that are `[NetworkedComponent]` participate. `SessionSpecific` / `SendOnlyToOwner` components send per-player variants.
3. Server builds the state with `ComponentGetState` (raised by ref on the component; handler sets `args.State`; generated automatically with `[AutoGenerateComponentState]`). Client applies it with `ComponentHandleState` (`args.Current`/`args.Next`). If `[AutoGenerateComponentState(true)]` the generated handler also raises `AfterAutoHandleStateEvent` after applying — subscribe to it to refresh derived/cached data or visuals when replicated fields change.
4. The client **predicts**: shared system code runs on the client on the player's own input. Server states overwrite predicted data; the client rewinds, applies the new state, and re-runs unconfirmed inputs.
5. Prediction helpers (`IGameTiming`): `IsFirstTimePredicted` (guard sounds/popups/spawns so they don't repeat on every re-prediction), `InPrediction`, `ApplyingState` (true while applying a server state — don't raise side effects then). Client-only side effects typically check `_timing.IsFirstTimePredicted`; server-only effects check `_net.IsServer` (`INetManager`).
6. Client → server input that must be validated and predicted is sent with `RaisePredictiveEvent(ev)` (raised locally every re-prediction *and* sent to the server).
7. Fields that must not be trusted/leaked: keep them in server-only components (`Content.Server`), never `[AutoNetworkedField]`.
