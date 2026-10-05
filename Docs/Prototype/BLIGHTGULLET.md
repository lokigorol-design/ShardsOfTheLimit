# Blightgullet — контентный срез

## Запуск и ручная проверка

Открыть `Assets/_Project/Scenes/BlightgulletBattle.unity`, нажать Play, закрыть подсказки.
Blightgullet заменяет первого союзника только в этой сцене. Исходная `PrototypeBattle` не меняет состав.
Для наблюдения AI можно до Play изменить `Blightgullet Control` на `BasicAI` у компонента сцены;
это меняет способ управления, не команду и не определение юнита.
Кнопки 1–5: Mud Spit, Poison Cloud, Corrosion Cloud, Tongue Pull, Oil Cloud.
Выбрать подсвеченную цель, проверить preview и подтвердить Enter. Space заканчивает активацию, R перезапускает сцену.

Проверить облако на клетке (1,0), а Tongue Pull — на ближайшем противнике (4,0).
Подтягивание остановится перед занятой клеткой/препятствием; immobilized не блокирует язык.
Для наблюдения движения облаков передавать активации: время движется только при передаче активации, не по часам компьютера.
Все команды получают стаки облаков, включая владельца. Это не занятость поля.

Наведение на подпись или объём облака показывает тип, владельца, остаток времени, стаки, направление, скорость,
вертикальные границы и следующие клетки. Переключатель status events включает события зон/статусов в таймлайне.
Над юнитами отображаются независимые счётчики и временная защита Native Mire.

## Inspector и BALANCE_DEFAULT / TODO

Это временные значения, а не утверждённый баланс:

| Конфиг | Начальные значения / TODO |
| --- | --- |
| Units/Blightgullet | HP 100, shield 40, initiative 40, movement 3. Согласовать характеристики. |
| Все 5 способностей | AP 1, CD 1, Ranged, базовая дальность 4. Согласовать отдельно для каждой. |
| Mud Spit | 15 урона, Immobilized 100 глобальных тиков, одна цель-враг. |
| Tongue Pull | 3 клетки, без урона; цель-враг. |
| Три основных CloudDefinition | Ромб/крест radius 1, duration 300t, pulse 100t, move 100t, speed 1, stacks 1, height 1, damage 0. |
| VenomousMudCloud | Отдельный preset: radius 0, остальные стартовые значения как у основных облаков. |
| Poison / Corrosion / Oil StatusDefinition | Max 20, duration 300t с обновлением при повторном наложении, pulse 100t, damagePerStack 0. |
| Immobilized | Max 1, duration 100t, блокирует добровольное движение, не способности. |
| Native Mire | Defense 5 (плоское снижение входящего урона), heal 5 за активацию владельца. Утвердить формулу Defense. |
| Toxic Cocktail | Множитель 2, добавка 0. |

Настройки находятся в `Assets/_Project/Resources/Prototype`. Обычные числа редактируются через Inspector.
В описаниях новых способностей и заголовках конфигов отмечен BALANCE_DEFAULT. Точные глобальные свойства
Poison, Corrosion и Oil не заданы: сейчас это независимые стаки **без пассивного урона, ослабления щита или реакции с огнём**.
Общий StatusDefinition уже позволяет включить damagePerStack; облако не реализует этот урон самостоятельно.
TODO: согласовать Poison damage, эффект Corrosion, реакцию Oil/fire, ограничения перекрытия, иммунитет владельца (если нужен).

В `Passives/BlightgulletBranches.asset` включить `nativeMire`, `toxicCocktail` или `venomousMud` и перезапустить Play.
По умолчанию ветки выключены. Их можно включать независимо, в том числе вместе для технического теста.
`modifiedAbility` и `onHitCloud` — явная модификация Mud Spit, без дополнительной кнопки.
Native Mire не складывается от числа собственных облаков. Владение определяется instance ID, а не видом Resonant.
Защита проверяется по текущей позиции при расчёте урона и исчезает сразу после выхода/исчезновения зоны.

## Архитектура

- `CloudDefinition`, `StatusDefinition`, `PassiveModifierDefinition` — неизменяемые данные SO; runtime получает чистые снимки правил.
- `CloudAreaEffect` — отдельный runtime-объект: ownerId, sourceAbilityId, клетки, фиксированный нижний Z, высота,
  направление, даты движения/импульса/окончания и итоговые стаки. Никакого класса юнита для облаков.
- `AreaEffectWorld` принадлежит бою; существующий `TimelineCoordinator` обрабатывает его события до активаций.
  Старый Burning продолжает использовать свой проверенный расчёт; новый стековый статус не дублирует поджог.
- `AbilityRuntime` использует одну функцию разрешения для preview/execute и выдаёт результаты статусов/создания облаков.
  Preview не добавляет зоны и не изменяет ресурсы/статусы. Эффекты способностей и пассивки не привязаны к строке Blightgullet.
- У статусов независимые счётчики, cap и источник последнего наложения; повторное наложение обновляет общую длительность.
- На одном тике: истечение стековых статусов → их периодический урон → импульсы облаков → движение → удаление закончившихся зон.
  Последний импульс облака на границе duration выполняется. Следом существующий scheduler обрабатывает burn и активацию.
