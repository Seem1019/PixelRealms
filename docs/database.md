# Modelo de datos (PostgreSQL 17 + EF Core 10)

Convenciones: tablas y columnas en `snake_case` (`EFCore.NamingConventions` → `UseSnakeCaseNamingConvention()`),
PK `uuid` (UUID v7 generado en .NET: `Guid.CreateVersion7()`), timestamps `timestamptz` en UTC,
nombres únicos case-insensitive mediante la collation ICU no determinista `case_insensitive` (ver *Nombres únicos* abajo).

```mermaid
erDiagram
  accounts ||--o{ characters : tiene
  characters ||--o{ character_items : posee
  characters ||--o{ character_hotbar : configura
  characters ||--o{ character_cooldowns : recarga
  characters ||--o{ item_audit_log : genera
  accounts {
    uuid id PK
    varchar20 username "único, collation case_insensitive"
    text password_hash "ASP.NET PasswordHasher (PBKDF2)"
    bool is_admin
    timestamptz created_at
    timestamptz last_login_at
  }
  characters {
    uuid id PK
    uuid account_id FK
    varchar64 name "único entre vivos, collation case_insensitive, 3-16, ^[A-Za-z][A-Za-z0-9]+$"
    text class_id "warrior|rogue|mage|priest"
    int level "1..rules.progression.maxLevel (15)"
    int xp
    bigint gold "en cobre"
    text map_id
    real x
    real y
    int hp
    int resource
    timestamptz created_at
    timestamptz updated_at
    timestamptz deleted_at "soft delete"
  }
  character_items {
    uuid id PK "id de instancia, NUNCA se reutiliza"
    uuid character_id FK
    text template_id
    int quantity ">=1"
    smallint container "0=bag 1=equip"
    smallint slot
  }
  character_hotbar {
    uuid character_id PK,FK
    smallint slot PK "0..7 (0–3 hechizos, 4–7 utilizables)"
    smallint kind "0=spell 1=item"
    text ref
  }
  character_cooldowns {
    uuid character_id PK,FK
    smallint kind PK "0=spell 1=item (plantilla de consumible)"
    varchar48 ref PK
    timestamptz ends_at "fin en reloj real: sigue corriendo desconectado (HU-015)"
  }
  item_audit_log {
    bigint id PK
    uuid item_id
    uuid character_id
    text action "loot|buy|sell|destroy|split|merge|trade_in|trade_out|admin_give"
    text template_id
    int quantity
    timestamptz at
  }
```

Restricciones:
- `UNIQUE (character_id, container, slot)` en `character_items` (evita dos items en el mismo slot).
- `CHECK (quantity >= 1)`, `CHECK (level BETWEEN 1 AND 15)` (si `maxLevel` sube, migración).
- Máx 4 personajes por cuenta: `ICharacterRepository.CreateAsync` cuenta e inserta en una transacción que bloquea la fila
  de la cuenta (`SELECT … FROM accounts WHERE id = … FOR UPDATE`), así que peticiones concurrentes no lo superan.
- `item_audit_log.counterparty_character_id uuid NULL` para `trade_in`/`trade_out`.
- `characters.gold` en **cobre** (`1 oro = 100 plata = 10 000 cobre`); en UI se formatea.

### Nombres únicos (sin distinguir mayúsculas)
- `accounts.username` y `characters.name` usan la collation **`case_insensitive`**: ICU, locale `und-u-ks-level2`,
  `deterministic = false`. Nivel 2 = ignora mayúsculas pero no acentos, así que `'Bob' = 'bob'` es verdadero.
- Los índices únicos `ix_accounts_username_ci` y `ix_characters_name_ci` (constantes en `GameDbContext`) van sobre la
  columna tal cual: al heredar la collation, son ellos los que rechazan `Bob`/`bob`, también en inserciones concurrentes.
  No hay índice sobre `lower(...)` ni hace falta `ToLower()` en las consultas: un `==` de EF ya compara sin mayúsculas y usa
  el índice.
- Los repositorios solo traducen a `username_taken` / `name_taken` una violación `23505` de **ese** índice; cualquier otro
  fallo de base de datos se propaga.
- Al borrar un personaje (soft delete) su nombre pasa a `Nombre#deleted-<id>` y queda libre (HU-013 CA4).
- Limitaciones de las collations no deterministas en PG 17: no admiten `LIKE`/`ILIKE` sobre esas columnas.
- La validación (`\A…\z`, solo ASCII) impide guardar nombres que ICU y `OrdinalIgnoreCase` (repositorio InMemory)
  compararían distinto; el login aplica la misma validación antes de consultar.

Migraciones: una por HU que cambie el modelo, nombre `YYYYMMDD_Descripcion`. Nunca editar una migración ya aplicada
en el VPS; crear otra.
