# Промты для миниатюр резонантов (ChatGPT / Nano Banana → Meshy)

Цель — картинка, из которой Meshy соберёт чистую модель: одна фигурка, целиком, на нейтральном фоне,
ровный свет, читаемый силуэт, крупные формы без мелкой «каши». Стиль — раскрашенная настольная миниатюра
(как фигурки Table Tactics, но детальнее), под комиксовые контуры в игре.

## Как пользоваться

1. Собери промт: **общий блок стиля** + **блок резонанта**. Промты на английском — обе модели понимают их точнее.
2. Сначала сгенерируй вид 3/4 спереди. Когда понравится — попроси у той же модели **вид сбоку и сзади
   того же персонажа** (промт ниже), чтобы дать Meshy несколько ракурсов (Multi-Image to 3D).
3. В Meshy: Image to 3D, текстура включена, PBR можно выключить (нам нужен «раскрашенный» вид),
   полигонов умеренно (10–30k). Подставку Meshy не делать — она есть в игре.

## Общий блок стиля (вставлять в начало каждого промта)

```
A single hand-painted tabletop miniature figure, 28mm wargame miniature style, chunky stylized proportions,
bold readable silhouette, large clear shapes and few small details, smooth sculpted surfaces,
vivid hand-painted colors with soft painted highlights and darker recesses, slight comic-book feel.
Full body visible, standing on the ground, no base, no plinth, no text, no frame.
Three-quarter front view, camera at eye level of the figure, centered, whole figure in frame with margin.
Plain light grey seamless background, soft even studio lighting, no harsh shadows, no motion blur,
no other objects or characters.
```

## Резонанты

### Blaze Worm (огненный червь)

```
Subject: a heavy fire worm creature, thick and massive like a giant grub, NOT a thin snake.
Segmented body coiled in an S-shape with the front third raised up, covered in overlapping dark charcoal
armor plates; glowing orange-yellow lava visible in the seams between the plates; wide round maw at the front
ringed with short blunt teeth, warm inner glow.
Palette: charcoal black, deep red, orange, bright yellow glow.
```

Магмовая ветка (вариант): заменить вторую строку на
`broader body with thick basalt plates, molten magma between them, heavier blunt head`.

### Primal Idol (первобытный идол)

```
Subject: an ancient stone totem idol, a tall carved pillar of earth-brown stone with a large stylized face
(heavy brow, deep eye sockets with faint amber glow, wide flat mouth), rough chiseled edges, a few roots
and moss growing around its lower part, simple carved tribal grooves; it floats slightly above the ground
with small stones hovering beneath it.
Palette: warm brown and ochre stone, green moss, amber eyes.
```

### Blightgullet (ядовитая жаба)

```
Subject: a big swollen poison toad, squat round body, wide mouth, huge throat sac puffed out,
warty skin with toxic green-yellow pustules, small mushroom growths on its back, thick front legs planted
on the ground, heavy-lidded eyes; a little toxic drool at the mouth.
Palette: sickly olive and moss green, toxic yellow-green spots, purple accents on the throat.
```

### Mist Panther (туманная пантера)

```
Subject: a sleek predatory panther made of night and mist, low stalking pose with one front paw raised,
lean muscular body, long tail curling up, glowing pale cyan eyes; wisps of mist sculpted as solid flowing
shapes trailing from its shoulders, tail tip and paws (solid sculpted mist, not transparent smoke).
Palette: deep blue-grey fur, pale cyan and white mist, cyan eyes.
```

## Дополнительные ракурсы для Meshy

После удачной картинки, в том же чате:

```
Now show exactly the same miniature, same design, colors and proportions, from the left side view
and from the back view, side by side, same plain light grey background and the same lighting.
```

## Если выходит «каша»

- Добавить: `fewer details, bigger shapes, clean silhouette, toy-like readability`.
- Убрать огонь/туман/частицы как отдельные эффекты — пусть будут вылеплены как часть фигурки:
  `effects sculpted as solid shapes, not particles`.
- Для Meshy лучше всего работают изображения без перекрытий (лапы не прячутся за телом, хвост не уходит за кадр).

Внешность взята из того, что есть в картах и в игре (червь — из вики: «тяжёлый червь, не тонкая змея»,
магмовая ветка — базальтовые пластины с магмой). Остальное — моё предложение, правь под свою задумку.
