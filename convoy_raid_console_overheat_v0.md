# ТЗ: механика перегрева пульта (Heat / Overheat) — v0

> Техническое задание для Claude Code. Стек: Unity, C#.
> Scope: **только** heat/overheat пульта и стейт-машина, которая его хостит. Индикаторы, открытие сейфа, агро и урезанная стрельба — вне scope, но пульт обязан отдавать события/состояние, на которые эти системы подпишутся (см. §5).

---

## 1. Контекст (минимум)

Пульт (`BreachConsole`) на грузовике A. Игрок вставляет ключ-карту в слот и удерживает её там. Пока карта в слоте и пульт не перегрет — идёт взлом, затем сейф на грузовике B открыт. Пока сейф открыт, копится **heat**; на пределе пульт **перегревается**, принудительно выбрасывает карту и блокируется на кулдаун.

Смысл механики — задать игроку **решение**: тянуть окно дальше (риск залочиться в худший момент) против вынуть карту раньше самому (сбросить heat на своих условиях, но напарник на B теряет окно выноса). Это создаёт бёрст-ритм «открыли → выгребли партию → закрыли → снова».

---

## 2. Стейт-машина

`enum ConsoleState { Idle, Breaching, Open, Overheat }`

| Состояние | Что значит | Сейф | Heat |
|---|---|---|---|
| `Idle` | карты в слоте нет | закрыт | падает к 0 |
| `Breaching` | карта вставлена, идёт warm-up | закрыт | падает к 0 |
| `Open` | сейф открыт | **открыт** | **растёт** |
| `Overheat` | принудительный лок после перегрева | закрыт | падает к 0 |

**Открытие сейфа = `State == Open`.** Пульт сам сейф не открывает — он лишь держит состояние (§5).

---

## 3. Переходы

| Из | В | Условие |
|---|---|---|
| `Idle` | `Breaching` | карта вставлена в слот |
| `Breaching` | `Open` | warm-up истёк, карта всё ещё в слоте |
| `Breaching` | `Idle` | карту вынули во время warm-up |
| `Open` | `Idle` | карту вынули (решение игрока / оператор в дауне / оператор ушёл) |
| `Open` | `Overheat` | `heat >= overheatThreshold` |
| `Overheat` | `Idle` | истёк `overheatCooldown` |

**Правила:**
- «Карту вынули» инициируется из трёх источников: действие игрока, даун оператора, выход оператора из зоны взаимодействия. Все три обрабатываются одинаково.
- В `Overheat` слот **заблокирован**: попытка вставить карту игнорируется до конца кулдауна. При входе в `Overheat` карта принудительно выбрасывается из слота.
- `heat` всегда клампится в `[0, 1]`.

---

## 4. Модель heat

`heat` — нормализованный `float` в `[0, 1]`.

- **Растёт** только в `Open`: `heat += heatRisePerSecond * dt`.
- **Падает** во всех остальных состояниях (`Idle`, `Breaching`, `Overheat`): `heat -= heatFallPerSecond * dt`.
- Достиг `overheatThreshold` в `Open` → немедленный переход в `Overheat`.

**Важное следствие (желаемое поведение, не баг):** heat падает, но не мгновенно. Если игрок вынул карту и сразу вставил обратно, heat не успел упасть до нуля → следующее окно короче. Это естественно поощряет паузу между бёрстами: не дал остыть — окна укорачиваются. Тюнингом `heatFallPerSecond` регулируется, насколько наказывается «жадность без пауз».

**Выход из `Overheat`:** для v0 — фиксированный таймер `overheatCooldown`. (Альтернатива на будущее: выход по падению heat ниже порога `overheatResetThreshold` — не реализуем в v0, но оставить код так, чтобы замена была локальной.)

---

## 5. Публичный контракт (хуки для других систем)

Пульт **декуплен**: не открывает сейф, не трогает агро, не ограничивает стрельбу, не рисует UI. Он отдаёт состояние и события, на которые подписываются другие системы. Это делает механику тестируемой в изоляции.

Публичные члены:
- `ConsoleState State { get; }` — текущее состояние.
- `float HeatNormalized { get; }` — heat в `[0, 1]` для шкалы.
- `bool IsVaultOpen => State == ConsoleState.Open` — для системы сейфа.
- `bool IsCardHeld => State == Breaching || State == Open` — для агро и урезанной стрельбы.
- `event Action<ConsoleState> OnStateChanged` — дёргается при каждом переходе.
- `event Action<float> OnHeatChanged` — дёргается при изменении heat (для UI-шкалы).

Кто на что подписывается (для контекста, реализовать вне этого ТЗ): система сейфа — на `OnStateChanged` (открыть при `Open`, закрыть иначе); индикаторы пульта и свет на сейфе — на `OnStateChanged` + `OnHeatChanged`; агро и урезанная стрельба — на `IsCardHeld`.

Входная точка (вызывается слоем взаимодействия/сети, авторитетно на сервере):
- `void SetCardInSlot(bool inserted)` — вставить/вынуть карту. В `Overheat` вставка игнорируется.

---

## 6. Сеть

Механика **server-authoritative** (heat влияет на сейф, а сейф — server-auth).

- Ввод игрока (вставка/выемка карты) идёт от владельца на сервер (в Mirror — `[Command]`; или эквивалент выбранного стека).
- Весь тик (heat, таймеры warm-up и cooldown, переходы) считается **на сервере**.
- `State` и `HeatNormalized` реплицируются на клиентов (в Mirror — `[SyncVar]` с хуком, дёргающим `OnStateChanged` / `OnHeatChanged`). Клиенты используют их **только для косметики** (индикаторы, свет, шейк).
- Открытие/закрытие сейфа определяется серверным `State`, а не клиентом.

