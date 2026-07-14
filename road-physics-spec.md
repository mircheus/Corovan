# Спецификация: физика объектов на «движущейся» дороге (Unity + Mirror)

## 0. Контекст проекта

Кооперативный шутер про ограбление конвоя фур. Мир **статичен**: грузовики стоят на месте, иллюзия движения создаётся скроллом текстуры дороги, движением придорожных пропов и системой `MotionDirector` (фейковые силы тряски/сноса).

Задача: объекты (лут, бочки, обломки), падающие с кузова на дорогу, должны **правдоподобно кувыркаться и уезжать назад**, синхронно у всех игроков, и оставаться геймплейно значимыми (бочка может взорваться под преследователем).

---

## 1. Ключевая концепция: дорога — это конвейерная лента

Всё построение стоит на одном переходе системы отсчёта.

Мы находимся в системе отсчёта грузовика. В ней **дорожное полотно движется** со скоростью

```
V_road = (0, 0, -V)      // V — скорость конвоя, м/с
```

Объект, упавший на полотно, — это объект, упавший на движущуюся ленту. Дальше физика делает всё сама:

1. **Скольжение.** У точки контакта есть скорость относительно ленты.
2. **Трение.** Кулоновская сила трения гасит скольжение.
3. **Момент.** Сила трения приложена **в точке контакта**, смещённой от центра масс → возникает крутящий момент → **объект начинает кувыркаться от той грани/ребра, которой приземлился**. Это не надо скриптовать, это следствие.
4. **Затухание.** Когда объект разгонится до скорости ленты, скольжение → 0, трение → 0, кувырок затухает, объект просто ровно уезжает назад. (Физически: в системе отсчёта земли он остановился и лежит.)

> **Не пытаться скриптовать вращение вручную.** Любая попытка «задать правильный кувырок» руками ломает эмерджентность и будет выглядеть искусственно. Вся задача — правильно приложить трение в правильной точке.

---

## 2. Архитектура

| Компонент | Где живёт | Ответственность |
|---|---|---|
| `RoadScroll` | синглтон, `NetworkBehaviour` | Единственный источник истины о «пройденной дистанции». Аналитическая функция от `NetworkTime`. |
| `RoadSurface` | на коллайдере дороги | Отдаёт скорость ленты и высоту неровностей в точке. |
| `RoadBody` | на падающих объектах | Фазовая машина + приложение сил + сетевой режим. |
| `MotionDirector` | существует | Тряска фуры. **Интегрируется**: берёт неровности из того же поля, что и `RoadBody`. |

---

## 3. `RoadScroll` — часы иллюзии

### Проблема

Наивная реализация `distance += V * Time.deltaTime` **недопустима в мультиплеере**: локальная интеграция разъедется между клиентами за минуты. А от этой дистанции зависят скролл текстуры, поле неровностей, тряска фуры и экстраполяция уехавших объектов — разъедется всё сразу.

### Решение

Дистанция выводится **аналитически** из серверного времени. Синхронизируются только точки излома скорости.

```csharp
public class RoadScroll : NetworkBehaviour
{
    public static RoadScroll Instance { get; private set; }

    [SyncVar] double _t0;     // NetworkTime.time последней смены скорости
    [SyncVar] double _d0;     // накопленная дистанция на момент _t0
    [SyncVar] float  _speed;  // текущая V, м/с

    /// Пройденная конвоем дистанция. Одинакова на сервере и всех клиентах.
    public double Distance => _d0 + _speed * (NetworkTime.time - _t0);

    public float Speed => _speed;

    /// Скорость ленты в мировых координатах (система отсчёта грузовика).
    public Vector3 BeltVelocity => new Vector3(0f, 0f, -_speed);

    void Awake() => Instance = this;

    [Server]
    public void SetSpeed(float newSpeed)
    {
        _d0 = Distance;          // зафиксировать накопленное ДО смены
        _t0 = NetworkTime.time;
        _speed = newSpeed;
    }

    /// Перевод мировой Z в Z «на земле» (в системе отсчёта дороги).
    public double ToGroundZ(float worldZ) => worldZ + Distance;
}
```

**Требования:**
- `SetSpeed` вызывается **только на сервере** и **только** при реальном изменении скорости конвоя (разгон, торможение, скриптовые события). Не каждый кадр.
- Всё остальное в игре, что зависит от «движения» (материал дороги, спавн пропов, MotionDirector, RoadBody), читает `RoadScroll.Instance.Distance`. Ни у кого не должно быть своего аккумулятора.

