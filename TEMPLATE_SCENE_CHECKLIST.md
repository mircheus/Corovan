# Чек-лист: Template-сцена

Минимальная сцена, в которой работают: подключение игроков (Mirror), конец матча (победа / поражение), скроллящаяся дорога и лут, реагирующий на падение на неё.

Значения ниже совпадают с автоматически собранной `Assets/Game/Scenes/Template.unity`: её можно открыть рядом для сверки или удалить перед ручной сборкой.

Ориентация: **конвой едет в +Z**, лента дороги движется в −Z. `RoadBody` удаляет предметы при `z < −160`.

---

## 0. Сцена

- [ ] `File → New Scene → Empty`, сохранить как `Assets/Game/Scenes/<Имя>.unity`
- [ ] **Build Settings → Add Open Scenes.** Без этого не работает Restart: он вызывает `ServerChangeScene` по имени активной сцены
- [ ] **Main Camera не добавлять.** Камера (FPSF-риг) входит в `Player.prefab`

## 1. Окружение

- [ ] **Directional Light**, rotation `(50, −30, 0)`, Soft Shadows
- [ ] **EventSystem.** Модуль ввода заменить на `InputSystemUIInputModule` (проект на New Input System). Без EventSystem не нажимаются Host / Join / Restart

## 2. `_Core` — сеть и матч

Пустой объект `_Core`, внутрь:

- [ ] **NetworkManager** — `Prefabs/Network/NetworkManager.prefab`
  - [ ] `Spawn Points` → перетащить Spawn_1…4 (см. §4). **Обязательно руками:** автопоиск работает только при смене сцены, а не при прямом запуске
  - [ ] `Spawn Prefabs`: всё, что в этой сцене спавнится в рантайме (бочки, снаряды турелей). В базовом шаблоне список пустой. `NetGrenade` регистрируется самим префабом
- [ ] **GameStateManager** — `Prefabs/Core/GameStateManager.prefab`
  - [ ] `Win Condition Sources` → размер 1, перетащить `ExtractionZone` (см. §5)
- [ ] **ScoreManager** — `Prefabs/Core/ScoreManager.prefab`
- [ ] **RoadScroll** — `Prefabs/Core/RoadScroll.prefab` (`Start Speed` = 20)
- [ ] **MotionDirector** — `Prefabs/Environment/Physics/MotionDirector.prefab`
  - [ ] `Fall Respawn Point` → Spawn_1

## 3. `Road` — дорога

Пустой объект `Road`, внутрь:

- [ ] **RoadSurface** — 3D Cube
  - [ ] Position `(0, 0, 0)`, Scale `(24, 1, 320)`
  - [ ] Layer **Ground** (3)
  - [ ] BoxCollider → Material = `Assets/Game/Physics/Road.physicMaterial` (**трение 0**, иначе PhysX спорит с ручным трением `RoadBody`)
  - [ ] MeshRenderer → `Graphics/Materials/Road/Road_shader.mat`
  - [ ] Add Component → **RoadSurface**
  - [ ] Static ✔ (коллайдер дороги **не двигается**)

> ⚠️ `RoadSurface` ставится **только** на дорогу. На обычном полу лут перейдёт в `Settled` и поедет назад.

## 4. `Convoy` — платформа, спавн, лут

Пустой объект `Convoy`, внутрь:

- [ ] **Platform** — 3D Cube
  - [ ] Position `(0, 2, 0)`, Scale `(6, 0.5, 16)`, Layer Default
  - [ ] Add Component → **MotionPlatform** (без него игрок не чувствует качку конвоя)
- [ ] **SpawnPoints** — пустой объект, внутри 4 пустых:
  - [ ] Spawn_1 `(−1.5, 2.5, 3)`
  - [ ] Spawn_2 `(1.5, 2.5, 3)`
  - [ ] Spawn_3 `(−1.5, 2.5, 0)`
  - [ ] Spawn_4 `(1.5, 2.5, 0)`
