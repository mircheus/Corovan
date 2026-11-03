# EnemyCar

Вражеские машины. Устроены как **контроллер + поведение**: `EnemyCarController` — ядро (здоровье, смерть, цели), а что машина делает с целью, решает один `EnemyCarBehaviour` на том же объекте. Новый тип врага = новый класс поведения + копия префаба (не variant).

Префабы в `Assets/Game/Prefabs/Enemies/Cars/`:

| Префаб | Поведение | Роль |
|---|---|---|
| `EnemyCar.prefab` | `GunnerCarBehaviour` | догоняет и стреляет из турели |
| `EnemyCarObstacleLayer.prefab` | `ObstacleLayerCarBehaviour` | безоружная, держится впереди и бросает препятствия |
| `EnemyCarAggroZone.prefab` | `EnemyAggroZone` | триггер-зона, регистрирующая цели у списка машин |

Скрипты: `Enemies/EnemyCarController.cs`, `EnemyCarBehaviour.cs`, `GunnerCarBehaviour.cs`, `ObstacleLayerCarBehaviour.cs`, `EnemyAggroZone.cs`; препятствие — `Hazards/CarEjectionTrigger.cs` + `CarPushTrigger.cs`.

---

## Ядро (`EnemyCarController`, одинаково для обоих префабов)

### Что умеет
- **Цели — машины игроков** (`CarController`), а не пешие игроки. Кандидаты попадают в список двумя путями: `EnemyAggroZone` (машина въехала в триггер) и необязательный опрос `proximityAggroRadius`.
- **Выбор цели:** ближайшая живая машина-кандидат. Если водитель вышел, действует `onDriverExit`: `ShootPlayer` (целиться в вышедшего игрока, запоминается в момент выхода) или `ShootCar` (продолжать по брошенной машине). В префабах стоит `1` = `ShootCar`.
- **Агро на урон** (`aggroOnDamage`): простаивающую машину, по которой попал игрок, она берёт в цель — его машину, если он за рулём, иначе его самого пешком.
- **Смерть:** эффекты разрушения (RPC, чтобы поздние игроки не видели вспышку), все меши получают `burntMaterial`, дочерние объекты из `hideOnDeath` отключаются, включаются горящие эффекты (это состояние, поэтому поздние игроки видят огонь). Обломок запускается через `RoadBody` (профиль `EnemyCarWreck`): подбрасывается вверх, садится на дорогу и дрейфует назад, убирается на `despawnZ = -160`.
- **Эффекты попаданий:** искры/дым на каждом нелетальном попадании, не чаще `damageEffectCooldown` (0.1 с).
- Корень машины не вращается (`updateRotation = false`); поворачиваться может только часть, например турель.

### Настройки (одинаковы в обоих префабах)

| Параметр | Значение | Смысл |
|---|---|---|
| `maxHealth` | 300 | перекрывается Balance (см. ниже) |
| `aggroOnDamage` | да | |
| `onDriverExit` | `ShootCar` | |
| `launchUpSpeed` | 6…9 | подброс обломка, м/с |
| `launchSideSpeed` | 1.5 | боковой разброс по X |
| `launchSpin` | 1.5 | вращение, рад/с |
| `damageEffectCooldown` | 0.1 с | |
| `proximityAggroRadius` | **0** (Gunner) / **70** (ObstacleLayer) | радиус опроса по плоскости; 0 = выключено |
| `proximityLoseRadius` | 0 / **100** | за этим расстоянием найденная опросом машина забывается |
| `proximityScanInterval` | 0.5 с | |

`EnemyCarWreck.asset` (RoadBody): `mu 1.2`, `dragCoefficient 0.5`, `angularDamping 0.3`, `maxAngularVelocity 20`, `settleEpsilon 0.35`, `settleFrames 10`, `despawnZ -160`.

### Balance (секция `enemyCar` в `BalanceConfig`)
Применяется на сервере в `OnStartServer`: `maxHealth` в контроллере, а `projectileDamage` / `projectileSpeed` — в поведении (`ServerApplyBalance`). Пушечная машина использует оба, у слоя препятствий этих параметров нет.

| Конфиг | maxHealth | projectileDamage | projectileSpeed |
|---|---|---|---|
| `Balance_Default` | 300 | 10 | 20 |
| `Balance_Test` | 300 | 10 | 20 |

В `.asset`-файлах обоих конфигов секции `enemyCar` нет (ассеты сохранены до добавления поля), поэтому Unity подставляет значения по умолчанию из `EnemyCarBalance` — они совпадают с префабом. Отредактируешь значения в инспекторе — секция запишется в ассет и начнёт перекрывать префаб.

---

## Gunner — `EnemyCar.prefab` (`GunnerCarBehaviour`)

### Что умеет
- Едет по NavMesh к позиции цели и останавливается за `stopDistance` до неё (не таранит); путь пересчитывается каждые `repathInterval`.
- Турель (`gunPivot`) поворачивается к цели по Y со скоростью `gunRotationSpeed`; угол синхронизируется (`gunYaw` SyncVar), так что все видят, как турель следит.
- Стреляет `EnemyCarProjectile` (variant `Projectile.prefab` с `EnemyCarProjectileFx`) из `firePoint`, пока цель ближе `attackRange`. Вспышка у дула — через RPC. Снаряд игнорирует коллайдеры самой машины.
- Без цели просто сбрасывает путь и стоит.

### Настройки

