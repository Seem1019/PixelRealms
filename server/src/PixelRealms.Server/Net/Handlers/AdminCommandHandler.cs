using System.Globalization;
using PixelRealms.Content;
using PixelRealms.Game.Core;
using PixelRealms.Game.Entities;
using PixelRealms.Game.Items;
using PixelRealms.Game.Progression;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Players;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>
/// HU-070: `AdminCommand{text}` solo para cuentas con `is_admin` (CA2: `Error{forbidden}` + warn). Cada comando se loguea con
/// el autor (CA3); `/give` y `/gold` quedan en `item_audit_log` como `admin_give`. Comandos: `/tp x y`, `/tpto Nombre`,
/// `/spawn monsterId [n]`, `/give itemId [qty] [Nombre]`, `/level n`, `/heal`, `/kill`, `/gold n`, `/god`, `/debug move on|off`,
/// `/announce texto`. Las coordenadas van en casillas.
/// </summary>
public sealed class AdminCommandHandler(CombatHandlerDeps deps, PlayerRegistry players, MapTransferService transfers, ReloadableContent content, ILogger<AdminCommandHandler> logger) : IMessageHandler<AdminCommand>
{
    public void Handle(AdminCommand msg, HandlerContext ctx)
    {
        var p = ctx.Player;
        if (p is null || deps.MapOf(p) is not { } map) return;
        var session = ctx.Connections.Get(ctx.ConnectionId);
        if (session is null || !session.IsAdmin)
        {
            logger.LogWarning("Comando admin rechazado: {Name} (cuenta {Account}) intentó '{Text}'", p.Name, p.AccountId, msg.Text);
            ctx.SendError(ErrorCodes.Forbidden);
            return;
        }
        var text = (msg.Text ?? "").Trim();
        if (text.StartsWith('/')) text = text[1..];
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        var cmd = parts[0].ToLowerInvariant();
        var args = parts[1..];
        logger.LogInformation("Comando admin de {Name} (cuenta {Account}): /{Text}", p.Name, p.AccountId, text);

        var reply = cmd switch
        {
            "tp" => Tp(p, map, args),
            "tpto" => TpTo(p, args, ctx),
            "spawn" => Spawn(p, map, args, ctx),
            "give" => Give(p, map, args, ctx),
            "level" => Level(p, map, args, ctx),
            "heal" => Heal(p, map, ctx),
            "kill" => Kill(p, map, ctx),
            "gold" => Gold(p, map, args, ctx),
            "god" => God(p),
            "debug" => Debug(p, args),
            "announce" => Announce(args, ctx),
            "reload" => Reload(args, ctx),
            _ => null,
        };
        if (reply is null) { ctx.SendError(ErrorCodes.InvalidPayload, null, $"Comando desconocido: /{cmd}"); return; }
        ctx.Send(new ChatMessage("system", "", reply, ctx.Tick.NowMs));
    }

    /// <summary>
    /// HU-003 CA4c: `/reload rules` relee content/rules.json; desde el siguiente tick los sistemas usan los números nuevos. Si el
    /// archivo es inválido se conserva el anterior y se responde con los errores. Lee el disco en el tick: es un comando de admin,
    /// raro y de un archivo pequeño. El cliente sigue con su copia hasta reiniciarse (Welcome.rulesHash lo delata). Las stats
    /// derivadas cacheadas (crítico, armadura, vida máxima…) se recalculan con las reglas nuevas y cada jugador recibe su StatsUpdate.
    /// </summary>
    private string Reload(string[] args, HandlerContext ctx)
    {
        if (args.Length != 1 || args[0] != "rules") return "Uso: /reload rules";
        var report = content.ReloadRules();
        if (!report.IsValid)
        {
            logger.LogError("/reload rules rechazado: {Errors}", string.Join(" | ", report.Errors));
            return $"rules.json inválido, se mantiene el anterior: {report.Errors[0]}";
        }
        foreach (var map in deps.World.Instances)
            foreach (var actor in map.Actors.Values)
            {
                if (actor is not Player p) { actor.MarkStatsDirty(); continue; }
                deps.Combat.Services.Recalculate(p);
                ctx.Tick.Emit(new StatsChangedEvent(map.Id, p));
            }
        logger.LogInformation("rules.json recargado (hash {Hash})", content.Rules.Hash);
        return $"rules.json recargado (hash {content.Rules.Hash})";
    }

    private static string Tp(Player p, Game.Map.MapInstance map, string[] args)
    {
        if (args.Length < 2 || !TryFloat(args[0], out var x) || !TryFloat(args[1], out var y)) return "Uso: /tp x y";
        if (map.Collision.IsSolidAt(x, y)) return $"({x:0.##}, {y:0.##}) es sólido";
        Teleport(p, new Vec2(x, y));
        return $"Teletransportado a ({x:0.##}, {y:0.##})";
    }

