# Corovan — Multiplayer Convoy FPS

## Tech Stack
- **Unity 6** (6000.0.60f1)
- **Mirror** networking (Assets/Mirror/)
- **New Input System** (Send Messages mode on PlayerInput component)
- **ParrelSync** for local multiplayer testing
- **ZeroTier** for peer-to-peer LAN networking

## Project Structure

```
Assets/Game/
├── Input/
│   └── InputSystem_Actions.inputactions
├── Prefabs/
│   ├── Player/Player.prefab
│   └── Network/NetworkDebugUI.prefab
├── Scripts/
│   ├── Player/
│   │   ├── PlayerInputHandler.cs    # Central input router; vehicle/carry/interact dispatch
│   │   ├── PlayerController.cs      # CharacterController FPS movement, crouch, sprint, jump
│   │   ├── PlayerCamera.cs          # First-person mouse look, ADS FOV, recoil
│   │   ├── PlayerShooting.cs        # Multi-weapon system, ammo, reload, melee/ranged
│   │   ├── PlayerHealth.cs          # SyncVar LifeState (Alive/Downed/Dead), bleedout, optional regen
│   │   ├── PlayerRevive.cs          # Hold-E revive of a downed teammate; server-validated
│   │   ├── PlayerDownedVisual.cs    # Standing/lying model swap + downed marker on all clients
│   │   └── PlayerCarry.cs           # Pick up, drop, throw CarryableObjects
│   ├── Vehicle/
│   │   ├── CarController.cs         # Arcade car: MovePosition, authority assignment, vehicle health
│   │   └── CargoTrigger.cs          # Trigger child on car; deposits loot into ScoreManager
│   ├── Environment/
│   │   ├── Enemy.cs                 # Turret enemy: detects players/cars, rotates, fires projectiles
│   │   ├── EnemyProjectile.cs       # Server-spawned projectile
│   │   ├── CarryableObject.cs       # Networked loot: pickup/drop/throw with client authority
│   │   ├── InteractableObject.cs    # Hold-E interaction (doors etc.) via UnityEvent
│   │   ├── DestroyableObject.cs     # Hit-count destruction (SyncVar)
│   │   ├── DestructionTarget.cs     # Single destructible tracked by DestructionObjective
│   │   ├── DestructionObjective.cs  # Tracks N targets; fires UnityEvent when complete
│   │   ├── ExtractionZone.cs        # Win condition: hold the driven car in the zone N seconds (IWinCondition)
│   │   ├── DoorOpener.cs
│   │   └── InfiniteRoad.cs          # Client-side scrolling tile recycler (no networking)
│   ├── Hazards/
│   │   ├── Barrel.cs                # Pooled kinematic barrel; bounces on AnimationCurve, damages car/player
│   │   └── BarrelSpawner.cs         # Server-side object pool + timed spawn loop
│   ├── Networking/
│   │   ├── CustomNetworkManager.cs  # Spawn point rotation; registers players with GameStateManager
│   │   ├── GameStateManager.cs      # Singleton NetworkBehaviour; SyncVar MatchResult, defeat on squad wipe
│   │   ├── IWinCondition.cs         # Server-polled win requirement (stub — no implementors yet)
│   │   ├── PlayerSpawnManager.cs
│   │   ├── NetworkMessages.cs
│   │   ├── NetworkDebugUI.cs        # Runtime connection info overlay
│   │   └── ScoreManager.cs          # Singleton NetworkBehaviour; SyncVar totalScore
│   ├── Weapons/
│   │   ├── WeaponData.cs            # ScriptableObject: damage, spread, recoil, reload, melee flag
│   │   └── WeaponBase.cs
│   ├── Animations/
│   │   └── WheelAnimation.cs
│   ├── UI/
│   │   ├── HUD.cs
│   │   ├── MatchEndUI.cs            # Victory/defeat screen + host-only restart
│   │   ├── ExtractionUI.cs          # Progress bar while the car is held in the ExtractionZone
│   │   ├── ScoreboardUI.cs
│   │   ├── PauseMenu.cs
│   │   └── SettingsMenu.cs
│   ├── Config/
│   │   └── GameSettings.cs
│   ├── Utility/
│   │   ├── SceneReloader.cs         # F9 hot-reload
│   │   ├── AutoTile.cs
│   │   ├── AxisSpacer.cs
│   │   └── TestEvent.cs
│   └── Editor/
│       └── FPSGameSetup.cs
├── Scenes/
│   ├── JoinMenu.unity
│   ├── Road.unity                   # Main gameplay scene (infinite road + convoy)
│   ├── SampleScene.unity
│   ├── Test.unity                   # (untracked, WIP)
│   └── WagonsConstruct.unity        # (untracked, WIP)
```

