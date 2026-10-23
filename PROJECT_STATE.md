# Corovan — Project State

> Snapshot of implemented systems and mechanics. Last reviewed: **2026-10-23** (after the FPS Framework integration, branch `FPS_framework_merge`).
> For architecture conventions and lessons, see `CLAUDE.md`. This doc focuses on *what exists right now*.
> Convoy motion/physics system: design in `convoy_raid_physics_v0.md`, scripts + prefab setup in `convoy_raid_physics_setup.md`.

---

## 1. Gameplay Overview

Corovan is a **multiplayer convoy FPS** on Unity 6 with Mirror networking. Players join a host (peer-to-peer over ZeroTier), spawn on foot, and play around a vehicle convoy on an endlessly scrolling road.

**The loop that works today:**
1. Join via the network UI → spawn at a rotated spawn point.
2. Move/shoot on foot — our movement with the FPS Framework's camera feel and weapons (pistol, rifle, shotgun, sniper, grenades, axe, tools).
3. Walk up to the convoy car, press **E** to drive it (client authority transfer).
4. Pick up loot props (carryables), carry them into the car's **cargo trigger** → score goes up.
5. Survive turret enemies and rolling barrel hazards that damage you and the car.
6. Go **down** at zero health instead of dying; a teammate holds **E** to revive you before the bleedout timer runs out.
7. **Win** by driving the car into the extraction zone and holding it there; **lose** if the whole squad is down/dead at once. Either way `MatchEndUI` shows the result (+ loot score on victory) with a host-only restart.
8. Optional: destruction objectives (destroy N targets → fire an event).

**Supporting / experimental mechanics present in code:** zombie melee enemies, ragdoll target dummies (`RagdollBot`), a grappling hook, a floor-cutting tool + safe that falls through the hole, wind zones, kill volumes, vehicle remote control.

**Scenes** (`Assets/Game/Scenes/`):
| Scene | Role |
|---|---|
| `Hardcode_Level.unity` | **Main gameplay** (current) — convoy, enemies, extraction zone, match UI |
| `Road.unity` | Earlier main gameplay scene — infinite road, convoy, enemies, barrels |
| `JoinMenu.unity` | Lobby / join flow |
| `FloorCutter.unity` | Floor-cutting tool test bed |
| `GrapplingHook_Test.unity` | Grappling hook test bed (untracked) |
| `Drone_Testing.unity` | Drone enemy test bed |
| `Wall Laser.unity` | WIP (untracked) |
| `WagonsConstruct.unity` | WIP (untracked) |
| `SampleScene.unity` | Placeholder |

---

## 2. System Status at a Glance

| System | State | Notes |
|---|---|---|
| Player movement / camera | ✅ Working | Our `PlayerController` (convoy riding, downed, vehicle) + FPS Framework camera rig (FOV kick, head bob, jump/land kicks, recoil). Lean off |
| Weapons | ✅ Working | FPS Framework firearms (pistol/rifle/shotgun/sniper), axe, grenades, tools as loadout slots (`PlayerLoadout`). Hits client-authoritative, damage server-side `int` |
| Friendly fire | ✅ Working | Full; per-target multipliers on `PlayerHitReporter`. Remote players are hit through a `PlayerHitbox` capsule |
| Grenades | ✅ Working | Server-spawned `NetGrenade`; fuse + blast on the server, effects on clients |
| Ragdoll target dummy | ✅ Working | `RagdollBot` (FPSF Bot): server health, ragdoll with hit push on every client, stands up again |
| Third-person weapon view | ◑ Placeholder | World model per slot at a fixed anchor, no head pitch; remote shot FX (flash/sound/tracer/impact) |
| Health / damage / death | ✅ Working | `LifeState` SyncVar (Alive/Downed/Dead), bleedout deadline, regen — **respawn not wired to scene** |
| Down / revive | ✅ Working | Zero HP → downed + bleedout; teammate hold-E revive; enemies ignore downed; lying-model swap + marker synced |
| Match state (win / lose) | ✅ Working | `GameStateManager` SyncVar `MatchResult`; defeat on squad wipe, victory via `ExtractionZone` (`IWinCondition`) |
| Carry / throw loot | ✅ Working | Client-authority pickup; drop/throw |
| Vehicle driving | ✅ Working | Arcade `MovePosition`, authority transfer, vehicle health + explosion FX |
| Vehicle **remote control** | ◑ Wired, needs playtest | Loadout slot 8 (`ToolItem` → `RemoteVehicleControlTool`), unlocked by `CarControl_Pickup` (see §4) |
| Cargo / scoring | ✅ Working | `CargoTrigger` → `ScoreManager` SyncVar |
| Turret enemies | ✅ Working | Detect player/car, rotate, fire projectiles — no death visuals |
| Barrel hazards | ✅ Working | Server pool + timed spawn, curve bounce, damages car/player |
| Convoy motion / physics | ✅ Working | Single `MotionDirector` signal → platform tilt, camera shake, player sway, loot force, swerve telegraph, fall hazard + respawn. Setup: `convoy_raid_physics_setup.md` |
| Destruction objective | ✅ Working | `DestructionObjective` tracks `DestructionTarget`s |
| Interactables (hold-E) | ✅ Working | Generic; binds UnityEvents (doors etc.) |
| Wind zones / kill volumes | ✅ Working | Trigger-based push / instant kill |
| Floor cutter + falling safe | ✅ Working (tool) | Runtime mesh hole + point-in-quad fall detection |
| Zombie enemies | ⚠️ Coded, not placed | Full NavMesh state machine — not spawned in any scene |
| Grappling hook | ⚠️ Local-only prototype | No networking; placeholder visuals |
| UI (HUD/scoreboard/pause/settings) | ◑ Mostly working | Our HUD + FPSF Weapon HUD (ammo, dynamic crosshair) + FPSF hit marker / damage flash. `PauseMenu` not placed in any scene; some buttons stubbed (see §3 UI) |
| Net message structs | ❌ Not integrated | Declared in `NetworkMessages.cs`, unused (`GameStateManager` uses its own `MatchResult`) |
| Game settings | ◑ Local only | Applied client-side; not networked |

