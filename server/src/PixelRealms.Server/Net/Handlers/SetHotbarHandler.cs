using PixelRealms.Content;
using PixelRealms.Protocol.Messages;

namespace PixelRealms.Server.Net.Handlers;

/// <summary>
/// `SetHotbar{slot, kind?, ref?}` (ADR-014, HU-041 CA2, HU-043 CA2/CA5): casillas 0–3 solo hechizos conocidos; 4–7 solo
/// consumibles; sin `kind` vacía la casilla. En combate, una casilla de hechizo ocupada no se cambia ni se vacía (`in_combat`):
/// si no, cambiarla antes de cada `CastSpell` daría todo el kit conocido a mano. Persiste con el personaje (character_hotbar).
/// </summary>
public sealed class SetHotbarHandler(ReloadableContent content) : IMessageHandler<SetHotbar>
{
    public void Handle(SetHotbar msg, HandlerContext ctx)
    {
        var player = ctx.Player;
        if (player is null) return;
        var rules = content.Current.Rules.Loadout;
        var total = rules.SpellSlots + rules.UsableSlots;
        if (msg.Slot < 0 || msg.Slot >= total || msg.Slot >= player.Hotbar.Length) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        if (msg.Slot < rules.SpellSlots && player.Hotbar[msg.Slot] is not null
            && player.IsInCombat(ctx.Tick.NowMs, content.Current.Rules.Combat.InCombatWindowSec)) { ctx.SendError(ErrorCodes.InCombat); return; }
        if (msg.Kind is null || msg.Ref is null)
        {
            player.Hotbar[msg.Slot] = null;
            player.Dirty = true;
            return;
        }
        var isSpellSlot = msg.Slot < rules.SpellSlots;
        if (msg.Kind == "spell")
        {
            if (!isSpellSlot || !player.KnownSpells.Contains(msg.Ref)) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        }
        else if (msg.Kind == "item")
        {
            if (isSpellSlot || !content.Current.TryGetItem(msg.Ref, out var item) || item is null || item.Type != Content.Defs.ItemType.Consumable) { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        }
        else { ctx.SendError(ErrorCodes.InvalidPayload); return; }
        // Un mismo hechizo no ocupa dos casillas.
        for (var i = 0; i < player.Hotbar.Length; i++)
            if (i != msg.Slot && player.Hotbar[i] is { } h && h.Kind == msg.Kind && h.Ref == msg.Ref) player.Hotbar[i] = null;
        player.Hotbar[msg.Slot] = (msg.Kind, msg.Ref);
        player.Dirty = true;
    }
}
