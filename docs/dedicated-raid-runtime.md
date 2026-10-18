# Server-authoritative raid runtime

## Goal

Replace player-hosted and headless-client-hosted raids with independent, server-owned raid instances. A single SPT/Fika server should be able to run multiple instances of different maps simultaneously. No game client should be required to keep an instance alive.

## Verified existing code

- `FikaServer/Services/MatchService.cs` owns a concurrent match dictionary, creates location/loot data, tracks players, and handles match timeouts.
- `FikaServer/Controllers/RaidController.cs` exposes create, join, leave, gethost, and getsettings. `gethost` currently returns an IP/port registered by a game host.
- `Fika.Core/Main/GameMode/HostGameController.cs` uses EFT/Unity game-world classes, bot controllers, spawn scenarios, weather, and game networking. Those are **not** available in the ASP.NET SPT server process.
- `Fika.Core/UI/Custom/MatchMakerUIScript.cs` opens the Session Settings popup and starts either a player-hosted raid or a headless-client raid.

## Target flow

1. Client selects a location and presses **START RAID**.
2. Client sends a request to the server, including map and raid settings.
3. Server validates the player, squad, map, capacity, and request.
4. RaidInstanceManager allocates a unique raid ID and an isolated network endpoint; starts a dedicated simulation worker.
5. Worker loads authoritative map, loot, AI, physics/combat and raid clock; reports READY.
6. Server returns the endpoint and session token. Clients connect as participants, never as hosts.
7. Worker sends authoritative state updates. Server validates inventory, loot, extraction, and persistence.
8. When the last player leaves, the instance enters a configurable grace period, saves state, then shuts down.
9. Multiple raid workers run concurrently, with separate world state, resource budgets, and ports.

## Implementation milestones

1. **Instance lifecycle:** Add a registry with Starting/Ready/Running/Draining/Stopped/Failed states, per-map limits, port allocation, heartbeat, and shutdown. Do not change existing match creation until a worker can report READY.
2. **Dedicated simulation worker:** Separate process, without graphical output, that can actually load and tick EFT world simulation. Investigate feasibility of loading EFT/Unity runtime without a graphical client; if impossible, a new simulation implementation is required. A plain .NET HTTP server cannot execute the current `HostGameController`.
3. **Networking:** Adapt Fika host packets and connect/join responses to target the worker; authenticate players and ensure isolated raid sessions.
4. **Persistence:** Server-authoritative loot, combat, death, extraction, inventory, and reconnect handling.
5. **UI migration:** Rename HOST RAID to START RAID and remove the Session Settings/headless toggle only once the server-start path is operational. Retain the current host path until that point.
6. **Retirement:** Remove headless-client dependencies after multi-map and multiplayer tests pass.

## Required tests

- Two different maps running concurrently, and two instances of the same map.
- Player disconnect/reconnect and squad join while raid is running.
- AI, loot, damage, weather, extracts, and save persistence independent of all players.
- Instance crashes, timeouts, idle shutdown, restart recovery, and port reuse.
- A player starting a raid never becomes the authoritative network host.

## Non-goal for initial milestone

Do not pretend that allocating a match record in `MatchService` creates a dedicated raid. The worker and authoritative simulation must exist before the new START RAID flow is enabled.

## Capacity and hot-join fallback policy

Default settings: `concurrentServer: 1`, `restartAfterRaid: true`, `maxPlayersPerRaid: 5`, `maxHotJoinTime: 120` (seconds), `setLevelToAverageOfLobby: true`.

- The hot-join timer starts when the raid simulation transitions to RUNNING, not when a user clicks START RAID or while the worker loads.
- Joining an active raid is allowed only while elapsed running time is at most `maxHotJoinTime` and player capacity is available. Use authoritative server timestamps.
- If all dedicated instances are occupied, or the available raid's hot-join window has expired, offer **Play Solo** for the requested map instead of silently joining a different raid.
- **Play Solo** must be an explicit client choice, not a hidden fallback. Solo play must not reserve a dedicated instance or expose a public Fika raid.
- A player who started a raid before the hot-join deadline but is still loading must have a server-side admission reservation, with expiry, so they are not rejected after the window closes.
- A group must not be silently split into individual solo sessions. Inform players and let them choose to wait, join an eligible session, or play solo.
- Apply existing SPT solo progression/save rules; never grant multiplayer rewards based solely on a client claim.
- Enforce the capacity check atomically when allocating instances or accepting join reservations; simultaneous requests must not exceed `concurrentServer` or `maxPlayersPerRaid`.

These are design requirements. The fallback UI, reservations, server capacity enforcement, and dedicated simulation worker are not yet implemented.