Legend: ✅ done · ◑ partial · ⚠️ exists but not integrated · 🚧 actively being built · ❌ absent

---

## 3. Systems in Detail

### Player (`Assets/Game/Scripts/Player/`)
- **PlayerInputHandler** — router for our *game* actions (Input System *Send Messages* on `InputSystem_Actions`: `OnInteract/OnPrevious/OnNext`, `Attack`/`Aim` polled for carry/keycard, slot keys 1–9/0). Move/look/jump/crouch/sprint and weapon fire/aim/reload live in FPSF's `Controls` (read through `CharacterInput`); in a car it steers from `CharacterInput.moveInput`. Owns the FPSF input block (`SetPaused`, and for good on death/match end). Handles vehicle enter/exit (proximity search), hold-E progress for interactables, carry/throw gating, and the post-throw aim block. Also owns the **revive hold** on downed teammates (found by iterating `NetworkClient.spawned`, *not* physics — remote players' only collider, the CharacterController, is disabled), driving `CmdBeginRevive/CmdCancelRevive/CmdCompleteRevive`. Reacts to its own `OnDowned` (strip weapons/vehicle/holds, keep look), `OnRevived`, `OnDeath`, and `GameStateManager.OnMatchEnded` (disable input, exit vehicle, unlock cursor). Commands: `CmdEnterVehicle`, `CmdExitVehicle`, `CmdDetachSafe`, `CmdInteract`, `CmdToggleBreachConsole`, revive Cmds. Disables `PlayerInput` on remote players in `OnStartClient`.
- **PlayerController** — `CharacterController` FPS movement (walk 5 / sprint 8.5 / tactical sprint 10.5 (double-tap) / crouch 2.5 m/s, jump, air control, slope/step handling, gravity ramp, crouch height blend, vehicle jump boost). Reads FPSF `CharacterInput` and implements FPSF `ICharacterController` (FPSF weapons set aim/fire walk speeds through `SetSpeed`/`ResetSpeed`). `SetVehicleMode(bool)` zeroes velocity + disables the controller. `SetDownedMode(bool)` pins the player where they fell (movement/jump/sprint/crouch blocked in the setters) but keeps the CharacterController so gravity and `MotionPlatform` riding still apply. Exposes `AddExternalVelocity` (knockback / wind) and `IsGrappling`. Disabled on remote players (NetworkTransform drives them).
- **PlayerCamera** — pitch/yaw clamp ±85° from FPSF `CharacterInput.lookInput` (includes FPSF recoil), motion-shake offsets, death camera drop. Our sensitivity is converted to FPSF's FOV-scaled formula (`FPSFSensitivity`). FOV/ADS/head bob/sprint kick belong to FPSF's `CameraManager`. API: `GetAimRay`, `Lock/UnlockCursor`, `SetSensitivity`, `SetShakeOffsets`. Local-owner only.
- **PlayerLoadout** — weapons/tools on top of the FPSF `Inventory` (replaced `PlayerShooting`). Slots = `LoadoutItem`s baked **inactive** under the Inventory, child order = keys 1–9: axe, pistol, rifle, shotgun, sniper, grapple, floor cutter, car remote, grenade. Server `unlockedMask` (axe by default, the rest via `WeaponPickup`; `unlockAllForTesting`), owner-chosen slot mirrored as `equippedSlot`. FPSF Inventory's own `Update` is off; we call `Switch()`. "Hands busy" (carrying, keycard, vehicle, downed, paused, LMB/RMB still held after a throw, dead/match over) = nothing in hand. Ammo is FPSF's, client-side; a pickup of an owned weapon refills via `TargetRpc`. HUD ammo feed.
- **LoadoutItem / MeleeItem / ToolItem** — slot marker (id, default unlock); the axe (FPSF `Item`: procedural chop + camera SphereCast, damage via `PlayerHitReporter`, reads `Axe.asset`); a tool slot (enables a tool `Behaviour` while in hand — owner only).
- **PlayerHitReporter** — client-authoritative hits: `ReportHit` → `CmdReportHit` (server checks shooter alive, ≤300 m, damage clamp) → `ServerApplyHit` with friendly-fire multipliers (teammates, vehicle; default 1) → `DamageRouter`. `TargetKillConfirmed` lights the FPSF hit marker red.
- **RemotePlayerHitbox** — capsule on layer `PlayerHitbox` (21, collides with nothing) enabled on *other* players' copies, so they can be shot (their CharacterController is off).
- **RemoteWeaponView** — third-person placeholder: world model per slot under `RemoteWeaponAnchor` for `equippedSlot`; replays the owner's shots (FPSF `Firearm.ShotFired` → unreliable `CmdShot` → `RpcShot`: flash, sound, tracer, impact). Cosmetic only.
- **PlayerGrenades** — FPSF `Throwable.ThrowOverride`: the owner spends one grenade (FPSF ammo "Gernades") and `CmdThrow` → the server validates and spawns `NetGrenade`.
- **PlayerDamageFeedback** — owner: FPSF damage-indicator flash + camera shake on health loss.
- **FPSFPlayerBridge** — disables every FPSF component (except `Ragdoll`) on other players' copies before their `Start`; wires `Inventory.characterManager`.
- **PlayerHealth** — `currentHealth` (hook) + `lifeState` (`Alive/Downed/Dead`, hook) SyncVars; bleedout synced as a deadline (`bleedoutEndTime` + pause flag), optional server-side regen. **All damage/health values are `int`.** Zero HP calls `GoDown()` (downed + bleedout) rather than dying; `TakeDamage` is a no-op while downed. Server API: `TakeDamage(int)`, `TakeDamage(int, hitPoint, hitDir)`, `Heal`, `SetHealth`, `ServerKillInstant()` (skips downed — kill volumes, falls, car explosion), `ServerRevive()`, `ServerSetBleedoutPaused(bool)`, `Respawn`, `ResetHealth`. Events `OnHealthChanged/OnDowned/OnRevived/OnDeath/OnRespawn` (raised via hook on clients+host, via `isServerOnly` on a dedicated server). Registered with `GameStateManager` by `CustomNetworkManager`. **`Respawn()` has no caller in scene.**
- **PlayerRevive** — sits next to `PlayerHealth`; owns the "can a teammate pick me up?" side. `[SyncVar] reviverNetId` + `reviveStartTime`; `CanBeRevivedBy`, `ServerBeginRevive/ServerCancelRevive/ServerCompleteRevive` (re-validate reviver identity, range +1 m tolerance, hold ≥ `reviveHoldDuration` − 0.25 s). A `[ServerCallback] Update` watchdog aborts if the reviver dies, goes down, walks off, or disconnects. Pauses the victim's bleedout while a hold is in flight. Exposes `IsBeingRevived`, `ReviveProgress`, `OnBeingRevivedChanged`.
- **PlayerDownedVisual** — swaps the standing model (`aliveVisual`) for a lying one (`downedVisual`) on all clients, driven by `PlayerHealth`'s life-state events (`IsIncapacitated` — dead players stay on the ground too). Shows a `downedMarker` above a downed teammate (never to the owner). Both models must live under a `LocalPlayerBodyHider.bodyRoots` entry.
- **PlayerCarry** — pickup within ~10 m, attaches to a carry point, puts weapons away via `PlayerLoadout.SetCarryingMode`. Commands `CmdPickup/CmdDrop/CmdThrow`; throw applies camera-ray impulse. Local parenting is instant; SyncVar confirms on others. `ForceDrop()` used when the carrier goes down.
- **RemoteVehicleControlTool** *(see §4)* — `NetworkBehaviour` tool, enabled while loadout slot 8 (`car_remote`, a `ToolItem`) is in hand. First fire on a car = bind it; later fires = `CmdCommandAutopilot(carNetId, point)` → `CarController.ServerStartAutopilot`. L-path preview LineRenderer; cooldown gate; bind/command refused if the car has a driver or is destroyed (validated client-side via SyncVars, re-validated server-side on the command).
- **LocalPlayerBodyHider** — moves the local player's body renderers to a layer the FPS camera doesn't cull (optional shadows-only mode). Runs on `OnStartLocalPlayer`. Needs the layer set up in project settings.
- **GrapplingHookInput** — per-frame `Attack` (our asset) and jump (FPSF `CharacterInput.jumpInput`) bools for the grapple controller.
- **GrapplingHookController** — *local-only prototype.* Active while loadout item `grapple` is in hand. Idle→Grappling states, 30 m aim raycast, transparent sphere placeholder, fly-to-target at 20 m/s, jump cancels, sets `PlayerController.IsGrappling`. **No networking** — won't work in multiplayer yet.

