# Prompts de contenido, mapas y arte

## Nuevo hechizo
```
Usando la skill game-content, agrega al Sacerdote un hechizo de nivel 6 "Palabra de las sombras: Dolor":
DoT de sombras de 18 s, instantáneo, 25 de maná. Ajusta los números con la guía de balance de la skill,
muéstrame el cálculo de daño total a nivel 6 y 10 frente a Castigo, valida con ContentValidator y crea el ícono
placeholder o lista el asset faltante.
```

## Lote de items
```
Usa el subagente content-designer para diseñar 12 items nuevos de nivel 5–9 (3 por clase, mezcla de armas,
armaduras y joyería; 8 uncommon y 4 rare) con el presupuesto de stats de la skill game-content.
Añádelos a items.json y repártelos en las tablas de botín de goblin y esqueleto con probabilidades de la guía.
Muéstrame una tabla resumen (id, nivel, rareza, slot, stats, fuente) antes de escribir los JSON.
```

## Nuevo monstruo con zona
```
Agrega un monstruo "Araña del bosque" nivel 4–5 que envenena (reutiliza el aura rogue_poison o crea una propia),
con su tabla de botín, y 2 spawns en el Bosque Sombrío de maps/meadow.tmj (skill world-maps).
Verifica que los spawns no caen en sólido y que no solapan el aggro de los lobos.
```

## Nueva clase (post-MVP)
```
Quiero una 5.ª clase "Cazador" (arco, energía, mascota NO en esta versión). Analiza primero:
1. ¿Qué efectos/targeting/tipos de arma faltan en el motor actual? (lista concreta de cambios de código + HUs nuevas)
2. Propuesta de 5 hechizos que usen solo lo existente + lo mínimo nuevo.
No escribas nada hasta que aprobemos el análisis.
```

## Mapa
```
Con la skill world-maps, crea maps/test_small.tmj (10×10) para los tests del TiledMapLoader con: 1 muro,
1 tile blocksSight, 1 spawn punto, 1 spawn rectángulo count 3, 1 npc vendedor, 1 cementerio, 1 zona safe.
Escríbelo como JSON de Tiled válido (formato CSV, tilesets embebidos) y verifícalo abriéndolo con el loader en un test.
```

## Arte con PixelLab (MCP)
```
Con la skill pixel-art-assets y el MCP de PixelLab:
1. Genera el personaje Mago: top-down 3/4, 32×32, 4 direcciones, túnica azul con ribetes dorados, bastón de madera,
   contorno oscuro, paleta limitada.
2. Anímalo: idle, walk y cast.
3. Guarda las hojas en client/assets/sprites/characters/mage.png con su JSON de metadatos y genera el SpriteFrames.
4. Añade la entrada en CREDITS.md.
Antes de generar, muéstrame las instrucciones exactas que enviarás a cada herramienta.
```

## Tileset
```
Con PixelLab genera un tileset top-down Wang 16×16 de pasto ↔ tierra y otro de pasto ↔ agua con el mismo estilo que
[describe o adjunta referencia]. Configúralos en Tiled como terreno (tilesets .tsj con wangsets) y marca propiedades
solid/blocksSight donde corresponda (agua sólida, no bloquea visión).
```

## Íconos en lote
```
Recorre content/*.json y lista todos los valores de "icon" que no tienen PNG en client/assets/icons/.
Genera con PixelLab (o, si no hay MCP, crea placeholders 16×16 por código con la inicial y color de rareza)
todos los faltantes, respetando la especificación de la skill pixel-art-assets.
```