- [ ] **FallHazard_Left** — `Prefabs/Environment/Physics/FallHazardVolume.prefab`, Position `(−5, 1.25, 0)`, Scale `(4, 1.5, 20)`
- [ ] **FallHazard_Right** — тот же префаб, Position `(5, 1.25, 0)`, Scale `(4, 1.5, 20)`
  - Урон + возврат на платформу при падении с конвоя. Если нужно ходить по дороге пешком, отключить оба
- [ ] **Loot** — пустой объект, внутри 3× `Prefabs/Environment/Carryable.prefab`:
  - [ ] `(0, 3, −4)`
  - [ ] `(−1.2, 3, −6)`
  - [ ] `(1.2, 3, −6)`

> Свой лут для дороги: `Rigidbody` + `RoadBody` + `NetworkTransformUnreliable` + `NetworkIdentity`; для переноски + `CarryableObject` (NT в **World**-пространстве). Образец: `Carryable.prefab`.

## 5. `Objective` — победа

Пустой объект `Objective`, внутрь:

- [ ] **Car** — `Prefabs/Player/Car.prefab`, Position `(8, 1.5, 10)`
- [ ] **ExtractionZone** — `Prefabs/Core/ExtractionZone.prefab`, Position `(8, 3.5, 45)`
  - [ ] `Capture Duration` = **5** (для тестов вместо 30)
  - [ ] `Require Driver` ✔ (засчитывается только машина с водителем)

> Без машины: убрать зону из `Win Condition Sources` и вызывать `GameStateManager.ServerTriggerVictory()` из UnityEvent (например, `InteractableObject`).

## 6. `_UI`

Пустой объект `_UI`, внутрь:

- [ ] **NetworkDebugUI** — `Prefabs/UI/NetworkDebugUI.prefab` (кнопки Host / Join по IP)
- [ ] **LocalPlayerDeadUI** — `Prefabs/UI/LocalPlayerDeadUI.prefab`
- [ ] **ExtractionUI** — `Prefabs/UI/ExtractionUI.prefab`
  - [ ] `Zone` → ExtractionZone (**в префабе пусто**)
- [ ] **MatchEndUI** — `Prefabs/UI/MatchEndUI.prefab`

---

## 7. Проверка

- [ ] Сохранить сцену. Нет ошибок Mirror про `sceneId` (у каждого сетевого объекта он уникальный, назначается при сохранении)
- [ ] **Host** (NetworkDebugUI): игрок появляется на Spawn_1, камера работает
- [ ] **Join** из ParrelSync-клона: второй игрок на Spawn_2, оба видят друг друга
- [ ] Платформа качается (MotionDirector), игрок чувствует сдвиг
- [ ] Столкнуть / бросить лут с платформы: он падает на дорогу, кувыркается и уезжает назад (−Z), а за `z < −160` удаляется
- [ ] Поднять лут с дороги: он берётся и синхронизируется у второго игрока
- [ ] Сойти с платформы: срабатывает FallHazard (урон + возврат)
- [ ] **Победа:** сесть в машину и заехать в ExtractionZone; полоска ExtractionUI 5 с → Victory + счёт лута
- [ ] **Поражение:** все игроки down/dead (например, урон от турели/бота и истечение bleedout, или `FallHazardVolume` с `Instant Kill`) → Defeat
- [ ] **Restart** у хоста перезагружает сцену, клиент остаётся подключён

## Грабли

- Скорость визуала дороги (`Road_shader`, `InfiniteRoad.scrollSpeed`) **не связана** с `RoadScroll.startSpeed`. При изменении одной скорости меняй и другую, иначе лут уезжает не синхронно с дорогой.
- `Spawn Prefabs` в инстансе NetworkManager **перекрывает** список префаба.
- Второй `AudioListener` / камера в сцене конфликтует с камерой игрока.
- Сетевые объекты, добавленные не из префабов, требуют `NetworkIdentity`.
