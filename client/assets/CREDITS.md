# Créditos de assets y addons

| Recurso | Versión | Origen | Licencia |
|---|---|---|---|
| GUT (Godot Unit Test) | 9.6.1 | https://github.com/bitwes/Gut | MIT (`addons/gut/LICENSE.md`) |
| Fuentes **Alegreya Sans** (Medium, Bold) y **Alegreya SC** (Bold) (`assets/fonts/Alegreya*.ttf`) | 2.x | Juan Pablo del Peral / Huerta Tipográfica — copia de https://github.com/google/fonts/tree/main/ofl/alegreyasans y .../ofl/alegreyasc | SIL OFL 1.1 (`assets/fonts/OFL-Alegreya.txt`) |
| Fuente **Departure Mono** (solo para dibujar el logo `assets/ui/logo.png`; no se distribuye con el juego) | 1.x | Helena Zhang, https://departuremono.com — `tools/art/fonts/DepartureMono-Regular.otf` | SIL OFL 1.1 (`tools/art/fonts/OFL-DepartureMono.txt`) |
| Paleta **Resurrect 64** (`assets/palette.png`) | — | Kerrie Lake, https://lospec.com/palette-list/resurrect-64 | Libre (paleta publicada para uso libre en Lospec) |
| YATI (importador Tiled) | — | **pendiente**: no se pudo descargar en la sesión sin red; instalar desde https://github.com/Skoti/YATI | MIT |

## Arte generado por el proyecto

Todo el arte de `assets/tiles/`, `assets/sprites/`, `assets/icons/` y `assets/ui/` lo dibuja el script
`tools/art/generate_all.py` (Python + Pillow) en la paleta Resurrect 64, con la dirección artística de Heartwood Online
como referencia de estilo (sin copiar ni descargar nada suyo). Es arte original del proyecto, con la misma licencia que
el resto del repositorio. Se generó así porque desde el entorno de trabajo no había acceso a itch.io ni a OpenGameArt
(octubre de 2026); cualquier pack libre que lo sustituya debe registrarse aquí con autor, URL y licencia.

| Carpeta | Generador | Contenido |
|---|---|---|
| `tiles/terrain.png`, `tiles/placeholder.png`, `tiles/collision.png` | `tools/art/gen_tiles.py` | pasto, tierra, camino, agua con orilla de piedras, rocas, bosque, arbustos, copas, cercas, casas, objetos (autotile dual-grid) |
| `sprites/characters/`, `sprites/npcs/`, `sprites/monsters/`, `sprites/shadow*.png` | `tools/art/gen_chars.py` | 4 clases, 2 NPC, 8 monstruos de 32×32 y 2 jefes de 64×64; 3 direcciones (w = e espejado), reposo y caminar |
| `icons/items/`, `icons/spells/`, `icons/slots/`, `icons/classes/` | `tools/art/gen_icons.py` | 37 items, 38 hechizos, 9 siluetas de equipo, 4 clases (16×16) |
| `ui/` | `tools/art/gen_ui.py` | marcos 9-slice de madera, botones, casillas, barras, campo de texto, aviso, casillas de verificación y logo |
| `sprites/characters/{warrior,mage,priest}.png` (HD) | `tools/art/import_heroes.py` | hojas de referencia aportadas por el equipo (`tools/art/refs/*_sheet.webp`), recortadas a 120×120 con `pixelScale` 3 |