### Weapons (`Assets/Game/Scripts/Weapons/` + FPS Framework)
- Firearms are **FPS Framework** prefabs (`Assets/ExternalAssets/FPS Framework/Prefabs/Weapons/`, presets in `Data/Weapons/`): projectile (pistol/rifle/shotgun) or hitscan (sniper), recoil, spread patterns, attachments, fire modes, their own Weapon HUD.
- **WeaponData** — our ScriptableObject; only `Axe.asset` is still used (damage, range, melee radius, swing rate for `MeleeItem`).
- **NetworkExplosive** — networks an FPSF `Explosive` (the thrown grenade, `NetGrenade.prefab`): the server runs fuse + blast; clients get a kinematic copy and play the blast effects/camera shake on `RpcExploded`; the server despawns it.

### Combat (`Assets/Game/Scripts/Combat/`)
- **IDamageReceiver** (+ optional **IHitForceReceiver**) — `CurrentHealth/MaxHealth/IsDead/TakeDamage(int)`; implemented by `PlayerHealth`, `EnemyTurret`, `DroneEnemy`, `ZombieEnemy`, `CarController`, `Barrel`, `ContainerLock`, `RagdollEnemy`, and the hit-counting `DestroyableObject`/`DestructionTarget` (any hit = one hit). Found from a hit collider with `GetComponentInParent`.
- **DamageRouter** — the single server-side entry for weapon damage.
- **FPSFDamageable** — adapts a receiver to FPSF's `IDamageable` (added on first hit through `Firearm.ResolveDamageable`, or placed explicitly where HealthGroups need it in `Start`); routes client hits through the local `PlayerHitReporter`, server-side damage through the attacker's reporter.

