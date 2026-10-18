# Иконки v3 и модель Mist Panther — отчёт

## Что импортировано

- 33 ч/б мастера `bw_pick/*.png` → `Assets/_Project/Art/Icons/Abilities_v3/` (имена как есть). Sprite (2D and UI),
  Single, без mipmaps, max 512, без сжатия, sRGB; Read/Write включён — по мастеру один раз строится мягкая маска.
- Рамка `frame_256.png`, `frame_128.png` → `Assets/_Project/Art/Icons/Frame/`.
- Палитры `ResonantPalette` → `Resources/Prototype/Palettes/`: fire, plant, poison, earth, mist, blood, lightning, water,
  neutral, `_background` (числа из palettes.json), плюс `IconMasters.asset` (список мастеров, рамка, фон, нейтральная).
- Mist Panther: FBX и PBR-карты → `Art/Models/MistPanther/` (URP Lit, упакованная Metallic/Smoothness, префаб с обёрткой
  масштаба), портрет → `Art/Portraits/MistPanther/`.
- Эталоны `colored/*` в проект не импортировались. Старые иконки в `Art/Icons/*` не тронуты.

## Палитры резонантов

| Юнит | Палитра |
| --- | --- |
| BlazeWorm | fire |
| MistPanther | mist |
| PrimalIdol | earth |
| Blightgullet | poison |
| PlayerEcho, RangerEcho, SkirmisherEcho, RaiderEnemy, TargetDummy | neutral |

## Ассет → иконка

| Ассет | Мастер | Палитра |
| --- | --- | --- |
| Abilities/BlazeFierySpit | fire_spit | владельца |
| Abilities/BlazeFieryVolley | fiery_volley | владельца |
| Abilities/BlazeLongBite | long_bite | владельца |
| Abilities/FlameBurst | flame_burst | владельца |
| Abilities/PurifyingFlame | purifying_flame | владельца |
| Abilities/WideSwipe | wide_swipe | владельца |
| Abilities/VanishingStep | vanishing_step | владельца |
| Abilities/PartingStrike | parting_strike | владельца |
| Abilities/BloodDash | blood_dash | blood (override) |
| Abilities/EarthPillar | earth_pillar | владельца |
| Abilities/FireGeyser | fire_geyser | владельца |
| Abilities/LiftTrap | lift_trap | владельца |
| Abilities/StormMagnet | storm_magnet | владельца |
| Units/BlazeWorm.movementIcon (Подземный ход, кнопка перемещения) | deep_burrow | fire |
| Units/PrimalIdol.passiveIcon (Ритуальные поверхности, кнопка Пол/Стена) | ritual_surfaces | earth |
| Passives/FireWorm_BankedHeat | banked_heat | владельца |
| Passives/FireWorm_RoaringFurnace | roaring_furnace | владельца |
| Passives/MagmaWorm_BankedHeat | banked_heat + magma_trail | владельца |
| Passives/MagmaWorm_BloodOfTheDeep | blood_of_the_deep + magma_trail | владельца |
| Passives/MistPanther_Mist | mist_veil + deep_wounds | mist + blood |
| Passives/MistPanther_Shadow | return_to_shadow + silent_executioner | владельца |
| Passives/PrimalIdol_TwinEruption | twin_eruption | владельца |
| Passives/PrimalIdol_StormOvercharge | storm_overcharge | владельца |
| Passives/PrimalIdol_TitanPillar | titan_pillar | владельца |
| Passives/PrimalIdol_RagingTornado | raging_tornado | владельца |
| Statuses/Bleed | status_bleed | blood |
| Statuses/Invisibility | status_invisibility | mist |
| Statuses/Mist (новый ассет-носитель иконки) | status_mist | mist |
| Statuses/Vulnerability (новый ассет-носитель иконки) | status_vulnerability | blood |
| Statuses/Magma (новый ассет-носитель иконки; показывается, пока резонант стоит на Магме) | status_magma | fire |
| Effects/ApplyBurnTenPercent.icon (Горение) | status_burning | fire |

Иконки веток видны рядом с названием ветки в карточке активного резонанта (две иконки, если в ветке две пассивки),
с подсказкой при наведении.

## Что осталось без новой иконки

- Статусы Corrosion, Oil, Poison, Immobilized, Slow — старые иконки, без раскраски.
- Способности жабы (Poison/Corrosion/Oil Cloud, Mud Spit, Reeling Pull) и технических юнитов — мастеров для них нет.
- Спорные места: `deep_wounds` по карточке 10.12 — пассивка туманной ветки (Mist Panther II), поэтому стоит в
  MistPanther_Mist, а не в Shadow; у Shadow две пассивки — return_to_shadow (II) и silent_executioner (III).
  Magma Trail отдельного ассета не имеет — его иконка вторая у обеих магмовых веток червя.

## Проверка

- Сверка с эталонами `colored/<palette>/*_256.png` (`IconReferenceCheck.Run`): среднее отличие на пиксель
  1,6–4,6 из 255 по всем 33 эталонам; листы «наше | эталон» в `Logs/Screenshots/icons-*-ours-vs-reference.png`.
- EditMode 120/120: у всех способностей червя, пантеры и тотема мастер из Abilities_v3, палитры назначены,
  `IconTintCache.Get` возвращает 256×256, одна иконка в разных палитрах различается.
- PlayMode 8/8: скриншоты HUD `40-icons-primal-idol.png`, `41-icons-blaze-worm.png`, `42-icons-mist-panther.png`,
  `43-wide-swipe-mist-fire-earth-neutral.png`.
