# Modelo de datos (PostgreSQL 17 + EF Core 10)

Convenciones: tablas y columnas en `snake_case` (`EFCore.NamingConventions` → `UseSnakeCaseNamingConvention()`),
PK `uuid` (UUID v7 generado en .NET: `Guid.CreateVersion7()`), timestamps `timestamptz` en UTC,
nombres únicos case-insensitive con índice sobre `lower(name)`.

```mermaid
erDiagram
  accounts ||--o{ characters : tiene
  characters ||--o{ character_items : posee
  characters ||--o{ character_hotbar : configura
  characters ||--o{ item_audit_log : genera
  accounts {
    uuid id PK
    text username "único (lower)"
    text password_hash "ASP.NET PasswordHasher (PBKDF2)"
    bool is_admin
    timestamptz created_at
    timestamptz last_login_at
  }
  characters {
    uuid id PK
    uuid account_id FK
    text name "único (lower), 3-16, ^[A-Za-z][A-Za-z0-9]+$"
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
- Máx 4 personajes por cuenta (validado en servicio).
- `item_audit_log.counterparty_character_id uuid NULL` para `trade_in`/`trade_out`.
- `characters.gold` en **cobre** (`1 oro = 100 plata = 10 000 cobre`); en UI se formatea.

Migraciones: una por HU que cambie el modelo, nombre `YYYYMMDD_Descripcion`. Nunca editar una migración ya aplicada
en el VPS; crear otra.
