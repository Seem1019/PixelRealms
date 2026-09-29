# server/ — reglas locales

Antes de editar aquí, carga la skill `dotnet-server` (y `combat-system` / `inventory-items` / `net-protocol` si aplica).

- `PixelRealms.Game` no puede referenciar `Server`, `Persistence`, ASP.NET ni EF. Sin `async`, sin `DateTime.Now`, sin `Random`.
- Todo cambio de dominio llega con tests en `tests/PixelRealms.Game.Tests` usando `WorldBuilder`, `FakeClock`, `FixedRng`.
- Handlers de red: validar → llamar dominio → `ctx.SendError` con códigos de `docs/protocol.md`. Nunca lanzar excepciones
  para errores de jugador.
- Tras cambios aquí, antes de cerrar: `dotnet build -warnaserror`, `dotnet test`, y subagente `server-authority-reviewer`.
