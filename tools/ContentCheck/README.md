# ContentCheck — validador base de `content/`

Punto de partida de **HU-003** (que lo sustituye por `server/tools/ContentValidator` con JsonSchema.Net e integra la carga en
`PixelRealms.Content`). Consola .NET 10 sin paquetes NuGet: solo `System.Text.Json`.

```bash
dotnet run --project tools/ContentCheck -- content/
```

Sale con código **0** si no hay errores (los avisos no fallan) y **1** si hay alguno. Imprime el resumen de HU-003 CA1
(`4 clases, 41 hechizos, 26 auras, 37 items, 10 monstruos, 10 tablas, 1 vendedor(es), rules OK`).

## Qué comprueba
1. **Schemas** (`content/schemas/*.schema.json`, draft 2020-12): cada `content/*.json` contra el schema que declara en `$schema`.
   `SchemaValidator` implementa el subconjunto de palabras clave que usan los schemas del repo (`type`, `enum`, `const`,
   `required`, `properties`, `additionalProperties`, `items`, `min/maxItems`, `uniqueItems`, `minimum`, `maximum`,
   `exclusiveMinimum`, `pattern`, `min/maxLength`, `allOf`, `oneOf`, `if/then/else`, `$ref` local y entre archivos por `$id`).
   Si un schema usa una palabra clave nueva, hay que añadirla (o ya habrá llegado JsonSchema.Net).
2. **Referencias cruzadas** (`CrossRefChecks`, lista de la skill `game-content` §Procedimiento y HU-003 CA 3/4/4b/4d):
   ids únicos por archivo; `auraId`, `useSpellId`, `lootTableId`, `spellId` de monstruos, `itemId` de botín, equipo inicial y
   vendedores existen; `damageMin ≤ damageMax`, `min ≤ max`; equipo inicial con afinidad **alta**; `levelReq` en `1..maxLevel`;
   `cost.resource` = recurso de la clase; `items[].scaling` = `rules.affinity.weaponScaling[weaponType]`; `type: boss` ⇔
   `boss: true`; `groups[].rolls ≤ entradas`; XP de monstruo no escrita; `ground_aoe_all` con al menos un efecto positivo y uno
   negativo (HU-085); hechizos instantáneos de clase con `cooldownMs ≥ minInstantSpellCooldownMs`; ≤ `maxSpellsPerClass` por
   clase y `levelReq` iguales a `spellUnlockLevels`.
3. **`rules.json`**: `affinity.byClass` cubre todos los tipos de arma y armadura; `weapons.types` = `weaponScaling`;
   `bonusBySize` con `maxMembers` entradas; `levelCapByPhase` creciente y terminando en `maxLevel`; `minutesPerLevel` con
   `maxLevel − 1` entradas; `spellRankLevels` en rango (ADR-024); filas de `classScaling` y `classAdvantage` por clase; rulesets
   de PvP definidos; pentagrama con puntas que suman `budget` y no superan `maxPerAxis`.
4. **Avisos (ADR-023):** un hechizo que use una forma, un targeting o un efecto que el motor aún no implementa se marca como
   *no disponible*; también avisa de áreas apuntadas de daño sin casteo (ADR-015), auras y tablas de botín huérfanas.

## Qué NO hace todavía (llega con HU-003)
Cargar `ContentDb`/`RulesDb` tipados, el hook `PostToolUse` de `.claude/settings.json`, tests xUnit por regla (aquí la prueba
es manual: romper un JSON y ver el error), ni la validación completa de JSON Schema (solo el subconjunto de arriba).