Стек финально не выбран — код писать так, чтобы серверный тик и репликация состояния были изолированы и не завязаны на конкретный фреймворк жёстко.

---

## 7. Параметры (SerializeField, стартовые значения — тюнятся на плейтесте)

| Параметр | Тип | Старт | Смысл |
|---|---|---|---|
| `warmupDuration` | float, сек | `2.0` | задержка `Breaching → Open` |
| `heatRisePerSecond` | float, 1/сек | `0.2` | скорость роста heat в `Open` (≈5 сек окна с холода) |
| `heatFallPerSecond` | float, 1/сек | `0.35` | скорость падения heat вне `Open` (остывает быстрее, чем греется) |
| `overheatThreshold` | float, 0..1 | `1.0` | порог перегрева |
| `overheatCooldown` | float, сек | `4.0` | длительность лока в `Overheat` |

Все значения — стартовая точка для баланса, не финал.

---

## 8. Критерии приёмки

1. **Открытие с холода:** вставил карту в `Idle` → через `warmupDuration` состояние `Open`, heat начинает расти.
2. **Перегрев:** держим в `Open` до `heat >= overheatThreshold` → авто-переход в `Overheat`, карта выброшена, сейф закрыт, вставка заблокирована на `overheatCooldown`, затем `Idle`.
3. **Ранний выход:** вынул карту до порога → мгновенно `Idle`, сейф закрыт, heat падает с `heatFallPerSecond`.
4. **Блок при перегреве:** попытка вставить карту в `Overheat` не меняет состояние.
5. **Даун оператора в `Open`:** трактуется как выемка карты → `Idle`, сейф закрыт.
6. **Клампы:** heat никогда не выходит за `[0, 1]`.
7. **Короткое окно после спешки:** вынул и сразу вставил без паузы → следующее окно `Open` заметно короче (heat не успел упасть).
8. **Авторитетность:** все смены `State`/heat происходят на сервере; клиенты сходятся в пределах интерполяции; сейф управляется серверным состоянием.

---

## 9. Референс-скелет (контракт, не финальная реализация)

Claude Code должен интегрировать это с существующими системами (слот карты, сейф, сеть), а не писать в вакууме.

```csharp
public enum ConsoleState { Idle, Breaching, Open, Overheat }

public class BreachConsole : MonoBehaviour // или NetworkBehaviour в выбранном стеке
{
    [Header("Warm-up")]
    [SerializeField] float warmupDuration = 2f;

    [Header("Heat")]
    [SerializeField] float heatRisePerSecond = 0.2f; // только в Open
    [SerializeField] float heatFallPerSecond = 0.35f; // во всех прочих состояниях
    [SerializeField] float overheatThreshold = 1f;
    [SerializeField] float overheatCooldown = 4f;

    public ConsoleState State { get; private set; } = ConsoleState.Idle;
    public float HeatNormalized { get; private set; }
    public bool IsVaultOpen => State == ConsoleState.Open;
    public bool IsCardHeld => State == ConsoleState.Breaching || State == ConsoleState.Open;

    public event Action<ConsoleState> OnStateChanged;
    public event Action<float> OnHeatChanged;

    bool cardInSlot;
    float warmupTimer;
    float overheatTimer;

    // Вызывается слоем взаимодействия/сети, авторитетно на сервере.
    public void SetCardInSlot(bool inserted)
    {
        if (State == ConsoleState.Overheat) return; // слот заблокирован
        cardInSlot = inserted;
    }

    // Серверный тик (Update на сервере или сетевой тик выбранного стека).
    void Tick(float dt)
    {
        switch (State)
        {
            case ConsoleState.Idle:
                CoolDown(dt);
                if (cardInSlot) EnterBreaching();
                break;

            case ConsoleState.Breaching:
                CoolDown(dt);
                if (!cardInSlot) { SetState(ConsoleState.Idle); break; }
                warmupTimer -= dt;
                if (warmupTimer <= 0f) SetState(ConsoleState.Open);
                break;

            case ConsoleState.Open:
                if (!cardInSlot) { SetState(ConsoleState.Idle); break; }
                SetHeat(HeatNormalized + heatRisePerSecond * dt);
                if (HeatNormalized >= overheatThreshold) EnterOverheat();
                break;

            case ConsoleState.Overheat:
                CoolDown(dt);
                overheatTimer -= dt;
                if (overheatTimer <= 0f) SetState(ConsoleState.Idle);
                break;
        }
    }

    void EnterBreaching() { warmupTimer = warmupDuration; SetState(ConsoleState.Breaching); }
    void EnterOverheat()  { cardInSlot = false; /* выбросить карту из слота */ overheatTimer = overheatCooldown; SetState(ConsoleState.Overheat); }
    void CoolDown(float dt) => SetHeat(HeatNormalized - heatFallPerSecond * dt);

    void SetState(ConsoleState s) { if (s == State) return; State = s; OnStateChanged?.Invoke(s); }
    void SetHeat(float v) { v = Mathf.Clamp01(v); if (Mathf.Approximately(v, HeatNormalized)) return; HeatNormalized = v; OnHeatChanged?.Invoke(v); }
}
```

> Примечание: физический выброс карты из слота (строка в `EnterOverheat`) интегрировать с реальной системой слота/носимого предмета. В сетевой версии `State` и `HeatNormalized` сделать реплицируемыми, а `OnStateChanged`/`OnHeatChanged` дёргать из хука репликации на клиентах.

---

## 10. Вне scope этого ТЗ

Реализуется отдельно, подписываясь на контракт из §5: открытие/закрытие сейфа, свет на сейфе (красный/зелёный), UI-шкала heat и индикатор состояния, стягивание агро при `IsCardHeld`, урезанная стрельба при `IsCardHeld`, сама система слота/переноса ключ-карты.
