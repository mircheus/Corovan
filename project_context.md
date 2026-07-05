# Corovan — Project Context

> Living snapshot of the project's implemented systems and current state.
> Last reviewed: **2026-06-25**.
> Companion docs: `CLAUDE.md` (architecture conventions + lessons), `PROJECT_STATE.md` (previous snapshot, 2026-05-12), `FPS_Technical_Specification.md`, `GrapplingHook.md`.
> This doc focuses on *what exists right now* and *what changed since the last review*.

---

## 0. ⚠️ Build-Blocking Issue (uncommitted)

`Assets/Game/Scripts/Enemies/EnemyAggroZone.cs` has a **stray `2` on line 32** (between `return;` and the `foreach`). This is a syntax error that will fail compilation of the whole assembly. It is an *uncommitted working-tree change* — likely an accidental keystroke. **Delete the `2` before building/playing.**

---

## 1. Gameplay Overview

Corovan is a **multiplayer convoy FPS** on **Unity 6 (6000.0.60f1)** with **Mirror** networking (peer-to-peer over **ZeroTier**, **ParrelSync** for local testing). Players join a host, spawn on foot, and play around a vehicle convoy on an endlessly scrolling road.

**The core loop that works today:**
1. Join via the network UI → spawn at a rotated spawn point.
2. Move/shoot on foot (FPS controller, multi-weapon, ammo/reload, melee + ranged).
3. Walk up to the convoy car, press **E** to drive it (client-authority transfer).
4. Pick up loot props (carryables), carry them into the car's **cargo trigger** → score goes up.
5. Survive enemies (turrets, **enemy cars**, **drones**) and rolling barrel hazards that damage you and the car.
6. Optional objectives: destruction targets, **interaction objectives**, **shoot container locks** / **collect keycards** to open doors.

**Supporting / experimental mechanics in code:** zombie melee enemies, a grappling hook (local-only), a floor-cutting tool + safe that falls through the hole, wind zones, kill volumes, vehicle remote control (autopilot).

**Scenes** (`Assets/Game/Scenes/`):
| Scene | Role |
|---|---|
| `Road.unity` | **Main gameplay** — infinite road, convoy, enemies, barrels (modified, uncommitted) |
| `JoinMenu.unity` | Lobby / join flow |
| `Drone_Testing.unity` | **(new)** drone enemy + airborne NavMesh test bed |
| `FloorCutter.unity` | Floor-cutting tool test bed |
| `GrapplingHook_Test.unity` | Grappling hook test bed |
| `Wall Laser.unity` | WIP |
| `WagonsConstruct.unity` | WIP |
| `Test.unity` | Scratch scene |

---

## 2. What Changed Since 2026-05-12

The biggest additions are a **richer enemy roster**, a **Doom-style keycard/door system**, **shootable container locks**, and **objective/autopilot wiring**.

