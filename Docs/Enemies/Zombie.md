# Zombie

Ближний зомби: бежит к игроку по NavMesh и бьёт в ближнем бою. Префаб: `Assets/Game/Prefabs/Enemies/Zombie/Zombie.prefab`.
Скрипты: `ZombieEnemy.cs` (мозг + здоровье, namespace `FPSGame.Environment`), `EnemyFallThrough.cs` (падение в дыры в полу), `ZombieSpawner.cs`.

> Статус: в CLAUDE.md числится как «есть в коде, но не интегрирован». Спавнер завязан на `BreachConsole`, в префабе `console` пустой — сам по себе спавнер не запустится.

## Что умеет

- **Состояния** (SyncVar, управляют Animator-параметром `State`): `0 Idle`, `1 Chasing`, `2 Attacking`, `3 Dead`.
- **Обнаружение:** каждый тик, пока цели нет, ищет ближайшего игрока `OverlapSphere` в `detectionRadius` (15). Цели только `IsTargetable` — игрок пешком, живой (не в машине, не downed). Видимость **не** проверяется — замечает через стены.
- **Погоня:** `NavMeshAgent` к позиции цели, поворот к ней в плоскости. Цель дальше `detectionRadius` — теряется (`LoseTarget`) и зомби встаёт в Idle.
- **Удар:** в `attackRadius` (2) останавливается и бьёт раз в `attackCooldown` (1.5 с). Урон применяется через `attackDamageDelay` (0.5 с) после начала анимации и только если цель ещё в пределах `attackRadius × 1.3`. Первый удар по входу в атаку — сразу.
- **Командная цель:** `ServerSetTarget` (его зовёт спавнер) — зомби гонится за этим игроком с любого расстояния, пока тот не умрёт/не упадёт/не сядет в машину (`targetCommanded`).
- **Урон по нему:** `IDamageReceiver` (`TakeDamage(int)`), здоровье — SyncVar. Эффект попадания (`ServerPlayHitEffect` → RPC) берёт свободную из 4 `BloodSmallExplosion` частиц.
- **Смерть:** `isAlive = false`, Animator → `State = 3` (анимация смерти), коллайдер выключается, атака отменяется. Рэгдолла нет. Труп убирает спавнер.
- **Падение в дыры:** `EnemyFallThrough` — если под ногами нет пола дольше `fallGraceTime`, отключает агента и роняет зомби по гравитации; цель сбрасывается. В `FallHazardVolume` объект уничтожается.
- Чего **нет**: стрельбы, агро на урон, оповещения союзников, проверки видимости, баланса из `BalanceConfig` (значения только из префаба).

## Настройки префаба

| Группа | Параметр | Значение (префаб) | Код по умолчанию |
|---|---|---|---|
| Здоровье | `maxHealth` | **20** | 80 |
| Обнаружение | `detectionRadius` | 15 | 15 |
| | `attackRadius` | 2 | 2 |
| | `playerLayerMask` | все слои | все слои |
| Движение | `moveSpeed` | **6** | 4 |
| | `rotationSpeed` | 10 | 10 |
| Бой | `attackDamage` | 15 | 15 |
| | `attackCooldown` | 1.5 с | 1.5 с |
| | `attackDamageDelay` | 0.5 с | 0.5 с |
| Animator | `stateParameterName` | `State` | `State` |
| NavMeshAgent | radius / accel / angular speed / height | 0.5 / 8 / 120 / 2 | |
| Агент в рантайме | `stoppingDistance` | `attackRadius × 0.8` = 1.6 | |
| Коллайдер | CapsuleCollider | r 0.25, h 2.3 | |
| EnemyFallThrough | `floorMask` / `groundCheckDistance` / `groundCheckUpOffset` / `groundCheckRadius` | слои 0 и 10 (маска 1025) / 0.5 / 0.3 / 0.25 | |
| | `gravity` / `fallGraceTime` | 25 / 0.35 с | |

Модель: `SM_Chr_Zombie_Male_01`, масштаб 1.2, Apply Root Motion выключен. Rigidbody kinematic (нужен для триггера `FallHazardVolume`).

Анимации: `Assets/Game/Animations/Zombie/` (Idle, Walk, Running, Punching, Death; контроллер `Zombie.controller`).

## ZombieSpawner

Серверный спавнер, запускается **только пока хранилище `BreachConsole` открыто** (`console.IsVaultOpen`). Префаб зомби должен быть в списке спавнимых префабов NetworkManager. Значения ниже — из `ZombieSpawner.prefab`.

| Группа | Параметр | Префаб | Код | Смысл |
|---|---|---|---|---|
| Консоль | `console` | **пусто** | — | без неё спавнер выдаёт предупреждение и не работает |
| Зомби | `zombiePrefab` | `Zombie.prefab` | — | |
| Спавн | `spawnInterval` | 1 с | 2 с | период спавна, пока живых < `maxAlive` |
| | `maxAlive` | 15 | 8 | живых одновременно (мёртвые освобождают слот) |
| | `spawnPoints` | сам спавнер | — | случайная точка; пусто — позиция спавнера |
| | `navSampleRadius` | 2 | 2 | привязка точки к NavMesh |
| Агро | `aggroTrigger` | дочерний `AggroTrigger` (Box 10×5×5, смещён на +16 по Z) | — | игрок пешком внутри объёма сразу становится целью свежего зомби |
| | `playerLayerMask` | слой 7 (маска 128) | все | какие слои считаются игроками |
| Уборка | `despawnOnVaultClose` | нет | нет | убить живых при закрытии хранилища |
| | `despawnDeadDelay` | 5 с | 5 с | через сколько убрать труп (0 = не убирать) |

Нюансы:
- Агро-зона проверяется только в момент спавна: зомби, вышедший на свет, получает цель один раз; дальше он живёт по своему `detectionRadius`.
- Трупы чистятся при очередном `TrySpawn` (`PruneAndCleanup`), поэтому после остановки спавна (хранилище закрылось) оставшиеся трупы сами не удалятся.
- В отличие от `ShooterDroidSpawner` нет `totalToSpawn`, `initialDelay`, `spawnClearance` (зомби могут появляться стопкой в одной точке) и режимов активации.

## Возможные проблемы

- **`NetworkTransformUnreliable` на префабе имеет `syncDirection: 1` (ClientToServer)**, у дроида — 0 (ServerToClient). Зомби двигает только сервер, поэтому клиенты, скорее всего, не получают его позицию. Я это в игре не проверял — стоит проверить в редакторе на хосте + клиенте.
- `maxHealth` и `moveSpeed` в префабе (20 / 6) сильно отличаются от дефолтов в коде (80 / 4) — похоже на тестовые значения.
- В `TakeDamage` остался `Debug.Log` на каждое попадание.