---

## 4. `RoadSurface` — лента и неровности

```csharp
public class RoadSurface : MonoBehaviour
{
    [Header("Неровности")]
    [SerializeField] float _bumpAmplitude = 0.06f;   // м
    [SerializeField] float _bumpScaleZ    = 3.0f;    // м, длина волны вдоль движения
    [SerializeField] float _bumpScaleX    = 2.0f;    // м, поперёк
    [SerializeField] float _lateralDrift  = 0f;      // м/с, задаётся MotionDirector при swerve

    /// Скорость полотна в данной точке. X-компонента — снос при манёврах конвоя.
    public Vector3 GetSurfaceVelocity(Vector3 worldPos)
    {
        Vector3 v = RoadScroll.Instance.BeltVelocity;
        v.x += _lateralDrift;
        return v;
    }

    /// Профиль неровностей. Функция от координат В СИСТЕМЕ ОТСЧЁТА ЗЕМЛИ.
    /// Один и тот же ландшафт кормит и тряску фуры, и прыжки объектов.
    public float GetRoughness(float worldX, float worldZ)
    {
        double groundZ = RoadScroll.Instance.ToGroundZ(worldZ);
        float n = Mathf.PerlinNoise(
            worldX / _bumpScaleX,
            (float)(groundZ / _bumpScaleZ) % 1024f);
        return (n - 0.5f) * 2f * _bumpAmplitude;
    }

    [Server] public void SetLateralDrift(float driftMetersPerSecond) => _lateralDrift = driftMetersPerSecond;
}
```

### Почему `groundZ`, а не `worldZ`

- **Грузовик** стоит на месте (`worldZ` = const), но `groundZ` под ним растёт → шум меняется во времени → тряска. ✔
- **Упавший объект** разгоняется до скорости ленты; при `v = -V` его `groundZ` становится **константой** → шум перестаёт меняться → прыжки затухают сами. ✔ Физически верно.

Оба эффекта — следствие одной функции. `MotionDirector` **должен** брать свою вертикальную тряску из `GetRoughness(truckX, truckZ)`, а не из своего отдельного шума, иначе мир будет некогерентен (фуру трясёт на одном ландшафте, мусор скачет на другом).

### Внимание: фильтрация потребителей

`RoadSurface` **не должен** действовать на всё, что касается дороги. Только на объекты с `RoadBody`. В частности:

- **Преследователи на `WheelCollider`** ездят по этой же статичной дороге, но живут в другой системе отсчёта (они «едут» в кадре фуры на дельте скоростей). Конвейер их утянет назад — это баг.
- **Игроки** используют `CharacterController`, физику не получают вообще.

Реализация: логика конвейера живёт в `RoadBody.OnCollisionStay`, а не в `RoadSurface`. `RoadSurface` — пассивный поставщик данных.

---

## 5. `RoadBody` — ядро

### 5.1 Фазовая машина

```csharp
public enum RoadPhase
{
    OnTruck,   // в кузове; силы прикладывает MotionDirector; RoadSurface не действует
    InAir,     // отделился, летит; действует встречный поток воздуха
    OnRoad,    // контакт с дорогой; действует конвейерное трение + неровности
    Settled    // догнал ленту; сервер прекращает репликацию, клиенты экстраполируют
}
```

**Переходы:**

| Из | В | Условие |
|---|---|---|
| `OnTruck` | `InAir` | Объект покинул триггер кузова / был выброшен |
| `InAir` | `OnRoad` | `OnCollisionEnter` со слоем `Road` |
| `OnRoad` | `Settled` | Скольжение относительно ленты `< settleEpsilon` в течение `settleFrames` подряд фиксед-шагов, при наличии контакта с дорогой |
| `Settled` | `OnRoad` | `OnCollisionEnter` с чем-либо, кроме дороги (преследователь въехал в бочку, взрыв рядом) |
| любая | — | Despawn при `worldZ < despawnZ` |

Фаза — `SyncVar` с хуком. Она решает **три вещи сразу**: кто прикладывает силы, какой режим репликации, и какие VFX играют клиенты.

### 5.2 Скелет

