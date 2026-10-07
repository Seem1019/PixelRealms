using PixelRealms.Content;
using PixelRealms.Game.Progression;
using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>
/// `ChooseSpellUpgrade{spellId, upgradeId?, reqId?}` (HU-104, ADR-027 D1): elige o quita (sin `upgradeId`) la mejora de un
/// hechizo propio desde `spellUpgradeLevel`, fuera de combate y gratis. Responde con todas las mejoras (`SpellUpgradesUpdate`);
/// errores: `invalid_payload` (hechizo ajeno o sin esa mejora), `level_too_low`, `in_combat`. Persiste con el personaje.
/// </summary>
public sealed class ChooseSpellUpgradeHandler(ReloadableContent content) : IMessageHandler<ChooseSpellUpgrade>
{
    public void Handle(ChooseSpellUpgrade msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null) return;
        var db = content.Current;
        var inCombat = player.IsInCombat(ctx.Tick.NowMs, db.Rules.Combat.InCombatWindowSec);
        var error = SpellUpgradeRules.Choose(player, msg.SpellId ?? "", msg.UpgradeId, db, db.Rules.Progression, inCombat);
        if (error is not null) { ctx.SendError(error, msg.ReqId); return; }
        ctx.Send(new SpellUpgradesUpdate(new Dictionary<string, string>(player.SpellUpgrades, StringComparer.Ordinal), msg.ReqId));
    }
}