| Группа | Параметр | Значение |
|---|---|---|
| Движение | `moveSpeed` | 8 |
| | `acceleration` | 12 |
| | `angularSpeed` | 0 (корень не поворачивается) |
| | `stopDistance` | **25** (по умолчанию в коде 12) |
| | `repathInterval` | 0.25 с |
| Бой | `fireRate` | 0.5 выстр./с (раз в 2 с) |
| | `attackRange` | 30 |
| | `projectileDamage` | 10 |
| | `projectileSpeed` | 20 |
| Турель | `gunRotationSpeed` | 180°/с |
| NavMeshAgent | radius / height / accel / angular speed | 3.1 / 1.99 / 8 / 120 (скорость, ускорение и stopping distance затем задаёт поведение) |

Снаряд (`EnemyProjectile`): `lifetime` 5 с; урон и скорость задаёт машина при выстреле.

---

## Obstacle layer — `EnemyCarObstacleLayer.prefab` (`ObstacleLayerCarBehaviour`)

### Что умеет
- Безоружная. Не строит путь: каждый кадр двигается `agent.Move`, держась **впереди цели по Z** и выравниваясь с ней по X.
- Всё меряется от **начальной точки** (позиция машины в `OnStartServer`), с жёсткими границами по осям — как бы далеко ни уехал игрок, машина не выйдет за «коробку».
- По Z: желаемое место = Z цели + `preferredZOffset`, обрезанное диапазоном; собственная скорость догоняет скорость цели (измеряется дифференцированием позиции) с ограниченным ускорением `zAcceleration` — это и есть «ленивость», машина не копирует игрока жёстко. Внутри `zDeadZone` разница не корректируется.
- По X: подстраивается под цель в пределах диапазона.
- Без цели возвращается в начальную точку со скоростью `returnSpeed`.
- **Бросает препятствия** `RoadObstacleSpawned.prefab` каждые `spawnInterval` за кормой, со случайным сдвигом по X в ±`spawnSpreadX`, лицом к -Z. Пока цель впереди машины (больше Z), ничего не бросает, а после того как цель оказывается позади — ждёт целый интервал.

### Настройки

| Группа | Параметр | Значение |
|---|---|---|
| Диапазон от нач. точки | `maxZForward` / `maxZBackward` | 40 / 20 |
| | `maxXRight` / `maxXLeft` | 8 / 8 |
| | `returnSpeed` | 6 |
| Следование по Z | `preferredZOffset` | 30 |
| | `zCorrectionSpeed` | 8 |
| | `zGain` | 0.4 |
| | `zDeadZone` | 4 |
| | `zAcceleration` | 4 м/с² |
| | `speedSmoothing` | 2 |
| Выравнивание по X | `lateralSpeed` | 8 |
| | `lateralAcceleration` | 16 |
| | `lateralGain` | 2 |
| Препятствия | `spawnInterval` | 2 с |
| | `obstacleSpeed` | 15 (в префабе препятствия 15 тоже; `SetSpeed` перекрывает его) |
| | `rearOffset` | 6 |
| | `spawnHeightOffset` | 1.5 |
| | `spawnSpreadX` | 4 |

Нюанс: `proximityLoseRadius` (100) должен быть больше максимального зазора, который держит поведение (30 + диапазон), иначе цель будет теряться.

### Препятствие — `RoadObstacleSpawned.prefab`
Сетевой корень с `NetworkIdentity`, `NetworkTransformUnreliable`, триггер-коллайдер 4.7×2.1×1, кинематический Rigidbody, `CarEjectionTrigger` и `CarPushTrigger`. Должно быть в `CustomNetworkManager.runtimeSpawnPrefabs`.

| Параметр | Значение |
|---|---|
| `speed` | 15 (перекрывается `obstacleSpeed` машины) |
| `travelDistance` | 60 |
| `launchOnStart` / `startDelay` | да / 0 |
| `ejectDriver` | **да** (см. ниже) |
| `disappearOnHit` | нет |
| `destroyOnFinish` | да |
| `CarPushTrigger.pushDelay` | 0.3 с (в коде) |

Толкает машину: `CarPushTrigger` работает на всех пирах, но `CarController.ApplyPush` действует только на пире, которому принадлежит машина (клиентская авторити). Пока идёт перекрытие, скорость машины в сторону препятствия гасится, и к её шагу добавляется скорость препятствия — машину несёт назад, пока игрок не вывернет или препятствие не исчезнет.

---

## EnemyAggroZone

Серверная зона: когда машина игрока входит в любой дочерний триггер, у каждого `EnemyCarController` из списка `enemies` вызывается `RegisterTarget`. `enableTriggerExit` (по умолчанию выкл.) — при выходе машины регистрация снимается. Префаб `EnemyCarAggroZone.prefab`: один BoxCollider-триггер (масштаб 31.5×1×1), `enableTriggerExit` выкл., список машин пуст — заполняется в сцене.

---

## Возможные проблемы
- **`ejectDriver: 1` на `RoadObstacleSpawned.prefab`.** В CLAUDE.md сказано, что препятствие должно толкать, а не выбрасывать водителя (`ejectDriver` off). В префабе флаг включён. Это либо нужно выключить, либо нужно поправить описание — реальное поведение я в игре не проверял.
- `stopDistance` 25 в префабе при `attackRange` 30: Gunner останавливается в 25 м, а стреляет с 30 — машина стреляет и по пути, и стоя.
- `ObstacleLayer`-префаб — копия Gunner-префаба со скрытой пушкой; в нём остаётся весь набор эффектов и материалов.