```csharp
[RequireComponent(typeof(Rigidbody))]
public class RoadBody : NetworkBehaviour
{
    [Header("Трение конвейера")]
    [SerializeField] float _mu = 0.6f;                 // коэффициент трения
    [SerializeField] float _maxAngularVelocity = 30f;  // рад/с, кламп

    [Header("Неровности")]
    [SerializeField] float _bumpImpulseScale = 1.0f;   // множитель вертикальных толчков

    [Header("Встречный поток")]
    [SerializeField] float _dragCoefficient = 0.02f;   // квадратичный драг по воздуху

    [Header("Settle")]
    [SerializeField] float _settleEpsilon = 0.35f;     // м/с
    [SerializeField] int   _settleFrames  = 10;

    [Header("Despawn")]
    [SerializeField] float _despawnZ = -160f;

    [SyncVar(hook = nameof(OnPhaseChanged))] RoadPhase _phase = RoadPhase.OnTruck;

    // данные для аналитической экстраполяции в Settled
    [SyncVar] Vector3 _settledPos;
    [SyncVar] Quaternion _settledRot;
    [SyncVar] double _settledDistance;

    Rigidbody _rb;
    RoadSurface _road;
    int _settleCounter;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.maxAngularVelocity = _maxAngularVelocity;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public override void OnStartClient()
    {
        if (!isServer) _rb.isKinematic = true;   // клиенты — чистые прокси
    }

    // ---------- СЕРВЕР ----------

    [ServerCallback]
    void FixedUpdate()
    {
        switch (_phase)
        {
            case RoadPhase.InAir:
            case RoadPhase.OnRoad:
                ApplyAirstream();
                break;
        }

        if (_phase != RoadPhase.Settled && transform.position.z < _despawnZ)
            Despawn();
    }

    [ServerCallback]
    void OnCollisionStay(Collision c)
    {
        if (_phase != RoadPhase.OnRoad) return;
        if (!c.collider.TryGetComponent(out RoadSurface road)) return;

        float dt = Time.fixedDeltaTime;
        float maxSlip = 0f;

        for (int i = 0; i < c.contactCount; i++)
        {
            ContactPoint contact = c.GetContact(i);
            Vector3 n = contact.normal;

            // Нормальная сила — берём ту, которую РЕАЛЬНО разрешил PhysX на прошлом шаге.
            float N = Mathf.Abs(Vector3.Dot(c.impulse, n)) / dt / c.contactCount;

            // Скольжение точки контакта относительно ленты, спроецированное на плоскость контакта.
            Vector3 pointVel = _rb.GetPointVelocity(contact.point);
            Vector3 beltVel  = road.GetSurfaceVelocity(contact.point);
            Vector3 slip     = Vector3.ProjectOnPlane(pointVel - beltVel, n);

            maxSlip = Mathf.Max(maxSlip, slip.magnitude);
            if (slip.sqrMagnitude < 1e-4f) continue;

            // Кулоновское трение В ТОЧКЕ КОНТАКТА — отсюда рождается кувырок.
            Vector3 F = -slip.normalized * (_mu * N);
            _rb.AddForceAtPosition(F, contact.point, ForceMode.Force);

            // Неровности: вертикальный толчок, тоже в точке контакта (→ доп. момент).
            float rough = road.GetRoughness(contact.point.x, contact.point.z);
            float bump = rough * slip.magnitude * _bumpImpulseScale;
            if (bump > 0f)
                _rb.AddForceAtPosition(n * bump, contact.point, ForceMode.Impulse);
        }

        TrySettle(maxSlip);
    }

    [Server]
    void ApplyAirstream()
    {
        // В системе отсчёта фуры на всё в воздухе дует ветер -V.
        Vector3 airVel = RoadScroll.Instance.BeltVelocity;
        Vector3 rel = _rb.linearVelocity - airVel;
        _rb.AddForce(-rel * (rel.magnitude * _dragCoefficient), ForceMode.Force);
    }

    [Server]
    void TrySettle(float slip)
    {
        if (slip > _settleEpsilon) { _settleCounter = 0; return; }
        if (++_settleCounter < _settleFrames) return;

        _settledPos = transform.position;
        _settledRot = transform.rotation;
        _settledDistance = RoadScroll.Instance.Distance;
        SetPhase(RoadPhase.Settled);
    }

    [ServerCallback]
    void OnCollisionEnter(Collision c)
    {
        bool isRoad = c.collider.GetComponent<RoadSurface>() != null;

        if (_phase == RoadPhase.InAir && isRoad)
            SetPhase(RoadPhase.OnRoad);

        // Пробуждение: преследователь въехал в лежащий объект / взрыв рядом.
        else if (_phase == RoadPhase.Settled && !isRoad)
        {
            _rb.isKinematic = false;
            SetPhase(RoadPhase.OnRoad);
        }
    }

    [Server]
    void SetPhase(RoadPhase p)
    {
        _phase = p;
        _settleCounter = 0;
        // TODO: включить/выключить NetworkRigidbodyUnreliable — см. §6
    }

    // ---------- КЛИЕНТ ----------

    void OnPhaseChanged(RoadPhase _, RoadPhase now)
    {
        // TODO: VFX (пыль, искры, скрежет) — клиенты играют сами, без отдельных RPC
        // TODO: переключить режим репликации
    }

    [ClientCallback]
    void Update()
    {
        if (isServer || _phase != RoadPhase.Settled) return;

        // Аналитическая экстраполяция: объект неподвижен относительно земли,
        // а земля уезжает назад. RoadScroll.Distance одинаков у всех → расхождений нет.
        float dz = (float)(RoadScroll.Instance.Distance - _settledDistance);
        transform.SetPositionAndRotation(
            new Vector3(_settledPos.x, _settledPos.y, _settledPos.z - dz),
            _settledRot);
    }
}
```

