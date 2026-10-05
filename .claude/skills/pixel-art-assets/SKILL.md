---
name: pixel-art-assets
description: Especificación y flujo para sprites, tiles, íconos, VFX y UI pixel art de PixelRealms — tamaños, paleta, animaciones, nombres, import en Godot, generación con el MCP de PixelLab o Aseprite y licencias de packs gratuitos. Úsala al crear, generar, importar o reemplazar cualquier asset gráfico.
---

# Assets pixel art

## Especificación
| Asset | Tamaño | Notas |
|---|---|---|
| Tiles | 16×16 | tilesets en hojas de 16 columnas; terrenos como Wang/autotile |
| Personajes jugables | 32×32 (cuerpo ~16×24, pies en y=28) | 4 direcciones (s, n, e, w; w = e espejado permitido) |
| Monstruos normales | 32×32 | jefe: 48×48 o 64×64 |
| Íconos (items/hechizos) | 16×16 dibujado, mostrado ×2 en UI | fondo transparente, contorno oscuro 1 px |
| Proyectiles/VFX | 16×16 o 32×32 | 4–6 frames |
| UI 9-slice | múltiplos de 8 | márgenes 4 px |
Resolución lógica del juego: 480×270 escalada ×N entera. **Nunca** escalar sprites por factores no enteros.

## Animaciones (nombres exactos en `SpriteFrames`)
`idle_<dir>` (2–4 frames, 4 fps), `walk_<dir>` (4–6 frames, 8 fps), `attack_<dir>` (3–4 frames, 12 fps),
`cast_<dir>` (2–3 frames loop, 6 fps), `hurt` (1–2 frames), `death` (4 frames, sin loop). `<dir>` ∈ `s|n|e|w`.

## Paleta y estilo
- Paleta limitada recomendada: **Resurrect 64** o **Endesga 32** (elige una y guárdala en `client/assets/palette.png`).
- Luz desde arriba-izquierda. Contorno oscuro (no negro puro) en personajes. Sombra elíptica de 1–2 px bajo los pies (sprite aparte).
- Rarezas de item también se reflejan en el borde del ícono en la UI (no en el ícono).

## Rutas y nombres
`client/assets/sprites/characters/<classId>.png`, `sprites/monsters/<monsterId>.png`, `sprites/npcs/<name>.png`,
`icons/items/<name>.png`, `icons/spells/<name>.png`, `sprites/vfx/<name>.png`, `tiles/<tileset>.png`.
Los ids en `content/*.json` (`icon`, `sprite`) son estas rutas relativas sin extensión.

## Importación en Godot
- Filtro Nearest global (ver skill `godot-client`), `Mipmaps: off`, `Compress: Lossless`.
- Sin `.tres`: `client/scripts/world/entity_sprites.gd` arma los `SpriteFrames` al cargar la hoja, a partir del PNG y su `.json`
  (`frameSize`, `feetY`, `pixelScale`, tabla `anims` con `<anim>_<dir>`) (ADR-026). Los efectos (`sprites/vfx/`) los arma
  `VfxLayer.frames_for` igual, con `frames`, `fps`, `loop` y `rows`.
- Un ícono que falte en tiempo de ejecución se ve como "?" (dibujado en código por `UiTheme.missing_icon`) y avisa una vez en
  el log (HU-082 CA2); el test `test_visual_redesign.gd` exige que existan todos los de `content/`.

## Generar con IA (MCP de PixelLab) — si está configurado (`claude mcp list`)
1. Personaje: pide un personaje de 4 direcciones, vista top-down 3/4, tamaño 32 px, con la descripción de la clase y la paleta.
   Ejemplo de instrucción: *"Top-down 3/4 view pixel art human mage, blue robe with gold trim, wooden staff, 32x32, 4 directions, dark outline, limited palette"*.
2. Anima `walk`, `idle`, y `attack`/`cast` según clase.
3. Tilesets: usa la herramienta de tileset top-down (Wang) para transiciones pasto↔tierra, pasto↔agua.
4. Objetos de mapa (árboles, rocas, cofres): herramienta de map objects con fondo transparente.
5. Descarga a las rutas de arriba, genera `SpriteFrames` y **revisa a mano** en Godot (alineación de pies, frames sueltos).
6. Registra en `client/assets/CREDITS.md`: herramienta, fecha, prompt.

## Sin IA: packs libres recomendados (verifica licencia antes de usar)
Busca en itch.io packs 16×16 top-down con licencia CC0 o que permita uso comercial/no comercial. Anota **siempre**
autor, URL y licencia en `client/assets/CREDITS.md`. No uses assets de juegos comerciales ni "rips".

## Arte del proyecto (generado)
El arte actual lo dibuja `tools/art/generate_all.py` en Resurrect 64 (`client/assets/palette.png`, ADR-026): tiles con
autotile dual-grid (`gen_tiles.py`, filas que lee `client/scripts/world/terrain_baker.gd`), sprites 3/4 de 32×32 (jefes de
64×64) con `filas = s, n, e` y la tabla `anims` del `.json` (reposo, caminar, ataque o casteo, golpe y muerte, HU-090;
`gen_chars.py`), las hojas HD de los héroes con `pixelScale` 3 (`import_heroes.py`, desde las hojas de referencia de
`tools/art/refs/`), efectos de hechizo (`gen_vfx.py`, HU-091), objetos de mapa (`gen_objects.py`: palancas y puertas, HU-083), íconos 16×16
(`gen_icons.py`) y UI 9-slice (`gen_ui.py`). Necesita `pip install pillow numpy` (y `scipy` para `import_heroes.py`, que
importa `generate_all.py`); es determinista, así que regenerar no cambia los PNG que no se tocan. Para añadir una hoja sin
regenerar todo, llama a la función de su generador (p. ej. `gen_chars.sheet` + `gen_chars.write_meta`) y escribe solo esa. Fuente de la
interfaz: Alegreya Sans / Alegreya SC (OFL, HU-092) en `client/assets/fonts/`. Un pack o un dibujo a mano puede sustituir
cualquier PNG con la misma ruta y tamaño; regístralo en `client/assets/CREDITS.md`.
Capturas de referencia: `godot --path client --rendering-driver opengl3 -s res://tools/screenshots.gd` →
`docs/screenshots/redesign/` y `docs/screenshots/combat/`.

## Placeholders
Mientras no haya arte: rectángulos de color por clase/monstruo generados por código (`PlaceholderSprite`) con la inicial
del nombre. Nunca bloquear una HU de gameplay por falta de arte.