### Vehicle (`Assets/Game/Scripts/Vehicle/`)
- **CarController** — arcade car `NetworkBehaviour`. `rb.MovePosition()` in `FixedUpdate`, velocity tracked in `currentVelocity` (never `rb.linearVelocity` — net sync overwrites it). `driverNetId` SyncVar w/ hook hides the driver model + flips `PlayerController.SetVehicleMode` on all clients. Enter/exit assigns/removes client authority. `vehicleHealth` + `vehicleIsAlive` SyncVars; `TakeDamage(int)` → `StartDestruction()` → fire FX → `ServerExplode` (kills the driver) → explosion FX, renderers/colliders off. Collision damage via `OnCollisionEnter` → `CmdReportCollision`. Driverless cars roll slowly backward once driven. **Autopilot (new):** `ServerStartAutopilot(Vector3)` / `ServerStopAutopilot()`; `autopilotActive`/`autopilotTarget`/`autopilotPhase` SyncVars; `ServerTickAutopilot` drives an L-shaped path (phase 0 = align world X, phase 1 = align world Z, threshold `autopilotArriveThreshold`). Autopilot cancels on damage, driver entering, and destruction. *Missing:* no destruction visual beyond the explosion FX / `vehicleIsAlive` flag.
- **CargoTrigger** — child trigger (no Rigidbody) on the car; a `CarryableObject` entering it → `ServerDepositLoot` → `NetworkServer.Destroy` + `ScoreManager.AddScore(value)`. Server-only.
- **SafeMount** — child trigger that auto-attaches a `Safe` entering it (`Safe.ServerAttach`) to an anchor; `ServerDetach` releases it. Server-only.

