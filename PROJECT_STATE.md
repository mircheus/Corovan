# Corovan — Project State

> Snapshot of implemented systems and mechanics. Last reviewed: **2026-05-12**.
> For architecture conventions and lessons, see `CLAUDE.md`. This doc focuses on *what exists right now*.

---

## 1. Gameplay Overview

Corovan is a **multiplayer convoy FPS** on Unity 6 with Mirror networking. Players join a host (peer-to-peer over ZeroTier), spawn on foot, and play around a vehicle convoy on an endlessly scrolling road.

**The loop that works today:**
1. Join via the network UI → spawn at a rotated spawn point.
2. Move/shoot on foot (FPS controller, multi-weapon, ammo/reload, melee + ranged).
3. Walk up to the convoy car, press **E** to drive it (client authority transfer).
4. Pick up loot props (carryables), carry them into the car's **cargo trigger** → score goes up.
5. Survive turret enemies and rolling barrel hazards that damage you and the car.
6. Optional: destruction objectives (destroy N targets → fire an event).

**Supporting / experimental mechanics present in code:** zombie melee enemies, a grappling hook, a floor-cutting tool + safe that falls through the hole, wind zones, kill volumes, vehicle remote control (in progress).

**Scenes** (`Assets/Game/Scenes/`):
| Scene | Role |
|---|---|
| `Road.unity` | **Main gameplay** — infinite road, convoy, enemies, barrels |
| `JoinMenu.unity` | Lobby / join flow |
| `FloorCutter.unity` | Floor-cutting tool test bed |
| `GrapplingHook_Test.unity` | Grappling hook test bed (untracked) |
| `Wall Laser.unity` | WIP (untracked) |
| `WagonsConstruct.unity` | WIP (untracked) |
| `Test.unity` | Scratch scene (untracked) |
| `SampleScene.unity` | Placeholder |

---

## 2. System Status at a Glance

| System | State | Notes |
|---|---|---|
| Player movement / camera | ✅ Working | Walk/sprint/crouch/jump, head bob, ADS FOV, recoil |
| Weapons (ranged + melee) | ✅ Working | Multi-slot, per-slot ammo, reload, optimistic switch |
| Health / damage / death | ✅ Working | SyncVar health, regen, death state — **respawn not wired to scene** |
| Carry / throw loot | ✅ Working | Client-authority pickup; drop/throw |
| Vehicle driving | ✅ Working | Arcade `MovePosition`, authority transfer, vehicle health + explosion FX |
| Vehicle **remote control** | 🚧 In progress | Scripts done; not yet wired into Player prefab (see §4) |
| Cargo / scoring | ✅ Working | `CargoTrigger` → `ScoreManager` SyncVar |
| Turret enemies | ✅ Working | Detect player/car, rotate, fire projectiles — no death visuals |
| Barrel hazards | ✅ Working | Server pool + timed spawn, curve bounce, damages car/player |
| Destruction objective | ✅ Working | `DestructionObjective` tracks `DestructionTarget`s |
| Interactables (hold-E) | ✅ Working | Generic; binds UnityEvents (doors etc.) |
| Wind zones / kill volumes | ✅ Working | Trigger-based push / instant kill |
| Floor cutter + falling safe | ✅ Working (tool) | Runtime mesh hole + point-in-quad fall detection |
| Zombie enemies | ⚠️ Coded, not placed | Full NavMesh state machine — not spawned in any scene |
| Grappling hook | ⚠️ Local-only prototype | No networking; placeholder visuals |
| UI (HUD/scoreboard/pause/settings/gameover) | ◑ Mostly working | Several buttons stubbed (see §3 UI) |
| Net message structs | ❌ Not integrated | Declared in `NetworkMessages.cs`, unused |
| Game settings | ◑ Local only | Applied client-side; not networked |
| Win / lose conditions | ❌ Missing | No match flow |

Legend: ✅ done · ◑ partial · ⚠️ exists but not integrated · 🚧 actively being built · ❌ absent

---

## 3. Systems in Detail

