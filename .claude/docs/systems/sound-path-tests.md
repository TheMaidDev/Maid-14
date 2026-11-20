# Sound-path: статус тестов и план продолжения (handoff 20.11)

Эталонная рабочая версия фичи: коммит **14d0479beb** («fold soundpath loudness into the stream processing override»), подтверждена юзером. Поверх него: раунд 21 (selfW всегда) — не закоммичен; тесты (этот документ) — не закоммичены.

## Что сделано в тестовой сессии 20.11

1. **`PathSoundSystem.TryGetModelOcclusion`** (public) — тест/отладочная обёртка: raw path-occl + свёртка в filter-домен (`occl·(1+occlLoss/0.8686) + (len+past)·loss/0.8686 + straight·loss/0.8686`), без сглаживания — детерминизм для тестов.
2. **`Content.IntegrationTests/Tests/_Maid/PathSoundTest.cs`** — 9 сценариев-контрактов на герметичном однотайловом коридоре восток от игрока: CleanCorridorDistanceFade, SingleClosedAirlock, FiveAirlocksRowSilence, SourceOwnTileExemption (раунд-21), OpeningAirlockRaisesAudibility, VestibuleStack (шлюз+стекло-шлюз = 3.912), WindowMuffle, WallFullSilence (герметичная комната вокруг источника), BeyondFieldRadius (30 тайлов, clamp-ветка).
3. **`sound-path.md`**: секции Testing (матрица-контракт, банды слышимости: <40 слышно / 40–150 глухо / >150 тишина) и Prior art (Barotrauma hull-модель + Soundproof Walls, Thief/Dark Mod порталы-как-двери, CS:GO 2017 traces, Project Triton — все решают «двери = динамические порталы» пересчётом, как мы).

## Статус прогона: 9/9 зелёные

## OpeningAirlockRaisesAudibility — причина найдена и устранена (20.11, вторая сессия)

Падение было **чисто тестовой багой**: в середине теста стоял лишний `await pair.CleanReturnAsync()` — пара утилизировалась до второго `SetDoorState`, поэтому `ToClientUid` не находил дверной entity («Failed to resolve MetaData … 12752»). Никакого PVS-делита и lifecycle-заговора. Заодно: wait-цикл `SetDoorState` ждал флипа клиентской коллизии, которого по контракту не бывает (см. ниже) — 60 холостых итераций на каждый Open; условие сведено к совпадению стейта.

### Важные открытые факты (проверено диагностикой, пригодится)

- **Сетевое изменение стейта двери НЕ флипает клиентскую коллизию**: клиент получает `State = Open` через AutoHandleState, но `PhysicsComponent.CanCollide` остаётся true (SetCollidable вызывается только в реальном потоке двери/предсказании). Модель консистентна с физикой клиента — контракт задокументирован в тесте (утверждение «opened but solid on client»).
- Следствие для продакшена: **звуки чужих дверей (сервер-реплицированные) не продырявливают глушение при открытии** — модель молчит, пока коллизия не флипнулась на клиенте. Это ХОРОШО для юзера (ранее «о чудо» поднимало слышимость), но означает: тихое открытие чужой двери будет глухим до реального CanCollide-флипа (OnPartialClose). Потенциальный follow-up: подписка на клиентский DoorComponent state-delta → явный SetCanCollide-флип на клиенте (если юзер захочет, чтобы чужие двери «продырявливали» звук сразу).
- Аналитические ожидания должны учитывать **past-decay** (`occlR = max(0, Σblockers − 0.3·past)`) и **clamp occl на −ln(1e-9) ≈ 20.72** (helper `Expected()` уже делает).
- Готчи инфраструктуры: клиентский локальный моб НЕ телепортируется серверным SetAttachedEntity-координатам (клиент-авторитетная физика) — телепортировать на клиенте тоже (`map.CGridUid`); `GlassAirlock`-прототипа нет, есть `AirlockGlass`; `client.System<ServerDoorSystem>()` кинет UnregisteredTypeException — использовать алиасы ClientDoorSystem/ServerDoorSystem; серверные uid нельзя читать клиентскими EntMan-методами в WaitAssertion (KeyNotFoundException → пара dirty).

## Состояние после второй сессии 20.11

1. OpeningAirlockRaisesAudibility починен (корень — лишний `CleanReturnAsync` в самом тесте) — **9/9**, прогнан дважды подряд стабильно (~1 мин и ~43 с).
2. Закоммичено поверх эталона 14d0479beb: selfW-всегда (раунд 21), smoothing 0.15, тесты + `TryGetModelOcclusion` + доки.
3. Осталось за юзером: финальный плейтест «на слух» + пуш ветки sound-path.
4. Опциональный follow-up из открытых фактов: клиентский флип CanCollide по сетевому стейту двери (см. выше) — сейчас чужие двери открываются акустически глухо до реального флипа коллизии.