---

## 6. Сеть (Mirror)

### 6.1 Модель авторитета

**Server-authoritative, без предикции.** Объект не управляется игроком, лаг в 100 мс на кувыркающемся мусоре невидим. Предикция здесь — чистая трата сложности.

Но объект **геймплейно значим** (бочка может взорваться под преследователем), поэтому чисто клиентская симуляция исключена: решение о дамаге обязано быть серверным.

- Сервер: `Rigidbody` не kinematic, все силы, все коллизии, все решения о дамаге.
- Клиенты: `isKinematic = true`, чистые визуальные прокси.

### 6.2 Двухрежимная репликация — главная оптимизация

**Режим A — фаза кувырка (`InAir` / `OnRoad`), длится ~1–2 секунды:**

`NetworkRigidbodyUnreliable`, `syncDirection = ServerToClient`, **включить `syncVelocity` и `syncAngularVelocity`**.

Именно unreliable-вариант: он экстраполирует между снапшотами по скорости. На 25 м/с и активном вращении без этого кувырок читается как стробоскоп.

**Режим B — фаза `Settled`:**

Объект догнал ленту → в системе отсчёта земли он **лежит неподвижно**. Его положение в кадре фуры становится чистой арифметикой от `RoadScroll.Distance`, которая и так одинакова у всех.

→ Сервер отправляет финальный снапшот (через `SyncVar` `_settledPos` / `_settledRot` / `_settledDistance`), **выключает компонент синхронизации** и **больше не шлёт ничего**. Клиент экстраполирует аналитически (см. `Update()` выше), кадр в кадр совпадая с сервером.

Rigidbody на сервере при этом **продолжает жить** и участвовать в коллизиях.

**Пробуждение:** `OnCollisionEnter` с не-дорогой → фаза возвращается в `OnRoad`, компонент синхронизации включается обратно.

**Итог:** трафик тратится ровно на драматические моменты. Вместо ~20 объектов × 30 Гц постоянно — 2–3 активных в любой момент времени. Разница на порядок.

### 6.3 VFX без RPC

Клиенты спавнят пыль/искры/звук **сами**, из хука `OnPhaseChanged` + локально известной `RoadScroll.Speed`. Отдельные `ClientRpc` на эффекты не нужны.

### 6.4 Пулинг

`NetworkClient.RegisterSpawnHandler(assetId, spawnHandler, unspawnHandler)` + серверный пул. Прямые `NetworkServer.Spawn` / `Destroy` на десятках объектов за рейд дадут GC-пилу и мусор в таблицах `NetworkIdentity`.

### 6.5 Interest management

Дистанционный, но с оглядкой на геймплей: **преследователи сзади**, игроки будут смотреть назад. Радиус отсечения — честные **~150 м назад**, а не «за кабиной».

---

## 7. Настройки физики (критично)