### Player (`Assets/Game/Scripts/Player/`)
- **PlayerInputHandler** — sole input router (Input System *Send Messages*: `OnMove/OnLook/OnJump/OnCrouch/OnAttack/OnAim/OnInteract/OnReload/OnPrevious/OnNext` + slot keys 1–9). Handles vehicle enter/exit (proximity search), hold-E progress for interactables, carry/throw gating, and the post-throw aim block. Commands: `CmdEnterVehicle`, `CmdExitVehicle`, `CmdDetachSafe`, `CmdInteract`. Disables `PlayerInput` on remote players in `OnStartClient`.
- **PlayerController** — `CharacterController` FPS movement (walk 5 / sprint 8.5 / crouch 2.5 m/s, jump, air control, slope/step handling, gravity ramp, crouch height blend, vehicle jump boost). `SetVehicleMode(bool)` zeroes velocity + disables the controller. Exposes `AddExternalVelocity` (knockback / wind) and `IsGrappling`. Disabled on remote players (NetworkTransform drives them).
- **PlayerCamera** — pitch/yaw clamp ±85°, FOV blend (base 90 / sprint 100 / ADS 55), head bob, recoil accumulate+recover, death camera drop. API: `AddRecoil`, `SetLookInput`, `SetAiming`, `GetAimRay`, `Lock/UnlockCursor`, `SetSensitivity`. Local-owner only.
- **PlayerShooting** — multi-weapon state machine. Per-slot `WeaponData[]` + `weaponModels[]` + optional `weaponTools[]` (per-slot `Behaviour` enabled only on local owner when that slot is active). SyncVars: `currentAmmo` (hook), `reserveAmmo`, `isReloading`, `currentWeaponIndex` (hook). Ranged = server raycast; melee = 0.5 m SphereCast. Optimistic local weapon switch → `CmdSwitchWeapon` → SyncVar confirm. Per-slot ammo saved/restored server-side. RPCs: `RpcOnFire` (muzzle flash), `RpcOnHit` (impact FX). Recognizes hits on `PlayerHealth`, `Enemy`, `ZombieEnemy`, `DestroyableObject`, `DestructionTarget`. Events for HUD.
- **PlayerHealth** — `currentHealth` (hook) + `isDead` SyncVars; optional server-side regen. Server API: `TakeDamage(float)`, `TakeDamage(float, hitPoint, hitDir)`, `Heal`, `SetHealth`, `Respawn`, `ResetHealth`; `CmdTakeDamage` for client-originated damage. RPCs `RpcOnDeath`/`RpcOnRespawn`. Events `OnHealthChanged/OnDeath/OnRespawn`. **`Respawn()` has no caller in scene.**
- **PlayerCarry** — pickup within ~10 m, attaches to a carry point, blocks shooting/aiming via `SetCarryingMode`. Commands `CmdPickup/CmdDrop/CmdThrow`; throw applies camera-ray impulse. Local parenting is instant; SyncVar confirms on others.
- **RemoteVehicleControlTool** *(new — see §4)* — `NetworkBehaviour` tool component for the `weaponTools[]` slot. First fire on a car = bind it; later fires = `CmdCommandAutopilot(carNetId, point)` → `CarController.ServerStartAutopilot`. L-path preview LineRenderer; cooldown gate; bind/command refused if the car has a driver or is destroyed (validated client-side via SyncVars, re-validated server-side on the command).
- **LocalPlayerBodyHider** — moves the local player's body renderers to a layer the FPS camera doesn't cull (optional shadows-only mode). Runs on `OnStartLocalPlayer`. Needs the layer set up in project settings.
- **GrapplingHookInput** — caches `Attack`/`Jump` action states as per-frame bools for the grapple controller.
- **GrapplingHookController** — *local-only prototype.* Idle→Grappling states, 30 m aim raycast, transparent sphere placeholder, fly-to-target at 20 m/s, jump cancels, sets `PlayerController.IsGrappling`. **No networking** — won't work in multiplayer yet.

