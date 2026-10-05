"""Regenera todo el arte del cliente en la paleta Resurrect 64 (client/assets/palette.png).

    pip install pillow numpy
    python tools/art/generate_all.py
    godot --path client --headless --import   # crea los .import de los PNG nuevos

Escribe: palette.png, tiles/ (terreno, placeholder.png y collision.png que usan los .tsj), sprites/ (personajes, NPC,
monstruos, sombras, efectos de hechizo en sprites/vfx/ y objetos de mapa en sprites/objects/), icons/ (items, hechizos, siluetas de equipo, clases) y ui/ (9-slice, barras, logo).
Todo es determinista: la misma versión del script da los mismos píxeles.
"""
import gen_chars
import gen_icons
import gen_objects
import gen_tiles
import gen_ui
import gen_vfx
import import_heroes
from pix import palette_png

if __name__ == "__main__":
    palette_png()
    gen_tiles.build()
    gen_chars.build()
    import_heroes.build()  # guerrero y mago: hojas dibujadas a mano (HU-093)
    gen_icons.build()
    gen_ui.build()
    gen_vfx.build()
    gen_objects.build()  # palancas y puerta de la Mina (HU-083)
    print("arte regenerado en client/assets/")
