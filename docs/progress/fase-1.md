# Progreso de la Fase 1 (Tier 1)

> Archivo de trabajo del agente que implementa la Fase 1 en la rama `fase-1`. Se actualiza al cerrar cada HU para que otra
> sesión pueda retomar desde donde quedó. La especificación es la documentación (`CLAUDE.md`, `docs/`, `.claude/`); este
> archivo solo registra orden, estado, decisiones provisionales y bloqueos.

## Entorno de la sesión (2026-10-01)
- Compilación y tests: SDK .NET 10 (10.0.112) en un sandbox Linux **sin acceso a NuGet**. El repo referencia los paquetes
  reales (`Directory.Packages.props`); en el sandbox se sustituyen `xunit.v3`, `Shouldly`, `Microsoft.NET.Test.Sdk` y
  `xunit.runner.visualstudio` por *shims* locales con la misma API (fuera del repo). Todo lo que no se puede compilar sin NuGet
  (EF Core + Npgsql, Testcontainers) se escribe pero **no se ha compilado**: ver `OfflineBuild` abajo y la sección "Sin compilar".
- Godot 4.7.2 (Linux, headless) + GUT 9.6.1 disponibles en el sandbox para `--check-only` y tests GUT. YATI no se pudo descargar.
- Docker/PostgreSQL: no hay demonio Docker en el sandbox; `docker compose up -d postgres` queda por probar en el PC de Diego.
- Propiedad MSBuild `OfflineBuild=true` (`dotnet build -p:OfflineBuild=true`): excluye los paquetes y fuentes que necesitan
  NuGet (EF Core/Npgsql en `Persistence`, Testcontainers en `Persistence.Tests`) y registra el repositorio en memoria. Es
  solo para entornos sin NuGet; el build normal es el documentado en `CLAUDE.md`.

## Orden de ejecución (hitos de `docs/backlog/README.md`)
| # | HU | Hito | Depende de | Estado | Notas |
|---|---|---|---|---|---|
| 1 | HU-001 Monorepo, solución .NET y CI | M1 | — | Hecha | commit `feat(infra)` |
| 2 | HU-002 Infra local con Docker | M1 | 001 | Hecha (EF sin compilar) | migración `Initial` por generar en el PC |
| 3 | HU-003 Carga y validación de contenido | M1 | 001 | Hecha | validador propio en vez de JsonSchema.Net (provisional) |
| 4 | HU-004 Game loop 20 Hz | M1 | 001, 003 | Hecha | |
| 5 | HU-005 Proyecto Godot base | M1 | 001 | Hecha (parcial) | fuente pixel y YATI pendientes; escalado sin verificar visualmente |
| 6 | HU-006 Protocolo base | M1 | 004, 005 | Hecha | integración sin Mvc.Testing (WebApplication en puerto libre) |
| 7 | HU-010 Registro · HU-011 Login · HU-012 Crear personaje · HU-013 Listar/borrar · HU-014 Entrar al mundo | M1 | 002/003/006 | Servidor hecho (010–013); pantallas del cliente y HU-014 en curso | JWT HS256 propio (provisional) |
| 8 | HU-020 Mapa Tiled · HU-021 Movimiento · HU-022 Predicción · HU-023 AOI · HU-024 Cámara · HU-025 Linkdead · HU-026 Guardado · HU-027 Portales | M1 | | HU-020 hecha (servidor); resto pendiente | cliente sin YATI: no dibuja el mapa |
| 9 | HU-030 → HU-039, HU-086, HU-085, HU-087, HU-088, HU-040, HU-041 | M2 | | Pendiente | dominio puro con tests primero |
| 10 | HU-050 → HU-059 | M3 | | Pendiente | |
| 11 | HU-042 → HU-044, HU-060 → HU-064 | M4 | | Pendiente | |
| 12 | HU-070 → HU-075, HU-080 → HU-083, HU-089 | M5 | | Pendiente | HU-084 y las validaciones jugando quedan fuera |

## Decisiones provisionales (revisar)
- **HU-003 · validación de schemas sin JsonSchema.Net.** Se eligió portar el `SchemaValidator` de `tools/ContentCheck`
  (subconjunto de draft 2020-12 con exactamente las palabras clave que usan los schemas del repo) a `PixelRealms.Content`.
  Descartado: JsonSchema.Net (NuGet bloqueado en el sandbox). Reversible: sustituir la clase `Validation/SchemaValidator`
  por `JsonSchema.Net` + `SchemaRegistry` sin tocar el resto. Riesgo: una palabra clave nueva en un schema no se valida
  hasta añadirla.

- **HU-006 · tests de integración sin `Microsoft.AspNetCore.Mvc.Testing`.** `TestServer` arranca el `WebApplication` real en
  `http://127.0.0.1:0` y `TestGameClient` usa `ClientWebSocket`. Descartado: `WebApplicationFactory` (NuGet bloqueado y el
  WebSocket real es lo que importa probar). Reversible: añadir el paquete y envolver `ServerApp.Build`.
- **HU-005 · fuente pixel.** No se eligió fuente (el prompt P4 pide proponer 2 y que Diego elija); se usa la de Godot con
  tamaño 8/32. Candidatas libres: m5x7 y m6x11 (Daniel Linssen, CC0) → `client/assets/fonts/` + `CREDITS.md`.

- **HU-011 · JWT HS256 a mano (`Auth/JwtService`)** en lugar de `Microsoft.AspNetCore.Authentication.JwtBearer`. Descartado el
  paquete por NuGet; la validación es un filtro de endpoint (`RequireJwt`). Reversible: sustituir el filtro por
  `AddAuthentication().AddJwtBearer` con la misma clave.
- **Logging:** se usa `Microsoft.Extensions.Logging` (consola) en vez de Serilog (skill dotnet-server). Reversible al añadir
  `Serilog.AspNetCore`; los mensajes ya son estructurados (`{Name}`).

## Sin compilar / sin ejecutar en esta sesión
- `server/src/PixelRealms.Persistence/Ef/*` (GameDbContext, EfAccountRepository, EfCharacterRepository, EfPersistence) y
  `server/tests/PixelRealms.Persistence.Tests/Ef/*` (PostgresFixture con Testcontainers, CharacterRepositoryTests). Pasos en el
  PC: `dotnet build server/PixelRealms.sln` → corregir lo que marque → `dotnet ef migrations add Initial -p
  server/src/PixelRealms.Persistence -s server/src/PixelRealms.Server` → `docker compose up -d postgres` → `dotnet test`.
- La versión de los paquetes en `Directory.Packages.props` se fijó de memoria (xunit.v3 3.1.0, Shouldly 4.3.0,
  Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0, EFCore.NamingConventions 10.0.0, Testcontainers.PostgreSql 4.6.0,
  Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4): si `dotnet restore` falla por una versión inexistente,
  `dotnet package search <id>` y ajustar.

## Bloqueos
- YATI (importador Tiled del cliente): `github.com/Skoti/YATI` no se pudo clonar desde el sandbox (repo no accesible); el
  cliente deja el hueco (`addons/yati/`) y el import de mapas se prueba en el PC.

## Problemas encontrados en la documentación
_(contradicciones o huecos descubiertos al implementar; cambios mínimos hechos en los docs se listan aquí)_
