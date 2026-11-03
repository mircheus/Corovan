# ShooterDroid

Пеший дроид-стрелок. Префаб: `Assets/Game/Prefabs/Enemies/Droids/ShooterDroid.prefab` (вариант `RagdollBot`).
Скрипты: `ShooterDroid.cs` (мозг, только сервер), `RagdollEnemy.cs` (здоровье, смерть, рэгдолл), `DroidAnimator.cs` (анимация), `ShooterDroidSpawner.cs`.

## Что умеет

- **Замечает** игрока пешком в `detectionRadius`; нужна прямая видимость (`requireSightToDetect`). Едущие в машине, лежащие (downed) и мёртвые игроки не цели (`IsTargetable`).
- **Идёт** по NavMesh к цели и останавливается на `standoffDistance`. Никогда не пятится от подошедшего игрока. Потеряв цель из виду, подходит вплотную (`closeInDistance`), чтобы обогнуть угол.
- **Стреляет на ходу** `EnemyProjectile` из `FirePoint` (между глаз), только при видимости цели и в пределах `fireRange`, с разбросом `spreadAngle`.
- **Снаряды проходят сквозь всех дроидов**, и проверка видимости их тоже игнорирует — иначе в стае стреляют друг в друга.
- **Агрится на урон**: простаивающий дроид, получивший выстрел от игрока, нападает на него откуда бы тот ни стрелял (`aggroOnDamage`).
- **Будит союзников** (опция `alertAllies`): соседи в `alertRadius` берут ту же цель, без цепной реакции.
- **Теряет цель** (опции): за `leashRadius`, либо если не видит дольше `loseSightTimeout` (`loseTargetOutOfSight`).
- **Смерть** — рэгдолл на всех клиентах; у этого префаба `respawnDelay = 0`, поэтому труп удаляется через `destroyDelay` (4 с), а не возрождается.
- Состояния (SyncVar): `Idle / Chasing / Attacking / Dead`. Анимация (Idle↔Walk) считается на каждом клиенте по скорости корня, ничего не синхронизируется.

## Настройки префаба

| Группа | Параметр | Значение |
|---|---|---|
| Здоровье | `maxHealth` (RagdollEnemy) | 100 (перекрывается Balance) |
| | `respawnDelay` / `destroyDelay` | 0 (переопределено в префабе) / 4 |
| Обнаружение | `detectionRadius` | 20 |
| | `requireSightToDetect` | да |
| | `retargetInterval` | 0.5 с |
| | `aimHeight` | 1.3 |
| Потеря цели | `leashRadius` | 35 |
| | `loseTargetOutOfSight` / `loseSightTimeout` | нет / 5 с |
| Агро | `aggroOnDamage` | да |
| | `alertAllies` / `alertRadius` | нет / 15 |
| Движение | `moveSpeed` (= NavMeshAgent speed) | 3.5 |
| | `standoffDistance` | 8 |
| | `closeInDistance` | 1.5 |
| | `rotationSpeed` | 8 |
| | `thinkInterval` | 0.2 с |
| Бой | `fireRate` | **1** выстрел/с (в коде по умолчанию 2, префаб переопределяет) |
| | `projectileDamage` | 8 (перекрывается Balance) |
| | `projectileSpeed` | 25 (перекрывается Balance) |
| | `fireRange` | 20 |
| | `spreadAngle` | 1.5° |
| | `sightMask` | всё |
| NavMeshAgent | radius / accel / angular speed / height | 0.35 / 12 / 360 / 1.8 |
| DroidAnimator | `maxWalkRate` / `speedSmoothTime` / `teleportDistance` | 2.2 / 0.15 / 2 |

### Balance (`SceneBalance.Current`, секция `shooterDroid`)

Применяется на сервере в `OnStartServer`; значения префаба остаются, если в сцене нет конфига.

| Конфиг | maxHealth | projectileDamage | projectileSpeed |
|---|---|---|---|
| `Balance_Default` | 100 | 8 | 25 |
| `Balance_Test` | 100 | 8 | 20 |

## ShooterDroidSpawner

Серверный спавнер. Префаб дроида должен быть в `CustomNetworkManager.runtimeSpawnPrefabs` (в `NetworkManager.prefab`). Значения ниже — дефолты из кода; конкретный объект в сцене может их менять.

| Группа | Параметр | По умолчанию | Смысл |
|---|---|---|---|
| Дроид | `droidPrefab` | — | что спавнить |
| Активация | `activation` | `WhilePlayersInZone` | `OnStart` — сразу; `WhilePlayersInZone` — пока игрок пешком в `activationZone`; `Manual` — `ServerStartSpawning/Stop` (можно из UnityEvent на любом пире) |
| | `activationZone` | — | любой Collider, только опрашивается (нужен для WhilePlayersInZone) |
| Спавн | `initialDelay` | 0 | пауза перед первым спавном |
| | `spawnInterval` | 3 с | период спавна, пока живых < `maxAlive` |
| | `maxAlive` | 4 | живых одновременно от этого спавнера (мёртвые освобождают слот) |
| | `totalToSpawn` | 0 | лимит за матч, 0 = без лимита |
| | `spawnPoints` | — | случайная точка; без них — позиция самого спавнера |
| | `navSampleRadius` | 2 | радиус привязки точки к NavMesh |
| | `spawnClearance` | 1.2 | точка занята, если рядом живой дроид — спавн ждёт свободную |
| Агро | `aggroOnSpawn` | да | сразу даёт дроиду ближайшего игрока пешком; перепроверяется каждый тик для простаивающих |
| | `aggroRadius` | 30 | без `activationZone`: на каком расстоянии от дроида ищется игрок (0 = везде) |

Нюансы:
- Зона опрашивается, а не триггерится: упавший (downed) в зоне игрок ставит спавн на паузу, после реанимации он продолжается.
- `ServerStopSpawning` останавливает только спавн, живые дроиды продолжают бой.
- Нужен запечённый NavMesh.