| Что | Значение | Почему |
|---|---|---|
| PhysicMaterial дороги: `dynamicFriction`, `staticFriction` | **0** | Штатное трение PhysX тянет объект к покою относительно **статического** коллайдера, то есть работает **против** конвейера. Всё трение считаем руками. |
| PhysicMaterial дороги: `bounciness` | 0.1–0.2 | Основные прыжки даёт поле неровностей, не отскок. |
| `Rigidbody.collisionDetectionMode` на `RoadBody` | `ContinuousDynamic` | На 25 м/с при 50 Гц объект пролетает 0.5 м за фиксед-шаг. Туннелирование гарантировано. |
| То же на **преследователях** | `ContinuousDynamic` | Сближение на встречных скоростях — самый опасный случай, и это ровно момент взрыва бочки. Его нельзя терять. |
| `Rigidbody.maxAngularVelocity` | ~30 рад/с | Дефолтные 7 рад/с задушат кувырок. Но без верхнего клампа объект уйдёт в блендер. |
| `Rigidbody.angularDamping` | 0.05–0.15 | Чуть-чуть, чтобы кувырок затухал читаемо. |
| Инерцию (`inertiaTensor`) | **не переопределять** | Пусть PhysX считает из коллайдера — от неё зависит характер кувырка. |
| Слои | `Road`, `RoadDebris`, `Truck`, `Pursuer` | `RoadDebris` не должен коллайдить с игроками на кузове. |

---

## 8. Порядок реализации

Каждый шаг оставляет проект запускаемым и проверяемым.

### Шаг 1 — `RoadScroll` + `RoadSurface` (пассивные)
Только данные. Проверка: `Distance` растёт линейно, идентична на хосте и клиенте (залогировать разницу за 5 минут — должна быть ~0).

### Шаг 2 — `RoadBody`: конвейерное трение, **одиночная игра**
Гейт `[ServerCallback]` уже стоит, но тестируем в host-режиме с одним игроком.

**Критерий приёмки:** сбросить куб под углом в редакторе → он должен кувыркнуться **от той грани, которой коснулся**, разогнаться назад и затухнуть. Если кувырка нет — проверить, что трение дороги действительно 0, и что сила прикладывается через `AddForceAtPosition`, а не `AddForce`.

### Шаг 3 — Фазовая машина + `Settled`
Проверка: объект перестаёт трястись, когда догнал ленту, и ровно уезжает.

### Шаг 4 — Грубый тюнинг ощущения (всё ещё в одиночку)
`_mu`, `_bumpAmplitude`, `_dragCoefficient`, `maxAngularVelocity`. Отдельно проверить, что лёгкие объекты (банки, тряпьё) уносит потоком мгновенно, а тяжёлые ящики — почти нет. Это бесплатная дифференциация материалов, она стоит того, чтобы её выкрутить.

> **Сначала добиться, чтобы куб красиво кувыркался вообще, и только потом раскладывать это по сети.** Иначе будешь отлаживать сетевой стек и физику ощущения одновременно — это два разных ада, и в паре они неразрешимы.

### Шаг 5 — Сеть: `NetworkRigidbodyUnreliable` для фазы кувырка
Проверка на двух билдах: кувырок читается плавно, без стробоскопа.

### Шаг 6 — Сеть: режим `Settled` + пробуждение
Проверка: после затухания трафик по объекту падает в ноль (посмотреть в Mirror'ском профайлере), позиции на клиенте и сервере не расходятся. Преследователь въезжает в лежащую бочку → она просыпается на всех клиентах.

### Шаг 7 — Пулинг + interest management
Стресс-тест: 20 объектов одновременно.

---

## 9. Что НЕ делать

- ❌ **Не скриптовать вращение при приземлении.** Оно эмерджентно. Любой ручной `AddTorque` «чтобы красиво крутилось» сломает связь между гранью касания и осью кувырка — то есть ровно ту вещь, ради которой всё затевалось.
- ❌ **Не оставлять трение на PhysicMaterial дороги.** Оно будет тихо бороться с конвейером, и тюнинг `_mu` превратится в гадание.
- ❌ **Не аккумулировать `distance += V * dt`** нигде, ни в одном скрипте.
- ❌ **Не кормить конвейером `WheelCollider` преследователей.**
- ❌ **Не делать клиентскую предикцию** для этих объектов.
- ❌ **Не реплицировать `Settled`-объекты.** Их движение — арифметика.
