# Convoy Raid — физика движения: скрипты и сборка

Реализация системы «физика-как-угроза» из `convoy_raid_physics_v0.md`. Уровень — статичные платформы; движение фейкается одним сигналом, который читают платформа, игрок и лут.

Namespace: `FPSGame.Environment` / `FPSGame.Player`.

---

## Скрипты

| Скрипт | Куда вешать | Что делает |
|--------|-------------|------------|
| **`MotionDirector`** | один сценовый объект («хаб конвоя») | Единственный источник сигнала. Считает боковой (`LateralSignal`) и вертикальный (`VerticalSignal`) сигнал: idle-синусоида + дискретные `Swerve`/`Bump`. Реплицирует события server-auth, гоняет `AddForce` по луту, хранит телеграф и точку респавна. **Ничего сам не двигает.** |
| **`MotionReactive`** | на префаб лута (рядом с `CarryableObject`) | Регистрирует `Rigidbody` лута в `MotionDirector` на сервере — с этого момента лут ловит боковую силу и съезжает к краю. |
| **`MotionPlatform`** | на физический пол платформы (коллайдер, по которому ходит игрок) | Пустой маркер. По нему `PlayerController` понимает, что игрок стоит на конвое, и только тогда его качает. |
| **`PlatformMotionReceiver`** | на **визуальный** меш платформы | Косметика: кренит/подкидывает меш по сигналу + упреждающий крен в телеграфе. Локально, без сети. |
| **`PlayerMotionReceiver`** | на корень игрока | Подмешивает боковой сдвиг в `CharacterController.Move()`, пока игрок на `MotionPlatform`. Множитель при переноске лута. |
| **`CameraMotionShake`** | на корень игрока | Шейк камеры по сигналу: джиттер, крен в занос, вертикальный кик, гул в телеграфе. Гейтится по нахождению на платформе. |
| **`FallHazardVolume`** | на триггер-объём у края | Маркер опасной зоны: урон/инсталл при падении. |
| **`PlayerFallHazard`** | на корень игрока | Копит безопасную точку, ловит вход в `FallHazardVolume` → дроп лута + телепорт + урон. |

**Правки в существующих скриптах:**
- `PlayerController` — маркер `OnMotionPlatform` (резолвится в проверке земли), `AddExternalVelocity()`, `TeleportTo()`.
- `PlayerCamera` — `SetShakeOffsets()` (аддитивный шейк поверх headbob/recoil).
- `PlayerCarry` — `ForceDrop()` (сброс мимо флага `allowDrop`).

---

## Как это связано

```
                 ┌──────────────────┐
                 │  MotionDirector  │  один сигнал (idle + swerve + bump)
                 └────────┬─────────┘
        ┌─────────────────┼──────────────────┬───────────────┐
        ▼                 ▼                  ▼               ▼
 PlatformMotion   PlayerMotion /      MotionReactive    (телеграф →
 Receiver         CameraMotionShake   (AddForce, server)  крен/сирена)
 (крен меша)      (сдвиг + шейк,
                  только на MotionPlatform)
```

---

## Сборка объектов

### 1. Хаб конвоя (`MotionDirector`)
1. Пустой GameObject на сцене → добавь **`NetworkIdentity`** и **`MotionDirector`**.
2. (Опц.) `AudioSource` на этот же объект → в поля `Telegraph Audio` + `Telegraph Clip` брось источник и звук-сирену.
3. Поля: `Lateral Axis` = направление «вбок» (по умолч. X), `Telegraph Lead` ≈ 1, кривые/амплитуды свёрва и бампа, `Fall Respawn Point` (см. игрока).
4. Дебаг-клавиши (server): `J`/`K` — свёрв влево/вправо (с телеграфом), `L` — бамп.

### 2. Платформа
Два объекта — **физика отдельно от визуала**:
- **Физический пол** (коллайдер, non-trigger, слой в Ground Mask игрока) → повесь **`MotionPlatform`**. Его не наклоняем.
- **Визуальный меш** (дочерний/отдельный) → повесь **`PlatformMotionReceiver`**. Его кренит. Настрой `Roll/Pitch/Bounce Per Unit`, `Anticipatory Roll`.

### 3. Префаб лута
На существующий префаб с `CarryableObject` (у него уже есть `Rigidbody` + `NetworkIdentity` + `NetworkTransform`):
1. Добавь **`MotionReactive`**.
2. Всё — свободный (не несомый) лут теперь съезжает по платформе. Несомый лут кинематичен и игнорируется автоматически.

### 4. Префаб игрока (`Player.prefab`)
На корень (там же `PlayerController`/`PlayerHealth`/`PlayerCarry`) добавь три компонента:
1. **`PlayerMotionReceiver`** — `Push Scale`, `Carry Multiplier`, `Stay Grace`.
2. **`CameraMotionShake`** — камеру найдёт сам; `Position/Rotation Shake`, `Lean Into Swerve`, `Bump Kick`.
3. **`PlayerFallHazard`** — `Recovery Mode`:
   - `LastSafePoint` — вернуть на последнее безопасное место на платформе;
   - `FixedPoint` — вернуть на `MotionDirector.Fall Respawn Point` (задать сценовый Transform в хабе; при пустом поле — откат к LastSafePoint).

> Transform точки респавна ставится **в `MotionDirector`**, а не на игроке: игрок — спавн-префаб, сценовую ссылку в нём не сохранить.

### 5. Опасная зона (`FallHazardVolume`)
1. GameObject с **Box/Mesh Collider**, `Is Trigger` ✔ (при добавлении скрипта ставится авто).
2. Повесь **`FallHazardVolume`**: `Fall Damage` (урон) или `Instant Kill` ✔ (бездонная яма).
3. Размести полосой под/сбоку от края, куда занос может столкнуть. Слой должен коллайдиться с Player в Physics Matrix (проще — `Default`).

---

## Модель сети (кратко)
- **Idle-качание** — детерминированное из `NetworkTime.time`, реплики не нужны (все в фазе).
- **Swerve/Bump** — server-auth, рассылаются как timestamped `ClientRpc`; один свёрв несёт и телеграф, и сам толчок.
- **Лут** — `AddForce` только на сервере, позиция едет через `NetworkTransform`.
- **Косметика (крен, шейк)** — локально на каждом клиенте.
- **Падение** — детект и телепорт на стороне владельца (только он гоняет `CharacterController`), урон валидируется сервером через `Command`.