### Weapons (`Assets/Game/Scripts/Weapons/`)
- **WeaponData** — ScriptableObject: name/icon, `isMelee`/`isAutomatic`, fire rate, damage, range, melee radius, spread (base / ADS×0.3 / movement×1.5), magazine + reserve, reload time, recoil pattern (Vector2[]) + multipliers, ADS time/position, sounds, prefab/muzzle/impact effects. `GetRecoilAtIndex(int)` clamps to the pattern.
- **WeaponBase** — abstract weapon base (ammo/reload/fire-rate/spread/recoil helpers, `CanFire`). **Not currently subclassed** — `PlayerShooting` does firing inline; this is scaffolding for a future refactor.

### Vehicle (`Assets/Game/Scripts/Vehicle/`)
- **CarController** — arcade car `NetworkBehaviour`. `rb.MovePosition()` in `FixedUpdate`, velocity tracked in `currentVelocity` (never `rb.linearVelocity` — net sync overwrites it). `driverNetId` SyncVar w/ hook hides the driver model + flips `PlayerController.SetVehicleMode` on all clients. Enter/exit assigns/removes client authority. `vehicleHealth` + `vehicleIsAlive` SyncVars; `TakeDamage(int)` → `StartDestruction()` → fire FX → `ServerExplode` (kills the driver) → explosion FX, renderers/colliders off. Collision damage via `OnCollisionEnter` → `CmdReportCollision`. Driverless cars roll slowly backward once driven. **Autopilot (new):** `ServerStartAutopilot(Vector3)` / `ServerStopAutopilot()`; `autopilotActive`/`autopilotTarget`/`autopilotPhase` SyncVars; `ServerTickAutopilot` drives an L-shaped path (phase 0 = align world X, phase 1 = align world Z, threshold `autopilotArriveThreshold`). Autopilot cancels on damage, driver entering, and destruction. *Missing:* no destruction visual beyond the explosion FX / `vehicleIsAlive` flag.
- **CargoTrigger** — child trigger (no Rigidbody) on the car; a `CarryableObject` entering it → `ServerDepositLoot` → `NetworkServer.Destroy` + `ScoreManager.AddScore(value)`. Server-only.
- **SafeMount** — child trigger that auto-attaches a `Safe` entering it (`Safe.ServerAttach`) to an anchor; `ServerDetach` releases it. Server-only.

### Networking (`Assets/Game/Scripts/Networking/`)
- **CustomNetworkManager** — extends Mirror's `NetworkManager`; spawn-point cycling, scene-reload recovery (clears stale refs, re-fetches from `PlayerSpawnManager`), fallback chain custom array → `NetworkStartPosition` → manager transform.
- **PlayerSpawnManager** — spawn modes `RoundRobin` / `Random` / `FurthestFromPlayers` (uses the `Player` tag at runtime). *Known gap:* `FurthestFromPlayers` ignores `minSpawnDistance`.
- **ScoreManager** — singleton `NetworkBehaviour`; `totalScore` SyncVar w/ hook → `OnScoreChanged` event; `AddScore(int)` server-side.
- **NetworkDebugUI** — runtime overlay: Host/Client/Server buttons, address field (defaults to a ZeroTier IP), auto-hides once connected.
- **NetworkMessages** — struct declarations (`WeaponFireMessage`, `DamageMessage`, `PlayerDeathMessage`, `PlayerRespawnMessage`, `ChatMessage`, `MatchStateMessage` + `MatchState` enum, `ScoreUpdateMessage`). **Declared but not wired to anything.**

