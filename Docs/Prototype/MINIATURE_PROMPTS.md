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
A single hand-painted tabletop board-game miniature, simplified stylized sculpt like a premium board game
figure, chunky proportions, bold readable silhouette, a few large clean shapes, smooth surfaces with almost
no surface texture (no fine cracks, no tiny scales, no moss clumps, no grain), color and simple painted
highlights do the work instead of sculpted detail, vivid flat-ish hand-painted colors, slight comic-book feel.
Full body visible, standing on the ground, no base, no plinth, no text, no frame.
Three-quarter front view, camera at eye level of the figure, centered, whole figure in frame with margin.
Plain light grey seamless background, soft even studio lighting, no harsh shadows, no motion blur,
no other objects or characters.
```

## Резонанты

### Fire Worm / Magma Worm (огненный и магмовый червь)

Ранги по карточке: I Нестабильное Эхо, II Пробуждение (развилка на огненную и магмовую линии), III Развитие,
IV Аспект, V Ипостась. Твои два червя — это потолок III ранга. На IV–V червь становится почти разумным
божеством: появляется органический доспех, «лицо» и осанка. Промт = общий стиль + основа червя + блок ранга
+ блок линии.

Основа червя (всегда):

```
Subject: a colossal volcanic worm resonant, thick and heavy like a giant grub, NOT a thin snake,
compact body coiled in place on the ground with the front third raised up, round maw at the front.
```

Блок ранга — выбрать один:

```
Rank I-III (wild beast): plain segmented body with simple smooth plates, thin glowing seams, round maw
with short blunt teeth, animal posture, no armor, no ornaments.
```

```
Rank IV - Aspect (awakened, near-sentient): the front segments are covered by grown organic armor, a fused
carapace like a helmet and a gorget over the head, a crest of three thick horn-like plates rising behind
the maw, two calm intelligent glowing eyes set in the armor above the maw, symmetrical rune-like glowing
lines on the carapace; upright proud posture, the raised part held straight like a cobra, not lunging.
```

```
Rank V - Hypostasis (living deity of the volcano): full organic armor of large polished plates along the
whole body like a temple or a cathedral of stone, a tall crown-shaped crest behind the head, a solar halo
ring of molten rock floating behind the crest, a calm ancient face-mask grown over the maw (the maw opens
beneath it), glowing sigils on the plates; majestic, still, regal posture, the body coiled like a throne.
```

Блок линии — выбрать один:

```
Fire line: slimmer smoother armor, warm black and deep red plates, bright orange-yellow fire glow, flame-shaped
crest edges.
Palette: charcoal black, deep red, orange, bright yellow.
```

```
Magma line: broader heavier body, thick basalt plates with molten magma between them, blunt massive head,
crest like cooled lava rock.
Palette: dark basalt grey, molten orange, dull red, a little gold.
```

### Primal Idol (стихийный тотем)

По карточке 2.5: левитирующий шаманский тотем из **четырёх сегментов-масок — земля, огонь, ветер, молния**.
Парение — только визуал. Порядок масок снизу вверх (земля, огонь, ветер, молния) — моё предложение.

```
Subject: a levitating shamanic totem made of four stacked carved mask segments, each mask a different element,
one above the other, each with its own face and its own color, clearly separated by thin stone rims:
1) bottom - EARTH mask: wide heavy stone face, square jaw, brown and ochre, small rocks orbiting it;
2) second - FIRE mask: angry face with open mouth, red-orange, flame-shaped brows and cheeks, ember glow
   in the eyes and mouth;
3) third - WIND mask: serene face with closed eyes and puffed cheeks blowing, pale teal and white,
   swirl-shaped ear plates curling outward;
4) top - LIGHTNING mask: sharp stern face, deep blue and violet, zig-zag lightning crest on top, bright
   electric-yellow eyes.
The totem floats above the ground with a small gap, a few flat stones hovering under it.
Simple bold carving, smooth stone, no moss, no roots.
```

Для IV ранга (Аспект) по карточке выбранная стихия подсвечивает верхний сегмент. Добавить в конец:
`the top segment is replaced by the chosen element and glows strongly: ` + одно из
`fire (Twin Eruption)`, `lightning (Storm Overcharge)`, `earth (Titan Pillar)`, `wind (Raging Tornado)`.

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

Из карточек взято: червь «тяжёлый, не тонкая змея», магмовая линия — базальтовые пластины с магмой, ранги
червя; идол — четыре маски (земля, огонь, ветер, молния), парение, подсветка верхнего сегмента на IV ранге.
Доспех и «божественность» червя на IV–V, порядок и облик масок, жаба и пантера — моё предложение.