    private string TpTo(Player p, string[] args, HandlerContext ctx)
    {
        if (args.Length < 1) return "Uso: /tpto Nombre";
        var target = players.ByName(args[0]);
        if (target is null || target.ConnectionId < 0) return $"No hay nadie conectado como '{args[0]}'";
        if (target.MapInstanceId == p.MapInstanceId) { Teleport(p, target.Position); return $"Junto a {target.Name}"; }
        var targetMap = deps.MapOf(target);
        if (targetMap is null) return "Destino sin mapa";
        return transfers.Transfer(p, targetMap.MapId, target.Position, ctx.Tick, $"admin /tpto {target.Name}") ? $"Junto a {target.Name} en {targetMap.MapId}" : "No se pudo cambiar de mapa";
    }

    private string Spawn(Player p, Game.Map.MapInstance map, string[] args, HandlerContext ctx)
    {
        if (args.Length < 1) return "Uso: /spawn monsterId [n]";
        var n = args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? Math.Clamp(parsed, 1, 50) : 1;
        var created = deps.Combat.Spawns.SpawnAt(args[0], p.Position, n, map, ctx.Tick.Rng);
        return created == 0 ? $"Monstruo desconocido: {args[0]}" : $"{created} × {args[0]} creados";
    }

    private string Give(Player p, Game.Map.MapInstance map, string[] args, HandlerContext ctx)
    {
        if (args.Length < 1) return "Uso: /give itemId [qty] [Nombre]";
        var db = content.Current;
        if (!db.TryGetItem(args[0], out var tpl) || tpl is null) return $"Item desconocido: {args[0]}";
        var qty = 1;
        var nameIdx = 1;
        if (args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)) { qty = Math.Max(1, q); nameIdx = 2; }
        var target = p;
        if (args.Length > nameIdx)
        {
            target = players.ByName(args[nameIdx]);
            if (target is null || target.ConnectionId < 0 || deps.MapOf(target) is null) return $"No hay nadie conectado como '{args[nameIdx]}'";
        }
        var result = InventoryOps.AddItem(target, tpl, qty, "admin_give", null, p.CharacterId);
        if (!result.Ok) return $"No cabe en la bolsa de {target.Name} ({result.ErrorCode})";
        ctx.Tick.Emit(new InventoryChangedEvent(target.MapInstanceId, target, null));
        return $"{qty} × {tpl.Name} → {target.Name}";
    }

    private string Level(Player p, Game.Map.MapInstance map, string[] args, HandlerContext ctx)
    {
        if (args.Length < 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)) return "Uso: /level n";
        var final = deps.Combat.Progression.SetLevel(p, level, map, ctx.Tick);
        return $"Nivel {final}";
    }

    private string Heal(Player p, Game.Map.MapInstance map, HandlerContext ctx)
    {
        if (p.IsDead) return "Estás muerto: usa /level o reaparece";
        p.Hp = p.MaxHp;
        p.Resource = p.MaxResource;
        p.Dirty = true;
        ctx.Tick.Emit(new StatsChangedEvent(map.Id, p));
        return "Vida y recurso al máximo";
    }

    private string Kill(Player p, Game.Map.MapInstance map, HandlerContext ctx)
    {
        if (p.Combat.TargetId is not { } tid || map.Find(tid) is not { } target) return "Sin objetivo";
        if (target.IsDead) return $"{target.Name} ya está muerto";
        if (target is Player { GodMode: true }) return $"{target.Name} es inmortal (/god)";
        target.Hp = 0;
        deps.Combat.Death.Kill(target, p, map, ctx.Tick);
        return $"{target.Name} eliminado";
    }

    private static string Gold(Player p, Game.Map.MapInstance map, string[] args, HandlerContext ctx)
    {
        if (args.Length < 1 || !long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)) return "Uso: /gold n";
        p.Gold = Math.Max(0, p.Gold + amount);
        p.Audit(new PendingAudit(Guid.Empty, "admin_give", "gold", (int)Math.Clamp(amount, int.MinValue, int.MaxValue), p.CharacterId));
        ctx.Tick.Emit(new InventoryChangedEvent(map.Id, p, null));
        return $"Oro: {p.Gold}";
    }

    private static string God(Player p)
    {
        p.GodMode = !p.GodMode;
        return p.GodMode ? "Modo dios: ON" : "Modo dios: OFF";
    }

    private static string Debug(Player p, string[] args)
    {
        if (args.Length < 2 || args[0] != "move") return "Uso: /debug move on|off";
        p.DebugMove = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        return p.DebugMove ? "Traza de movimiento: ON" : "Traza de movimiento: OFF";
    }

    private string Announce(string[] args, HandlerContext ctx)
    {
        if (args.Length == 0) return "Uso: /announce texto";
        var text = string.Join(' ', args);
        var sent = 0;
        foreach (var other in players.All)
        {
            if (other.ConnectionId < 0 || other.ConnectionId == ctx.ConnectionId) continue;
            ctx.Connections.Send(other.ConnectionId, new ChatMessage("system", "", text, ctx.Tick.NowMs));
            sent++;
        }
        return $"[Anuncio] {text} ({sent} jugadores)";
    }

    private static void Teleport(Player p, Vec2 pos)
    {
        p.Position = pos;
        p.MoveDx = 0;
        p.MoveDy = 0;
        p.Combat.TargetId = null;
        p.Dirty = true;
    }

    private static bool TryFloat(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