### Environment (`Assets/Game/Scripts/Environment/`)
- **Enemy** — turret `NetworkBehaviour`: SphereCollider trigger detection, server-only update loop rotates toward + fires `EnemyProjectile` (`NetworkServer.Spawn`) at a `PlayerHealth` (on foot) or `CarController` (driven). Health/alive SyncVars. *Gap:* `OnAliveChanged` hook empty — no death visuals.
- **EnemyProjectile** — server-spawned projectile; client-side travel, server validates the hit (damages `PlayerHealth` or `CarController`) then despawns; lifetime safety net.
- **ZombieEnemy** — **fully coded** NavMesh melee zombie: `Idle/Chasing/Attacking/Dead` SyncVar state machine, delayed-damage melee, `[ClientRpc]` hit effects, disables `NavMeshAgent` on clients, drops collider on death. **Not spawned in any scene yet.**
- **CarryableObject** — networked loot prop; pickup assigns client authority, kinematic + colliders off while carried; `SetCarriedLocally` for instant feel; SyncVar hook confirms. Has a `Value` used by `CargoTrigger`.
- **InteractableObject** — hold-E framework; server validates range + hold duration (timed in `PlayerInputHandler`), fires a `UnityEvent` on all clients via `[ClientRpc]`. No built-in effect — bind your own (e.g. `DoorOpener`).
- **DestroyableObject** — hit-count destructible; counter SyncVar; disables the GameObject at threshold. No FX/events.
- **DestructionTarget** — destructible that disables renderers/colliders and raises `OnTargetDestroyed` server-side for an objective to listen to.
- **DestructionObjective** — watches an array of `DestructionTarget`s; when the required count is destroyed, fires a `UnityEvent` on all clients via `[ClientRpc]`.
- **DoorOpener** — minimal: sets the GameObject inactive when opened; state SyncVar.
- **InfiniteRoad** — client-only tile recycler (scrolls + re-queues tiles); no networking.
- **KillVolume** — server-validated trigger: instantly kills players (`SetHealth(0)`), optionally damages cars.
- **WindZone** — trigger that calls `AddExternalVelocity` on the owned `PlayerController` during `TriggerStay`; preset-axis or custom direction, local/world toggle.

### Hazards (`Assets/Game/Scripts/Hazards/`)
- **Barrel** — pooled kinematic Rigidbody; server-side `Update` moves via `MovePosition` with an `AnimationCurve` Y-bounce; despawns after a lifetime; damages car (25) or on-foot player (15) on trigger contact, `[ClientRpc]` explosion FX, then deactivates.
- **BarrelSpawner** — server-only object pool (pre-spawned barrels reused on despawn); timed spawn loop with configurable delay.

### Tools (`Assets/Game/Scripts/Tools/`)
- **FloorCutter** — client-side 4-stroke hold-drag tool that records the corners of a cut quad with live `LineRenderer` previews; on the 4th stroke it auto-closes and executes the cut; off-floor release hard-resets. No undo. (See also `RemoteVehicleControlTool` for the `weaponTools[]` slot pattern.)
- **FloorMeshModifier** — runtime mesh surgery: rebuilds a floor mesh with a convex quad hole + perimeter band; auto-detects winding (can be forced); validates the hole is convex and in bounds; fires `OnHoleCreated(corners)`.
- **SafeFallTrigger** — listens to `FloorMeshModifier.OnHoleCreated`; if a safe's center is inside the cut quad (point-in-polygon XZ test), enables its gravity/physics so it drops through.
- **Safe** — networked destructible safe: can attach to a car (`carNetId` SyncVar) with locked orientation, detaches cleanly; while free-falling it scrolls backward on the ground layer (matching the conveyor illusion). *Gap:* no loot drop / visual state on destruction.

### UI (`Assets/Game/Scripts/UI/`)
- **HUD** — health bar + text (low-health color), ammo + reserve, reload indicator, crosshair (placeholder spread), interact progress bar, hit-marker flash, score readout. Wired to `PlayerHealth` / `PlayerShooting` / `PlayerInputHandler` events.
- **ScoreboardUI** — Tab-hold toggles the scoreboard panel.
- **PauseMenu** — ESC sets `Time.timeScale = 0`, toggles the panel, manages cursor lock. *Stubs:* `OpenSettings()` and `Disconnect()` are TODO.
- **SettingsMenu** — Controls/Video/Audio tabs; loads/saves `GameSettings` (sensitivity, invert Y, FOV, quality, vSync, volumes); `ResetToDefaults()` present.
- **GameOverUI** — listens to `PlayerHealth.OnDeath`, shows the panel + frees the cursor. *Stub:* `OnRestartButtonClick()` does nothing (should call `SceneReloader`).