### Networking (`Assets/Game/Scripts/Networking/`)
- **CustomNetworkManager** — extends Mirror's `NetworkManager`; registers `runtimeSpawnPrefabs` (e.g. `NetGrenade`) on clients — scenes override `spawnPrefabs` on their instances; spawn-point cycling, scene-reload recovery (clears stale refs, re-fetches from `PlayerSpawnManager`), fallback chain custom array → `NetworkStartPosition` → manager transform. Registers/unregisters each player's `PlayerHealth` with `GameStateManager` in `OnServerAddPlayer` / `OnServerDisconnect`.
- **GameStateManager** — scene singleton `NetworkBehaviour`; single `[SyncVar] MatchResult { InProgress, Victory, Defeat }`. **Defeat** = no registered player is `Alive` (`IsIncapacitated` test, evaluated on `OnDowned`/`OnDeath`/disconnect, never polled; empty roster never loses). **Victory** = polls a serialized `MonoBehaviour[]` of `IWinCondition` on `winCheckInterval` (`requireAllWinConditions` = all vs any). Public `ServerEndMatch/ServerTriggerVictory/ServerTriggerDefeat/ServerRestartMatch`. Ending the match only flips the SyncVar — the world keeps running. `OnMatchEnded` event fires on clients+host (via hook) and dedicated server (via `isServerOnly`).
- **IWinCondition** — one requirement (`bool IsMet`) polled server-side by `GameStateManager`. Implemented by `ExtractionZone`.
- **PlayerSpawnManager** — spawn modes `RoundRobin` / `Random` / `FurthestFromPlayers` (uses the `Player` tag at runtime). *Known gap:* `FurthestFromPlayers` ignores `minSpawnDistance`.
- **ScoreManager** — singleton `NetworkBehaviour`; `totalScore` SyncVar w/ hook → `OnScoreChanged` event; `AddScore(int)` server-side.
- **NetworkDebugUI** — runtime overlay: Host/Client/Server buttons, address field (defaults to a ZeroTier IP), auto-hides once connected.
- **NetworkMessages** — struct declarations (`WeaponFireMessage`, `DamageMessage`, `PlayerDeathMessage`, `PlayerRespawnMessage`, `ChatMessage`, `MatchStateMessage` + `MatchState` enum, `ScoreUpdateMessage`). **Declared but not wired to anything.**

