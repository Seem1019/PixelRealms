# Cómo trabajar con Claude Code (Opus 5.5) en PixelRealms

## 1. Preparar el entorno (una sola vez, Windows)
| Herramienta | Para qué | Cómo |
|---|---|---|
| Git + Git Bash | control de versiones; Claude Code usa Git Bash en Windows | `winget install Git.Git` |
| .NET 10 SDK | servidor | `winget install Microsoft.DotNet.SDK.10` |
| Godot 4.5+ (estándar, **no** .NET) | cliente | descarga de godotengine.org; añade la carpeta al `PATH` y renombra el ejecutable a `godot.exe` |
| Docker Desktop | PostgreSQL local | `winget install Docker.DockerDesktop` |
| Tiled | editor de mapas | `winget install Tiled.Tiled` o mapeditor.org |
| Aseprite (opcional) | pixel art manual | Steam o compilarlo |
| Claude Code | el agente | ver docs.claude.com → Claude Code → instalación |
| `dotnet-ef` | migraciones | `dotnet tool install --global dotnet-ef` |

### MCP servers recomendados
```bash
# Godot: abrir/ejecutar el proyecto y leer errores de depuración (requiere Node 18+)
claude mcp add godot -e GODOT_PATH="C:/Tools/Godot/godot.exe" -- npx @coding-solo/godot-mcp

# PixelLab: generar personajes, animaciones, tilesets e íconos pixel art (cuenta en pixellab.ai)
claude mcp add pixellab https://api.pixellab.ai/mcp -t http -H "Authorization: Bearer TU_TOKEN_PIXELLAB"

# Comprobar
claude mcp list
```
> Guarda el token de PixelLab solo en tu configuración local, nunca en el repo.

## 2. Modelo y modo de trabajo
- Abre Claude Code en la raíz del repo (`cd PixelRealms && claude`). Carga `CLAUDE.md`, las skills de `.claude/skills/`
  y los subagentes de `.claude/agents/` automáticamente.
- Modelo: `/model` → elige Opus. Para HUs grandes (L) o de arquitectura, pide explícitamente que piense a fondo
  ("piensa con detenimiento antes de planificar").
- **Modo plan** (Shift+Tab hasta "plan mode") al empezar cada HU: Claude lee y propone, no edita. Apruebas y sale del modo plan.
- **Una HU por conversación.** Al terminar: `/clear`. Si la conversación se alarga: `/compact` con una instrucción
  ("conserva el plan de HU-033 y los archivos tocados").
- Revisa cada diff antes de aceptar. Pide explicaciones de lo que no entiendas del lado C#: es tu fuerte, úsalo para auditar.
- Haz commit tú (o pídeselo) al final de cada HU; así puedes volver atrás con `git` si algo sale mal.

## 3. Archivos de prompts
| Archivo | Cuándo |
|---|---|
| `01-arranque.md` | primera sesión: crear el esqueleto (HU-001 → HU-006) |
| `02-por-hu.md` | plantilla para implementar cualquier HU |
| `03-contenido-y-arte.md` | agregar clases, hechizos, items, monstruos, mapas y arte |
| `04-depurar-revisar.md` | bugs, desincronización, rendimiento, revisión de seguridad, playtests |

## 4. Orden recomendado
Sigue los hitos de `docs/backlog/README.md` (M1 → M5). No empieces combate (M2) hasta que dos clientes se vean
moverse (M1) con fluidez: todo lo demás se apoya en eso.