- Клетка занимает вертикальный интервал `[height, height+1)` для проверки существа; облако `[lowerZ, lowerZ+extent)`.
  Граничное касание без объёма не считается пересечением. Облако не следует за поверхностью и не меняет Z при движении.
- Направление — доминирующая ось от источника к центру, при совпадении +Y; API принимает явный cardinal direction.
  При столкновении заблокированная часть остаётся, свободная движется; совпавшие части одного облака объединяются.
  Разные облака продолжают существовать и тикать независимо. Облако сохраняется после гибели владельца до своего окончания.
- Pull использует общую проверку шага сетки, как push; идёт по доминирующей оси и останавливается перед препятствием,
  занятой клеткой, недопустимым подъёмом или рядом с источником. Специальной линии видимости пока нет и у остальных способностей.
- AI использует короткий прогноз траектории, штраф союзникам, удержание/притягивание в зону и собственное облако для Native Mire.
  Наличие союзника с поджогом повышает интерес к Oil; это эвристика будущей связки, не уже реализованная реакция.

## Визуал и импорт

Пользовательский FBX IV, Base Color, Normal, Metallic и Roughness скопированы в Art/Models/Blightgullet.
MetallicSmoothness сгенерирована из metallic.r и `1 - roughness.r`. Импорт не требует новых пакетов.
Префаб равномерно подогнан под основание 0.9 клетки; исходные оси импорта сохранены. Модель статическая.
Аватар IV и пять предоставленных иконок импортированы как Sprite. Старые материалы/ориентация Blaze Worm не изменены.
Облака пока — прозрачные объёмы со штриховкой, контуром и разными цветами, не полноценные дымовые VFX.
Зелёный — Poison, янтарно-кислотный — Corrosion, тёмно-фиолетовый — Oil. При перекрытии цвета смешиваются,
а отдельные подписи остаются. Runtime-меши и UI не принимают игровых решений.

## Файлы этого изменения

Созданы (для Unity-ассетов также `.meta`):

- Domain: `StackStatus.cs`, `MovingAreaEffects.cs`.
- Gameplay/Definitions: `StatusDefinition.cs`, `CloudDefinition.cs`, `CreateCloudEffectDefinition.cs`,
  `ApplyStatusEffectDefinition.cs`, `PullEffectDefinition.cs`, `PassiveModifierDefinition.cs`.
- Gameplay/Presentation: `BattlePrototypeController.Clouds.cs`; Art/Shaders: `ComicCloud.shader`.
- Editor: `BlightgulletContentSetup.cs` — одноразовый импорт по явному вызову, не автоматическая генерация при открытии.
  При наличии Blightgullet прекращает работу, не переписывая баланс. Не является собственным окном редактора.
- Tests/EditMode: `BlightgulletTests.cs`; Tests/PlayMode: `BlightgulletSceneTests.cs`.
- Scenes: `BlightgulletBattle.unity`.
- Resources/Prototype/Units: `Blightgullet.asset`; Passives: `BlightgulletBranches.asset`.
- Abilities: `MudSpit`, `PoisonCloud`, `CorrosionCloud`, `TonguePull`, `OilCloud` (`.asset`).
- Effects: `MudDamage`, `MudImmobilize`, `TonguePull`, `CreatePoisonCloud`, `CreateCorrosionCloud`, `CreateOilCloud`.
- Statuses: `Poison`, `Corrosion`, `Oil`, `Immobilized`; Clouds: `PoisonCloud`, `CorrosionCloud`, `OilCloud`, `VenomousMudCloud`.
- Patterns: `BlightSingle`, `BlightCross`, `BlightRange`.
- Art/Models/Blightgullet: FBX, prefab, material, BaseColor, Normal, Metallic, Roughness, MetallicSmoothness PNG.
- Art/Icons/Blightgullet: `Blightgullet IV`, `Mud Spit`, `Poison Cloud`, `Corrosion Cloud`, `Tongue Pull`, `Oil Cloud` PNG.
- Этот документ.

Изменены: `CombatRules.cs`, `ForcedMovementRules.cs`, `TurnCoordinator.cs`, `UnitDefinition.cs`,
`AbilityDefinition.cs`, `AbilityRuntime.cs`, `BattlePrototypeController.cs`, `PrototypeSceneSmokeTests.cs`,
`ProjectSettings/EditorBuildSettings.asset`, `Docs/Prototype/DECISIONS.md`.
В проекте уже были несохранённые в Git изменения предыдущих этапов; они не откатывались и не коммитились.

## Проверки

Unity 6000.5.5f1: импорт завершён; компиляция и тесты проверяются штатным Test Runner.
EditMode: 56/56 (23 новых случая), PlayMode: 3/3 (новый сквозной сценарий + 2 прежних).
Отчёты: `Logs/Blightgullet-EditMode.xml`, `Logs/Blightgullet-PlayMode.xml` (не коммитить).
Снимки камеры: `Logs/Blightgullet/Blightgullet-model.png`, `Blightgullet-board.png`, `Blightgullet-clouds.png`.
Модель и поле визуально проверены по реальному рендеру Unity; снимки камеры не включают IMGUI.
AI пока эвристический; баланс, финальные VFX и ручная проверка удобства подписей требуют отдельного игрового теста.