### Environment (`Assets/Game/Scripts/Environment/`)
- **Enemy** — turret `NetworkBehaviour`: SphereCollider trigger detection, server-only update loop rotates toward + fires `EnemyProjectile` (`NetworkServer.Spawn`) at a `PlayerHealth` (on foot) or `CarController` (driven). Targets filtered on `IsTargetable` (alive only), so downed players are ignored; re-sweeps its detection volume on `retargetInterval` to re-acquire a revived teammate who never left the trigger. Health/alive SyncVars. *Gap:* `OnAliveChanged` hook empty — no death visuals.
- **EnemyProjectile** — server-spawned projectile; client-side travel, server validates the hit (damages `PlayerHealth` or `CarController`) then despawns; lifetime safety net.
- **RagdollEnemy** (`Enemies/`, on `Prefabs/Enemies/RagdollBot.prefab`) — FPSF Bot dummy with server health; on death `RpcDie` flops FPSF `Ragdoll` with the hit push on every client (cosmetic, per client); stands up after `respawnDelay` (or is destroyed). Bone hits use FPSF `HealthGroup` multipliers.
- **ZombieEnemy** — **fully coded** NavMesh melee zombie: `Idle/Chasing/Attacking/Dead` SyncVar state machine, delayed-damage melee, `[ClientRpc]` hit effects, disables `NavMeshAgent` on clients, drops collider on death. **Not spawned in any scene yet.**
- **CarryableObject** — networked loot prop; pickup assigns client authority, kinematic + colliders off while carried; `SetCarriedLocally` for instant feel; SyncVar hook confirms. Has a `Value` used by `CargoTrigger`.
- **InteractableObject** — hold-E framework; server validates range + hold duration (timed in `PlayerInputHandler`), fires a `UnityEvent` on all clients via `[ClientRpc]`. No built-in effect — bind your own (e.g. `DoorOpener`).
- **DestroyableObject** — hit-count destructible; counter SyncVar; disables the GameObject at threshold. No FX/events.
- **DestructionTarget** — destructible that disables renderers/colliders and raises `OnTargetDestroyed` server-side for an objective to listen to.
- **DestructionObjective** — watches an array of `DestructionTarget`s; when the required count is destroyed, fires a `UnityEvent` on all clients via `[ClientRpc]`.
- **ExtractionZone** — victory `IWinCondition`: hold a driven, alive car (`HasDriver` + `VehicleIsAlive`, `requireDriver` toggle) inside its trigger for `captureDuration` continuous seconds. Tracks overlapping car colliders in a `Dictionary<Collider,CarController>` (per-collider, so a partial exit doesn't drop a car still inside); capture is all-or-nothing (deadline resets the instant the car stops qualifying). Countdown synced as a deadline (`captureEndTime` + `capturing`), exposing `CaptureProgress`/`OnCapturingChanged` for `ExtractionUI`. Needs a `NetworkIdentity`; add it to `GameStateManager.winConditionSources`.
- **DoorOpener** — minimal: sets the GameObject inactive when opened; state SyncVar.
- **InfiniteRoad** — client-only tile recycler (scrolls + re-queues tiles); no networking.
- **KillVolume** — server-validated trigger: instantly kills players (`ServerKillInstant()`, skipping the downed state), optionally damages cars.
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
- **HUD** — health bar + text (low-health color), crosshair, grenade count, interact progress/prompt, carry hint, score readout, and the **downed overlay** (bleedout countdown + being-revived bar). With a firearm in hand our crosshair/ammo hide and the FPSF **Weapon HUD** (per firearm: ammo, dynamic crosshair, reload/low-ammo/fire-mode alerts) takes over. Instantiates `FPSF_CombatHUD` (FPSF `UIManager` + Hitmarker + Damage Indicator only) for the local player. The canvas lives in the **player prefab**, so it disables itself for non-local players (`NetworkIdentity.isOwned` check in `Start`). Wired to `PlayerHealth` / `PlayerLoadout` / `PlayerInputHandler` / `PlayerRevive` events.
- **MatchEndUI** — scene UI driven by `GameStateManager.OnMatchEnded`: victory/defeat text, loot score (`ScoreManager.TotalScore`, victory only), host-only restart (`ServerRestartMatch`). Handles a late join (already-decided match).
- **ExtractionUI** — scene progress bar bound to an `ExtractionZone` (`CaptureProgress` + `OnCapturingChanged`); shown only while the car is being held in the zone.
- **LocalPlayerDeadUI** *(renamed from GameOverUI)* — "you died" overlay while the match is still running (a teammate may still win/lose it). Binds to the runtime local player via `NetworkClient.localPlayer` (no serialized player ref possible on a scene object), shows on `OnDeath`, hides on revive/respawn and when the match ends (so `MatchEndUI` owns the screen).
- **ScoreboardUI** — Tab-hold toggles the scoreboard panel.
- **PauseMenu** — ESC sets `Time.timeScale = 0`, toggles the panel, manages cursor lock, and blocks FPSF input / puts the weapon away (`PlayerInputHandler.SetPaused`). **Not placed in any scene.** *Stubs:* `OpenSettings()` and `Disconnect()` are TODO.
- **SettingsMenu** — Controls/Video/Audio tabs; loads/saves `GameSettings` (sensitivity, invert Y, FOV, quality, vSync, volumes); `ResetToDefaults()` present.

### Config (`Assets/Game/Scripts/Config/`)
- **GameSettings** — ScriptableObject: mouse (sensitivity X/Y, invert Y), FOV (60–120), audio volumes (master/sfx/music), graphics (quality, vSync, target framerate), crosshair (color/size/thickness/dynamic spread), gameplay (head bob toggle + intensity). `ApplySettings()` applies quality/vSync/framerate/master volume. **Client-local only — not networked, not persisted per-client.**

### Animations / Utility / Editor
- **WheelAnimation** — sets `animator.speed` from a serialized `wheelSpeed` (vehicle wheel loops).
- **SceneReloader** — F9 hot-reload; picks `ServerChangeScene` vs `LoadScene` depending on connection state.
- **AutoTile** — `[ExecuteAlways]`; scales `mainTextureScale` from `transform.localScale` (walls use X/Y, floors X/Z).
- **AxisSpacer** — `[ExecuteAlways]` layout helper; distributes children along one axis with uniform spacing; cache/reset via context menu.
- **TestEvent** — logs `"TestEvent"` (debug callback target).
- **FPSGameSetup** — `#if UNITY_EDITOR` menu scaffolding: `CreatePlayerPrefab`, `CreatePistolWeaponData`, `CreateTestScene`, `CreateNetworkManager`, `SetupAll`; writes to hardcoded asset paths. Predates the FPS Framework player — the player it generates lacks the FPSF rig and loadout.

---

## 4. Vehicle Remote Control (wired, needs playtest)

**Goal:** a player tool (grappling-hook-style) that sends a *vehicle* to a targeted world point along an L-shaped path (world X first, then world Z) — no obstacle avoidance in v1.

**Done (code):**
- `CarController` — autopilot state (`autopilotActive` / `autopilotTarget` / `autopilotPhase` SyncVars, `autopilotSpeed` / `autopilotArriveThreshold` fields), `ServerStartAutopilot` / `ServerStopAutopilot`, `ServerTickAutopilot` L-path movement in `FixedUpdate` (skips the driverless drift while active), cancel hooks in `TakeDamage`, `ServerEnter`, and `StartDestruction`.
- `RemoteVehicleControlTool` — `NetworkBehaviour` tool: client raycast, bind-a-car / command-to-point in one fire, cooldown, L-path `LineRenderer` preview, `CmdCommandAutopilot`, client + server validation that the car has no driver and is alive.

**Wiring:** `RemoteVehicleControlTool` is on `Player.prefab` (disabled by default) and is loadout slot 8 (`car_remote`, a `ToolItem` that enables it while in hand), unlocked by `CarControl_Pickup`.

**Remaining:**
1. Check the Inspector values (FP `Camera`, `LineRenderer` template, `aimMask` car + ground/road, `cooldownSeconds`).
2. Playtest: bind → command → cooldown → cancel-on-driver-enter → cancel-on-damage → cancel-on-destroy.

Open design item: visual feedback for *which* car is bound (HUD reticle tint is probably enough for v1).

---

## 5. Known Gaps / TODO Backlog

- **Respawn** — `PlayerHealth.Respawn()` exists but nothing in the scene calls it; no respawn flow (down/revive is the only way back into a fight).
- **Win conditions** — only `ExtractionZone` implements `IWinCondition`; the win rule is a single hold-the-car objective. `MatchStateMessage` / `MatchState` in `NetworkMessages.cs` stay unused (`GameStateManager` uses its own `MatchResult`); no round/countdown state.
- **Downed edge cases** — `GrapplingHookInput` reads input directly, so a downed player can still grapple; a downed driver is force-exited but left at the driver-seat position.
- **Zombies** — `ZombieEnemy` is fully implemented but not placed/spawned anywhere.
- **Grappling hook** — local-only prototype, no networking, placeholder visuals.
- **Vehicle destruction** — only explosion FX + `vehicleIsAlive`; no wreck model / persistent destroyed state.
- **Enemy death visuals** — `Enemy.OnAliveChanged` hook is empty.
- **Safe** — no loot drop / visual change on destruction.
- **UI stubs** — `PauseMenu.OpenSettings/Disconnect`.
- **Net messages** — `NetworkMessages.cs` structs declared but never sent/handled.
- **Settings** — `GameSettings` not networked and not persisted per client.
- **Spawn** — `PlayerSpawnManager.FurthestFromPlayers` ignores `minSpawnDistance`.
- **Player ragdoll** — deliberately deferred (enemies only for now).
- **Third person** — placeholder weapon anchor; head pitch is not synced; no remote axe swing / tool animation.
- **Axe & tools** — no FPSF sway (they need FPSMotion's transform rig); they render on the weapon overlay camera (FOV 50), so positions may need tuning.
- **Pickups** — the pistol pickup still shows the old revolver model; grenades don't inherit the thrower's velocity.
- **Lean** — FPSF lean keys unbound (E is our Interact).