### Config (`Assets/Game/Scripts/Config/`)
- **GameSettings** — ScriptableObject: mouse (sensitivity X/Y, invert Y), FOV (60–120), audio volumes (master/sfx/music), graphics (quality, vSync, target framerate), crosshair (color/size/thickness/dynamic spread), gameplay (head bob toggle + intensity). `ApplySettings()` applies quality/vSync/framerate/master volume. **Client-local only — not networked, not persisted per-client.**

### Animations / Utility / Editor
- **WheelAnimation** — sets `animator.speed` from a serialized `wheelSpeed` (vehicle wheel loops).
- **SceneReloader** — F9 hot-reload; picks `ServerChangeScene` vs `LoadScene` depending on connection state.
- **AutoTile** — `[ExecuteAlways]`; scales `mainTextureScale` from `transform.localScale` (walls use X/Y, floors X/Z).
- **AxisSpacer** — `[ExecuteAlways]` layout helper; distributes children along one axis with uniform spacing; cache/reset via context menu.
- **TestEvent** — logs `"TestEvent"` (debug callback target).
- **FPSGameSetup** — `#if UNITY_EDITOR` menu scaffolding: `CreatePlayerPrefab`, `CreatePistolWeaponData`, `CreateTestScene`, `CreateNetworkManager`, `SetupAll`; writes to hardcoded asset paths.

---

## 4. In Progress: Vehicle Remote Control

**Goal:** a player tool (grappling-hook-style) that sends a *vehicle* to a targeted world point along an L-shaped path (world X first, then world Z) — no obstacle avoidance in v1.

**Done (code):**
- `CarController` — autopilot state (`autopilotActive` / `autopilotTarget` / `autopilotPhase` SyncVars, `autopilotSpeed` / `autopilotArriveThreshold` fields), `ServerStartAutopilot` / `ServerStopAutopilot`, `ServerTickAutopilot` L-path movement in `FixedUpdate` (skips the driverless drift while active), cancel hooks in `TakeDamage`, `ServerEnter`, and `StartDestruction`.
- `RemoteVehicleControlTool` — `NetworkBehaviour` tool: client raycast, bind-a-car / command-to-point in one fire, cooldown, L-path `LineRenderer` preview, `CmdCommandAutopilot`, client + server validation that the car has no driver and is alive.

**Remaining (Unity wiring, not code):**
1. Add `RemoteVehicleControlTool` to `Player.prefab` (its own NetworkBehaviour on the player's identity).
2. Inspector: assign the FP `Camera`, a `LineRenderer` template, `aimMask` (car + ground/road layers), tune `cooldownSeconds`.
3. Assign it to a free index in `PlayerShooting.weaponTools[]`; mirror how `FloorCutter` fills its parallel `availableWeapons[]` / `weaponModels[]` slots.
4. Playtest: bind → command → cooldown → cancel-on-driver-enter → cancel-on-damage → cancel-on-destroy.

Open design item: visual feedback for *which* car is bound (HUD reticle tint is probably enough for v1).

---

## 5. Known Gaps / TODO Backlog

- **Respawn** — `PlayerHealth.Respawn()` exists but nothing in the scene calls it; no respawn flow.
- **Match flow** — no win/lose conditions, no round state (the `MatchStateMessage` struct is unused).
- **Zombies** — `ZombieEnemy` is fully implemented but not placed/spawned anywhere.
- **Grappling hook** — local-only prototype, no networking, placeholder visuals.
- **Vehicle destruction** — only explosion FX + `vehicleIsAlive`; no wreck model / persistent destroyed state.
- **Enemy death visuals** — `Enemy.OnAliveChanged` hook is empty.
- **Safe** — no loot drop / visual change on destruction.
- **UI stubs** — `PauseMenu.OpenSettings/Disconnect`, `GameOverUI.OnRestartButtonClick`.
- **Net messages** — `NetworkMessages.cs` structs declared but never sent/handled.
- **Settings** — `GameSettings` not networked and not persisted per client.
- **Spawn** — `PlayerSpawnManager.FurthestFromPlayers` ignores `minSpawnDistance`.
- **`WeaponBase`** — abstract base unused; firing logic still inline in `PlayerShooting`.
