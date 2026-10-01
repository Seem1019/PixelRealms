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
| 1 | HU-001 Monorepo, solución .NET y CI | M1 | — | En curso | |
| 2 | HU-002 Infra local con Docker | M1 | 001 | Pendiente | compose + .env.example + migración `Initial` |
| 3 | HU-003 Carga y validación de contenido | M1 | 001 | Pendiente | parte de `tools/ContentCheck` |
| 4 | HU-004 Game loop 20 Hz | M1 | 001, 003 | Pendiente | |
| 5 | HU-005 Proyecto Godot base | M1 | 001 | Pendiente | GUT sí; YATI no descargable (ver bloqueos) |
| 6 | HU-006 Protocolo base | M1 | 004, 005 | Pendiente | |
| 7 | HU-010 Registro · HU-011 Login · HU-012 Crear personaje · HU-013 Listar/borrar · HU-014 Entrar al mundo | M1 | 002/003/006 | Pendiente | |
| 8 | HU-020 Mapa Tiled · HU-021 Movimiento · HU-022 Predicción · HU-023 AOI · HU-024 Cámara · HU-025 Linkdead · HU-026 Guardado · HU-027 Portales | M1 | | Pendiente | |
| 9 | HU-030 → HU-039, HU-086, HU-085, HU-087, HU-088, HU-040, HU-041 | M2 | | Pendiente | dominio puro con tests primero |
| 10 | HU-050 → HU-059 | M3 | | Pendiente | |
| 11 | HU-042 → HU-044, HU-060 → HU-064 | M4 | | Pendiente | |
| 12 | HU-070 → HU-075, HU-080 → HU-083, HU-089 | M5 | | Pendiente | HU-084 y las validaciones jugando quedan fuera |

## Decisiones provisionales (revisar)
_(se añaden al tomarlas: qué se eligió, qué se descartó y por qué)_

## Sin compilar / sin ejecutar en esta sesión
_(código escrito que requiere NuGet, Docker o el editor de Godot)_

## Bloqueos
- YATI (importador Tiled del cliente): `github.com/Skoti/YATI` no se pudo clonar desde el sandbox (repo no accesible); el
  cliente deja el hueco (`addons/yati/`) y el import de mapas se prueba en el PC.

## Problemas encontrados en la documentación
_(contradicciones o huecos descubiertos al implementar; cambios mínimos hechos en los docs se listan aquí)_
