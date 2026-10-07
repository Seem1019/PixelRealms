# Créditos de assets y addons

| Recurso | Versión | Origen | Licencia |
|---|---|---|---|
| GUT (Godot Unit Test) | 9.6.1 | https://github.com/bitwes/Gut | MIT (`addons/gut/LICENSE.md`) |
| Fuentes **Alegreya Sans** (Medium, Bold) y **Alegreya SC** (Bold) (`assets/fonts/Alegreya*.ttf`) | 2.x | Juan Pablo del Peral / Huerta Tipográfica — copia de https://github.com/google/fonts/tree/main/ofl/alegreyasans y .../ofl/alegreyasc | SIL OFL 1.1 (`assets/fonts/OFL-Alegreya.txt`) |
| Fuente **Departure Mono** (solo para dibujar el logo `assets/ui/logo.png`; no se distribuye con el juego) | 1.x | Helena Zhang, https://departuremono.com — `tools/art/fonts/DepartureMono-Regular.otf` | SIL OFL 1.1 (`tools/art/fonts/OFL-DepartureMono.txt`) |
| Paleta **Resurrect 64** (`assets/palette.png`) | — | Kerrie Lake, https://lospec.com/palette-list/resurrect-64 | Libre (paleta publicada para uso libre en Lospec) |

## Arte generado por el proyecto

Todo el arte de `assets/tiles/`, `assets/sprites/`, `assets/icons/` y `assets/ui/` lo dibuja el script
`tools/art/generate_all.py` (Python + Pillow, numpy y, para `import_heroes.py`, scipy) en la paleta Resurrect 64, con la dirección artística de Heartwood Online
como referencia de estilo (sin copiar ni descargar nada suyo). Es arte original del proyecto, con la misma licencia que
el resto del repositorio. Se generó así porque desde el entorno de trabajo no había acceso a itch.io ni a OpenGameArt
(octubre de 2026); cualquier pack libre que lo sustituya debe registrarse aquí con autor, URL y licencia.

| Carpeta | Generador | Contenido |
|---|---|---|
| `tiles/terrain.png`, `tiles/placeholder.png`, `tiles/collision.png` | `tools/art/gen_tiles.py` | pasto, tierra, camino, agua con orilla de piedras, rocas, bosque, arbustos, copas, cercas, casas, objetos (autotile dual-grid) |
| `tiles/terrain_forest.png`, `tiles/terrain_crypt.png` | `tools/art/gen_tiles.py` | Bosque (hierba oscura con musgo, tierra húmeda, pantano, copas oscuras, raíces) y Cripta (interior de cueva en verde), misma disposición que `terrain.png` (HU-108) |
| `sprites/characters/`, `sprites/npcs/`, `sprites/monsters/`, `sprites/shadow*.png` | `tools/art/gen_chars.py` | 4 clases, 2 NPC, 8 monstruos y 2 élites (jabalí de guerra y huargo, variantes de su especie) de 32×32 y 2 jefes de 64×64; 3 direcciones (w = e espejado), reposo, caminar, ataque o casteo, golpe y muerte (HU-090) |
| `sprites/monsters/` (Tier 2) | `tools/art/gen_monsters_t2.py` | 12 monstruos del Bosque y la Cripta de Raíces de 32×32, entre ellos 3 élites (oso viejo, bruja del pantano y guardián de la cripta), con las mismas animaciones (HU-114) |
| `icons/items/`, `icons/spells/`, `icons/slots/`, `icons/classes/` | `tools/art/gen_icons.py` | 37 items, 38 hechizos, 9 siluetas de equipo, 4 clases (16×16) |
| `sprites/vfx/` | `tools/art/gen_vfx.py` | efectos de hechizo (HU-091): brillos de casteo, proyectiles en 8 direcciones, impactos, cura y estallidos de área |
| `sprites/objects/` | `tools/art/gen_objects.py` | palanca (sin activar / activada) y puerta de la Mina (cerrada / abierta), cuadros de 16×16 (HU-083) |
| `ui/` | `tools/art/gen_ui.py` | marcos 9-slice de madera, botones, casillas, barras, campo de texto, aviso, casillas de verificación y logo |
| `sprites/characters/{warrior,mage,rogue,priest}.png` (HD) | `tools/art/import_heroes.py` | hojas de referencia aportadas por el equipo (`tools/art/refs/*_sheet.webp`), recortadas a 120×120 con `pixelScale` 3 |
| `sprites/characters/priest.png` (fila norte) | PixelLab MCP (2026-10-02), `tools/art/refs/priest_pixellab/` | rotación v3 del reposo sur ("young priest with short blond hair, white and blue robe with gold trim, red collar, holding a tall golden staff with a glowing orb, chibi proportions, dark outline, high detail pixel art") y animaciones v3 al norte: reposo (plantilla breathing-idle), caminar, golpe de báculo y casteo |
| `sprites/characters/{warrior,mage}.png` (ataque y casteo al norte) | PixelLab MCP (2026-10-02), `tools/art/refs/{warrior,mage}_pixellab/` | animaciones v3 al norte desde un cuadro de espaldas de su propia hoja (`start_north.png`): guerrero "swinging the sword in a strong diagonal slash forward, red cape flaring" y "raising the sword high and shouting a battle cry, golden sparkles"; mago "swinging the staff forward in a quick arc strike with a blue magic trail" y "raising the staff high to channel a spell, the blue orb glowing brighter" |
