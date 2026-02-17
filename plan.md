# Arcade Car Controller - Implementation Plan

## Overview
Rewrite the placeholder `CarController.cs` and `VehicleSync.cs` into a fully working arcade car with Mirror networking. Modify `PlayerInputHandler.cs` and `PlayerController.cs` to support enter/exit and input routing.

## Architecture

**Car prefab expected components:** `Rigidbody`, `NetworkIdentity`, `CarController` (NetworkBehaviour), `Collider`

Movement model: pure translation (no Y-axis rotation). WASD maps to world-relative or car-forward/strafe directions. The car itself never rotates from player input — it only translates.

### Files to modify/rewrite (4 files)

### 1. `CarController.cs` → full rewrite
- Inherits `NetworkBehaviour` (not MonoBehaviour)
- **Movement:** Rigidbody-based, `MovePosition` in `FixedUpdate`
  - Forward/back along `transform.forward`
  - Strafe along `transform.right`
  - No rotation applied
  - Speed, acceleration, deceleration parameters
- **Enter/Exit logic:**
  - `[Server] Enter(NetworkIdentity player)` — sets driver, grants client authority over car
  - `[Server] Exit()` — clears driver, removes client authority, teleports player to exit point
  - `[SyncVar] driverNetId` — so all clients know who's driving
- **Input:** received via `SetInput(Vector2)` from the driver's `PlayerInputHandler`
- **Exit point:** serialized `Transform exitPoint` for where the player appears on exit

### 2. `VehicleSync.cs` → delete or merge into CarController
Since `CarController` itself becomes a `NetworkBehaviour`, a separate sync script is unnecessary. All networking lives in `CarController`. The car's Rigidbody position is synced via Mirror's `NetworkTransformUnreliable` component on the prefab (same pattern as the player). We'll keep VehicleSync.cs but empty it out / leave a note, to avoid breaking any existing prefab references.

### 3. `PlayerInputHandler.cs` → add vehicle support
- Add reference to the car being driven (`CarController currentVehicle`)
- `OnInteract` (E key) — if near a car, enter it; if in a car, exit it
- When in a vehicle, route `OnMove` input to `CarController.SetInput()` instead of `PlayerController`
- Suppress shooting, jumping, crouching while in vehicle

### 4. `PlayerController.cs` → add enable/disable API
- Add `public void SetActive(bool active)` to enable/disable movement processing
- When entering car: disable CharacterController, hide player model (optional)
- When exiting car: re-enable CharacterController, reposition player at exit point

## Network Flow
1. Player presses E near car → `PlayerInputHandler` detects car via `Physics.OverlapSphere`
2. Calls `[Command] CmdEnterVehicle(carNetId)` on the player
3. Server validates, calls `CarController.Enter()`, assigns client authority to car's NetworkIdentity
4. Client now sends input to car; `NetworkTransformUnreliable` syncs car position to all clients
5. Player presses E again → `[Command] CmdExitVehicle()` → server calls `CarController.Exit()`, removes authority, repositions player

## What we do NOT change
- PlayerCamera.cs — camera stays parented to the player; since the player will be parented to the car's seat, camera follows automatically
- No new files created — we only modify existing ones