## Architecture

### Namespace Convention
All game scripts: `FPSGame.<Folder>` — `FPSGame.Player`, `FPSGame.Vehicle`, `FPSGame.Environment`, `FPSGame.Hazards`, `FPSGame.Networking`, `FPSGame.Weapons`, `FPSGame.UI`, `FPSGame.Config`

### Down / Revive System
- `PlayerHealth` holds a single `[SyncVar] LifeState { Alive, Downed, Dead }` — never two bools. Hook order can't then report "revived" for a player who was finished off while down.
- Damage that empties health calls `GoDown()` instead of dying. While `Downed`, `TakeDamage` is a no-op — the bleedout timer is the only thing that kills.
- `ServerKillInstant()` skips the downed state. Use it wherever a revive makes no sense: `KillVolume`, `PlayerFallHazard` instant-kill volumes, `CarController.ServerExplode`.
- Bleedout is synced as a deadline (`bleedoutEndTime`, a `NetworkTime.time` double) plus a pause flag — never as a per-frame countdown. Clients derive the remaining seconds themselves via `BleedoutRemaining`.
- `PlayerRevive` on the player prefab owns the hold. The reviving client times it (reusing `PlayerInputHandler`'s hold machinery and `OnInteractProgress`), but the server re-validates the reviver, the range, and that the hold really lasted `reviveHoldDuration`. A server-side watchdog in `PlayerRevive.Update` cancels the revive if the reviver dies, goes down, walks off, or disconnects.
- Only the first reviver owns a hold; a second player can't stack onto it to speed it up.
- Enemies filter targets on `IsTargetable` (alive only), so a downed player is ignored. `IsIncapacitated` (down **or** dead) is the check for "can this player still act / hold a console".
- `Enemy` re-sweeps its detection volume on `retargetInterval` while it has no target — `OnTriggerEnter` alone would never re-acquire a revived player who never left the trigger.
- A downed player keeps `PlayerInput` enabled (they still look around) and keeps their `CharacterController` (gravity + `MotionPlatform` ride still apply). Movement is blocked inside `PlayerController`'s setters, not by disabling components.

### Match State (Win / Lose)
- `GameStateManager` is a scene singleton `NetworkBehaviour` (same shape as `ScoreManager`) holding a single `[SyncVar] MatchResult { InProgress, Victory, Defeat }` — one enum, not a phase bool plus a result bool, for the same hook-ordering reason as `LifeState`.
- **Defeat** = no registered player is `Alive`. `IsIncapacitated` is the test, so a mix of downed and dead players still loses — a dead teammate can't revive anyone either. Evaluated on `OnDowned`/`OnDeath` and on disconnect, never polled.
- The roster is owned by `CustomNetworkManager` (`OnServerAddPlayer` / `OnServerDisconnect` → `ServerRegisterPlayer` / `ServerUnregisterPlayer`), not by `PlayerHealth` registering itself — a player who never finished spawning can't leave a dangling subscription.
- An empty roster never loses: `ServerReevaluateDefeat` returns early on `serverPlayers.Count == 0`, covering both the gap before the first join and the last disconnect.
- **Victory**: `GameStateManager` polls a serialized `MonoBehaviour[]` of `IWinCondition` on `winCheckInterval` (`requireAllWinConditions` = all vs any). `ExtractionZone` is the first implementor — hold the driven, alive car inside its trigger for `captureDuration` continuous seconds; it latches `IsMet` and the poll flips the SyncVar. `ServerEndMatch` / `ServerTriggerVictory` remain public for an objective to force an outcome directly.
- `ExtractionZone` (scene `NetworkBehaviour`, needs a `NetworkIdentity`): tracks overlapping car colliders in a `Dictionary<Collider,CarController>` (per-collider, so a partial exit doesn't drop a car still inside); `HasQualifyingCar` re-checks `HasDriver`/`VehicleIsAlive` each server tick. Capture is all-or-nothing — the deadline resets the instant the car stops qualifying. Countdown synced as a deadline (`captureEndTime`) + `capturing` flag, like bleedout; `CaptureProgress` (0–1) and `OnCapturingChanged` drive `ExtractionUI` (scene-level progress bar, shown only while capturing). `MatchEndUI` shows `ScoreManager.TotalScore` on victory only.
- Ending the match **does not stop the world** — turrets keep firing, `BarrelSpawner` keeps spawning, bleedout keeps ticking. It only flips the SyncVar. Clients react in `PlayerInputHandler.OnMatchEnded` (disable `PlayerInput`, drop holds, exit vehicle, unlock cursor) and `MatchEndUI`.
- Restart reloads the active scene via `ServerRestartMatch`; the button is only shown when `NetworkServer.active`. `GameStateManager` is a scene object, so the roster and result reset themselves.
- `MatchEndUI` is separate from `GameOverUI` — the latter still reacts to the local player's own death while the match may well still be running.

### Damage & Health Values
- All damage and health values are `int` — damage fields, `health`/`maxHealth` fields, `TakeDamage(int)` parameters, projectile/weapon damage. Never use `float` for a damage or health value.
- `PlayerHealth` regen accumulates fractional HP in a private `float regenBuffer` and applies whole `int` points; `regenerationRate` stays `float` (a per-second rate). `HealthPercentage` casts to `float` for the division.

### Networking Pattern
- All networked scripts inherit `NetworkBehaviour` (Mirror)
- Client-to-server: `[Command]`, server-to-client: `[ClientRpc]`, server-only: `[Server]`
- State synced via `[SyncVar]` with hooks
- Position synced via `NetworkTransformUnreliable` on player and car prefabs
- Server validates all game-state mutations; clients send intent via Commands

### Player System
- `PlayerInputHandler` is the sole input router (Input System **Send Messages** mode: `OnMove`, `OnLook`, `OnJump`, `OnCrouch`, `OnInteract`, `OnReload`, `OnPrevious`, `OnNext`)
- Only local player (`isOwned`) processes input; remote players have `PlayerInput` disabled in `OnStartClient`
- `PlayerController` uses `CharacterController` — disabled for remote players (NetworkTransform handles their position)
- `PlayerController.SetVehicleMode(bool)` disables CharacterController + zeroes velocity when entering a vehicle
- `PlayerCarry` manages loot carrying; while carrying, LMB=drop, RMB=throw; normal shooting blocked
- `PlayerShooting` supports multiple `WeaponData` slots with per-slot saved ammo; optimistic local weapon switch + server SyncVar confirmation

### Vehicle System
- `CarController` is a `NetworkBehaviour` on the car prefab
- **Arcade movement**: `rb.MovePosition()` in FixedUpdate; velocity tracked in `currentVelocity` (do NOT use `rb.linearVelocity` — network sync overwrites it)
- **Enter/exit flow**: Interact key → `CmdEnterVehicle` → `car.ServerEnter()` → `netIdentity.AssignClientAuthority(conn)` → `[SyncVar] driverNetId` hook hides/shows driver on all clients
- Vehicle has health (`vehicleHealth` SyncVar); enemies and barrels call `car.TakeDamage()`
- `CargoTrigger` child (trigger collider, no Rigidbody) calls `car.ServerDepositLoot()` when a carried object enters — adds to `ScoreManager`

### Weapon System
- `WeaponData` ScriptableObject: `Damage`, `Range`, `FireRate`, `MagazineSize`, `MaxReserveAmmo`, `ReloadTime`, `BaseSpread`, `AdsSpreadMultiplier`, `MovementSpreadMultiplier`, `IsAutomatic`, `IsMelee`, `MeleeRadius`, recoil pattern, `ImpactEffectPrefab`
- Ranged: Raycast on server; `RpcOnHit` spawns impact effect on all clients
- Melee: SphereCast for wide sweep; no ammo consumed
- Weapon switch: optimistic local model swap → `CmdSwitchWeapon` → SyncVar hook confirms on all clients

### Enemy System
- `Enemy` (NetworkBehaviour): SphereCollider trigger for detection, server-only Update loop
- Targets either `PlayerHealth` (on foot) or `CarController` (driven vehicle)
- Fires `EnemyProjectile` via `NetworkServer.Spawn`; optional `keepAttackAfterTriggerExit` flag
- `DestructionObjective` monitors an array of `DestructionTarget`s; fires a UnityEvent when enough are destroyed

### Hazard System
- `BarrelSpawner`: server-side object pool (`poolSize` pre-spawned barrels). Returns barrel to pool after despawn delay
- `Barrel`: kinematic Rigidbody, moves via `rb.MovePosition()`; bounces using an `AnimationCurve`; damages car or player on trigger contact, then deactivates

### Interaction System
- `InteractableObject`: hold-E with configurable `HoldDuration`; progress tracked in `PlayerInputHandler`; on completion `CmdInteract` → `ServerInteract` → `RpcOnInteracted` → UnityEvent fires on all clients
- `CarryableObject`: pickup assigns client authority; drop/throw removes it; `SetCarriedLocally` gives instant feedback; SyncVar confirms on other clients

### Score / Objective
- `ScoreManager`: singleton `NetworkBehaviour`; `[SyncVar] totalScore`; `AddScore(int)` called server-side when loot deposited via `CargoTrigger`

## Important Lessons

### Mirror + Rigidbody Sync
- `NetworkRigidbodyUnreliable` overwrites `rb.linearVelocity` every frame — do NOT use it with manual velocity control
- Use `NetworkTransformUnreliable` + `rb.MovePosition()` for physics objects with manual movement
- Track velocity in a plain `Vector3` field instead of `rb.linearVelocity`

### Client Authority Pattern
- Car and CarryableObject both use `netIdentity.AssignClientAuthority(conn)` / `RemoveClientAuthority()`
- Always validate on the server that the requesting connection actually holds authority before acting

### Animator + NavMeshAgent / Manual Movement
- Always disable **Apply Root Motion** on any enemy Animator that uses NavMeshAgent or manual transform movement — root motion overrides `transform.position` and `transform.rotation` every frame, fighting both NavMeshAgent pathfinding and manual `FaceTarget` rotation
- With root motion disabled, the Animator drives only visual bone poses; all locomotion is code-controlled

### Input System
- `PlayerInput` in Send Messages mode — callback methods must be named `On<ActionName>(InputValue value)`
- Disable `PlayerInput` on remote players in `OnStartClient` (not `OnStartAuthority`) to avoid double-processing

## Current State (2026-05-12)

See `PROJECT_STATE.md` for the full system-by-system inventory. Summary:

### Working
- Core gameplay loop: join → drive convoy car → carry loot into cargo (`CargoTrigger`) → `ScoreManager` updates
- FPS player: movement/crouch/sprint/jump, camera (ADS FOV, recoil, head bob), multi-weapon shooting (ranged + melee, per-slot ammo, reload, optimistic switch), health/damage/regen/death
- Downed state + teammate revive (hold E): bleedout deadline paused during a revive, enemies ignore downed players, collapsed pose + marker synced to all clients
- Match defeat when the whole squad is down/dead (`GameStateManager`): input locked on every client, `MatchEndUI` with a host-only restart
- Match victory: drive the car into `ExtractionZone` and hold it there for `captureDuration` seconds; `MatchEndUI` shows the collected loot score
- Carry/throw loot props (client-authority pickup)
- Vehicle: arcade driving, authority transfer on enter/exit, vehicle health → destruction (fire + explosion FX, kills driver), collision damage, driverless backward drift
- Enemy turrets shoot at players and driven vehicles; barrel hazard spawner (server pool) on Road scene
- Destruction objective system (`DestructionObjective` / `DestructionTarget`)
- Hold-E interactables, `WindZone` push, `KillVolume` instant-kill
- Floor-cutting tool: runtime mesh hole (`FloorMeshModifier`) + `Safe` falling through (`SafeFallTrigger`, `SafeMount` car attach)
- `LocalPlayerBodyHider` (hide own body from FP camera)
- ZeroTier for remote peers; `NetworkDebugUI` connection overlay; F9 scene hot-reload (`SceneReloader`)

### In progress
- **Vehicle remote control** (`RemoteVehicleControlTool` + `CarController` autopilot: `ServerStartAutopilot`/`ServerStopAutopilot`, `autopilot*` SyncVars, L-path `ServerTickAutopilot`). Code complete; not yet wired into `Player.prefab` / `PlayerShooting.weaponTools[]`. See PROJECT_STATE.md §4.

### Exists in code but NOT integrated
- `ZombieEnemy` — full NavMesh state machine, not spawned in any scene
- Grappling hook (`GrapplingHookController` / `GrapplingHookInput`) — **local-only prototype, no networking**: only the owner's resulting position syncs (via NetworkTransform); grapple state, target, rope visual, and server validation are absent
- `NetworkMessages.cs` structs — declared, never sent/handled
- `WeaponBase` abstract class — unused; `PlayerShooting` does firing inline

### Known gaps
- No respawn flow (`PlayerHealth.Respawn` exists server-side, no caller)
- `MatchStateMessage` / `MatchState` in `NetworkMessages.cs` remain unused (`GameStateManager` uses its own `MatchResult`)
- Vehicle destruction has no wreck/persistent state beyond explosion FX + `vehicleIsAlive`
- `Enemy.OnAliveChanged` hook empty (no turret death visuals)
- `Safe` has no loot drop / visual change on destruction
- UI stubs: `PauseMenu.OpenSettings`/`Disconnect`, `GameOverUI.OnRestartButtonClick`
- `GameSettings` not networked / not persisted per client
- `PlayerSpawnManager.FurthestFromPlayers` ignores `minSpawnDistance`
- WIP scenes (untracked): `Test.unity`, `WagonsConstruct.unity`, `Wall Laser.unity`, `GrapplingHook_Test.unity`