- **Enemy scripts moved to `Assets/Game/Scripts/Enemies/`** (`Enemy`, `EnemyProjectile`, plus the new ones below). `ZombieEnemy` still lives under `Environment/`. Namespaces are mixed: most stay `FPSGame.Environment`; `DroneEnemy` uses `FPSGame.Enemies`.
- **Turret `Enemy` now has death visuals + animation** — the previously-empty `OnAliveChanged` hook now hides the body, disables the collider, plays a death effect, and drives a `Shot` animator trigger + particle FX. Projectile speed is now an enemy responsibility.
- **`DroneEnemy` (new)** — flying NavMesh enemy on a dedicated airborne navmesh; manual vertical control (cruise height + combat Y-tracking + sine bob + clearance clamps), standoff distance, line-of-sight gate, full `Idle/Chasing/Attacking/Dead` state machine.
- **`EnemyCarController` + `EnemyAggroZone` (new)** — enemy cars that chase a player-driven car via NavMesh, with a yaw-only gun pivot and configurable post-driver-exit behavior (keep shooting the player on foot, or the abandoned car).
- **Keycard system (new, Doom-style)** — `KeycardId` enum (Red/Blue/Yellow/Green), `KeycardPickup` (walk-over collect), `PlayerKeyring` (bitmask SyncVar on the player), `KeycardLock` (proximity-driven `DoorOpener`; checks the on-foot player OR a car's current driver).
- **Container system (new)** — `ContainerLock` (shootable HP lock, child NetworkBehaviour sharing the container's identity) + `ContainerDoor` (swings doors open via AnimationCurve when the lock breaks; SyncVar open state for late joiners).
- **`InteractObjective` (new)** — tracks N interactions across `InteractableObject[]`; one-shot (latching) or repeatable; fires a UnityEvent.
- **`CarStopTrigger` (new)** — server trigger that halts an autopiloted car (`ServerHalt`).
- **`CarController` autopilot is live** — `ServerStartAutopilot`/`ServerStopAutopilot`/`ServerHalt`, L-path movement, plus a new `ServerOnDriverExited` event and `GetCurrentDriverServer()` (consumed by `EnemyCarController` and `KeycardLock`).
- **`PlayerShooting` now damages `ContainerLock`** on hit (in addition to `PlayerHealth`, `Enemy`, `ZombieEnemy`, `DestroyableObject`, `DestructionTarget`, `Barrel`). Note: it does **not** yet damage `DroneEnemy` or `EnemyCarController`.

---

## 3. System Status at a Glance

| System | State | Notes |
|---|---|---|
| Player movement / camera | ✅ Working | Walk/sprint/crouch/jump, head bob, ADS FOV, recoil |
| Weapons (ranged + melee) | ✅ Working | Multi-slot, per-slot ammo, reload, optimistic switch |
| Health / damage / death | ✅ Working | SyncVar health, regen, death state — **respawn not wired to scene** |
| Carry / throw loot | ✅ Working | Client-authority pickup; drop/throw |
| Vehicle driving | ✅ Working | Arcade `MovePosition`, authority transfer, vehicle health + explosion FX |
| Vehicle **autopilot / remote control** | ◑ Code complete | `CarController` autopilot + `RemoteVehicleControlTool` done; tool still not wired into `Player.prefab` / `weaponTools[]` (see §5) |
| Cargo / scoring | ✅ Working | `CargoTrigger` → `ScoreManager` SyncVar |
| Turret enemies | ✅ Working | Detect player/car, rotate head, fire, **now with death FX/anim** |
| **Enemy cars** | ◑ New, in scene WIP | NavMesh chase + yaw gun + aggro zone; **not damageable by player fire yet** |
| **Drone enemies** | ◑ New, test scene | Airborne NavMesh, full state machine; **not damageable by player fire yet**; only in `Drone_Testing` |
| Barrel hazards | ✅ Working | Server pool + timed spawn, curve bounce, damages car/player |
| Destruction objective | ✅ Working | `DestructionObjective` tracks `DestructionTarget`s |
| **Interaction objective** | ✅ New | `InteractObjective` — count-based or repeatable |
| Interactables (hold-E) | ✅ Working | Generic; binds UnityEvents (doors etc.) |
| **Keycards / locked doors** | ✅ New | Pickup → keyring bitmask → `KeycardLock` proximity opens `DoorOpener` |
| **Container (shootable lock)** | ✅ New | `ContainerLock` HP → `ContainerDoor` swing |
| Wind zones / kill volumes | ✅ Working | Trigger-based push / instant kill |
| Floor cutter + falling safe | ✅ Working (tool) | Runtime mesh hole + point-in-quad fall detection |
| Zombie enemies | ⚠️ Coded, not placed | Full NavMesh state machine — not spawned in any scene |
| Grappling hook | ⚠️ Local-only prototype | No networking; placeholder visuals |
| UI (HUD/scoreboard/pause/settings/gameover) | ◑ Mostly working | Several buttons stubbed; **keyring HUD not built** |
| Net message structs | ❌ Not integrated | Declared in `NetworkMessages.cs`, unused |
| Game settings | ◑ Local only | Applied client-side; not networked / not persisted |
| Win / lose conditions | ❌ Missing | No match flow |

Legend: ✅ done · ◑ partial · ⚠️ exists but not integrated · 🚧 actively being built · ❌ absent

---

## 4. New Systems in Detail

### Enemy roster (`Assets/Game/Scripts/Enemies/` + `Environment/ZombieEnemy.cs`)

- **Enemy** (turret, `FPSGame.Environment`) — SphereCollider trigger detection, server-only update; a `rotatingPart` (gun/head) yaws toward a `PlayerHealth` (on foot) or driven `CarController`, fires `EnemyProjectile`. SyncVars `isAlive` (hook) + `health`. `keepAttackAfterTriggerExit` flag. **Death now handled:** `HandleDeath()` plays a death effect, hides `objectToHideOnDeath`, disables `bodyCollider`. Animator `Shot` trigger + particle FX on fire. Damaged by `PlayerShooting`.
- **DroneEnemy** (`FPSGame.Enemies`) — flying enemy needing a dedicated **"Drone" airborne NavMeshSurface**. Horizontal movement via `NavMeshAgent` (rotation off, `baseOffset` = hover height); **vertical is manual** (cruise + combat Y-track within `±combatYRange` + sine bob + ground/ceiling clearance raycasts). `detectionRadius`/`attackRadius`/`preferredStandoff`, line-of-sight check before firing, `Idle/Chasing/Attacking/Dead` SyncVar state → animator. Has `TakeDamage`/`Die` but **`PlayerShooting` doesn't call it yet.**
- **EnemyCarController** (`FPSGame.Environment`) — kinematic Rigidbody + NavMeshAgent (rotation off; body orientation-locked, only `gunPivot` yaws via a `gunYaw` SyncVar). Chases the nearest registered player car, stops at `stopDistance` (no ramming), fires within `attackRange`. `DriverExitBehavior` (ShootPlayer/ShootCar) decides what to target after the driver leaves — snapshots the last driver via `CarController.ServerOnDriverExited`. **Targets are supplied by `EnemyAggroZone`, not self-acquired.** Not damageable by player fire yet.
- **EnemyAggroZone** (`FPSGame.Environment`, plain `MonoBehaviour`) — server trigger; when a player `CarController` enters, calls `RegisterTarget` on a serialized list of `EnemyCarController`s (`UnregisterTarget` on exit, gated by `enableTriggerExit`). **⚠️ Contains the stray-`2` compile error (see §0).**
- **EnemyProjectile** — server-spawned; client-side travel, server validates hit, ignores the firing enemy's colliders; `Initialize(dir, damage, speed)`.
- **ZombieEnemy** — unchanged: fully coded NavMesh melee zombie, **still not placed in any scene**.

### Keycards & doors (`Assets/Game/Scripts/Environment/` + `Player/`)

- **KeycardId** — enum `Red/Blue/Yellow/Green` (one bit each; keep < 32). A pickup and the lock it opens must share an id.
- **KeycardPickup** — `NetworkBehaviour` with a trigger; on-foot players only (driving disables the player collider). Cosmetic spin; `ServerGrantKey` then `NetworkServer.Destroy` (left in place if the player already holds it).
- **PlayerKeyring** — `NetworkBehaviour` on the player; `heldKeysMask` bitmask SyncVar (hook → `KeysChanged` event for a future HUD). `HasKey(id)`, server `ServerGrantKey(id)`. **Deliberately outside the weapon/carry inventory.**
- **KeycardLock** — `NetworkBehaviour` on a child trigger of a door's identity. Ref-counts overlapping entities (handles multi-collider cars), opens the `DoorOpener` while a key-holder is inside (player directly, or a car via its current driver), `closeWhenEmpty` re-closes. Re-evaluated every frame so picking up a key while standing in the trigger works.

### Containers (`Assets/Game/Scripts/Environment/`)

- **ContainerLock** — child `NetworkBehaviour` (shares the container root's identity via `GetComponentInParent`). `currentHealth`/`isBroken` SyncVars; `PlayerShooting.CmdFire` → `TakeDamage`. On break: hides the lock prop on all clients + fires `OnBroken`.
- **ContainerDoor** — subscribes to `ContainerLock.OnBroken`; swings `leftDoor`/`rightDoor` open around local Y via an `AnimationCurve`. `isOpen` SyncVar snaps late joiners open without animating.

### Objectives & autopilot

- **InteractObjective** — counts interactions across `InteractableObject[]`; `repeatable` fires the UnityEvent every interaction, otherwise latches after `requiredCount` (0 = all targets). `isCompleted` SyncVar replicates the latched result.
- **CarStopTrigger** — server trigger; `car.ServerHalt()` to stop an autopiloted car (skips driven cars when `onlyWhileAutopilot`).
- **CarController autopilot** — `ServerStartAutopilot(point)` drives an L-path (align world X → align world Z, `autopilotArriveThreshold`); `autopilotActive`/`autopilotTarget`/`autopilotPhase` SyncVars. Cancels on damage, driver entering, destruction, `ServerStopAutopilot`/`ServerHalt`. New `ServerOnDriverExited` event + `GetCurrentDriverServer()`.

---

## 5. Carried-Over In-Progress: Vehicle Remote Control Tool

`CarController` autopilot is done and now consumed by enemy AI / triggers. The **player-facing tool still isn't wired up:**
1. Add `RemoteVehicleControlTool` to `Player.prefab` (own NetworkBehaviour on the player identity).
2. Inspector: FP `Camera`, `LineRenderer` template, `aimMask` (car + ground/road), `cooldownSeconds`.
3. Assign to a free `PlayerShooting.weaponTools[]` index (mirror how `FloorCutter` fills its parallel slots).
4. Playtest: bind → command → cooldown → cancel-on-driver-enter → cancel-on-damage → cancel-on-destroy.

---

## 6. Known Gaps / TODO Backlog

- **⚠️ Build error** — stray `2` in `EnemyAggroZone.cs:32` (uncommitted). Fix before building.
- **Drone & enemy cars not damageable** — `PlayerShooting` recognizes `Enemy`, `ZombieEnemy`, `ContainerLock`, etc., but **not** `DroneEnemy` or `EnemyCarController`. Add them to the hit dispatch to make them killable.
- **Keyring HUD** — `PlayerKeyring.KeysChanged` event has no UI subscriber yet (no on-screen key icons).
- **Respawn** — `PlayerHealth.Respawn()` exists but nothing calls it; no respawn flow.
- **Match flow** — no win/lose conditions, no round state (`MatchStateMessage` unused).
- **Zombies** — `ZombieEnemy` fully implemented but not placed/spawned anywhere.
- **Grappling hook** — local-only prototype, no networking, placeholder visuals.
- **Vehicle destruction** — only explosion FX + `vehicleIsAlive`; no wreck model / persistent destroyed state.
- **Enemy car / drone death polish** — drone has death FX; enemy car has no death/destruction handling at all.
- **Safe** — no loot drop / visual change on destruction.
- **UI stubs** — `PauseMenu.OpenSettings/Disconnect`, `GameOverUI.OnRestartButtonClick`.
- **Net messages** — `NetworkMessages.cs` structs declared but never sent/handled.
- **Settings** — `GameSettings` not networked and not persisted per client.
- **Spawn** — `PlayerSpawnManager.FurthestFromPlayers` ignores `minSpawnDistance`.
- **`WeaponBase`** — abstract base unused; firing logic still inline in `PlayerShooting`.
- **Debug logging** — several new scripts (`ContainerLock`, `CarStopTrigger`, `Enemy`) ship with `Debug.Log` calls left in; strip before release.

---

## 7. Architecture Reminders (see `CLAUDE.md` for full detail)

- **Namespaces:** `FPSGame.<Folder>` — but note the new `Enemies/` folder mixes `FPSGame.Environment` (Enemy, EnemyCar, AggroZone) and `FPSGame.Enemies` (Drone). Be deliberate with `using` directives.
- **Damage/health values are always `int`.**
- **Networking:** `NetworkBehaviour` everywhere; `[Command]` client→server, `[ClientRpc]` server→clients, `[Server]`/`[ServerCallback]` server-only; state via `[SyncVar]` (+ hooks). Server validates all mutations.
- **Physics sync:** `NetworkTransformUnreliable` + `rb.MovePosition()` + a manual velocity field — never `NetworkRigidbodyUnreliable` / `rb.linearVelocity` for manually-driven bodies.
- **Child NetworkBehaviours** (`ContainerLock`, `CargoTrigger`, `KeycardLock`) share the root identity via `GetComponentInParent` — no nested `NetworkIdentity`.
- **NavMesh enemies:** disable Animator **Apply Root Motion**; separate NavMesh surfaces for cars vs. zombies vs. drones (airborne).
