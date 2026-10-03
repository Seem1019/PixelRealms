namespace PixelRealms.Protocol.Messages;

/// <summary>Intención enviada por el cliente (docs/protocol.md §Cliente → Servidor). El servidor valida todo (regla 1).</summary>
public interface IClientMessage;

/// <summary>Posición en píxeles del mundo (1 tile = 16 px), 2 decimales.</summary>
public readonly record struct Vec2Dto(float X, float Y);

/// <summary>Referencia a una casilla de bolsa o equipo: <c>c</c> = "bag" | "equip", <c>i</c> = índice.</summary>
public sealed record SlotRef(string C, int I);

public sealed record Hello(int ProtocolVersion, string Ticket) : IClientMessage;

public sealed record Ping(long ClientTime) : IClientMessage;

public sealed record MoveInput(int Seq, int Dx, int Dy) : IClientMessage;

public sealed record SelectTarget(int? TargetId = null) : IClientMessage;

public sealed record CastSpell(string SpellId, int? TargetId = null, Vec2Dto? TargetPos = null, int? ReqId = null) : IClientMessage;

public sealed record CancelCast : IClientMessage;

public sealed record AutoAttack(bool On) : IClientMessage;

public sealed record InventoryMove(SlotRef From, SlotRef To, int? Qty = null, int? ReqId = null) : IClientMessage;

public sealed record UseItem(string ItemId, int? TargetId = null, int? ReqId = null) : IClientMessage;

public sealed record DestroyItem(string ItemId, int Qty, int? ReqId = null) : IClientMessage;

public sealed record LootOpen(int LootId) : IClientMessage;

public sealed record LootTake(int LootId, int Index) : IClientMessage;

public sealed record LootTakeAll(int LootId) : IClientMessage;

public sealed record VendorOpen(int NpcId) : IClientMessage;

public sealed record VendorBuy(int NpcId, string TemplateId, int Qty) : IClientMessage;

public sealed record VendorSell(int NpcId, string ItemId, int Qty) : IClientMessage;

public sealed record ChatSend(string Channel, string Text, string? To = null) : IClientMessage;

public sealed record PartyInvite(string Name) : IClientMessage;

public sealed record PartyRespond(bool Accept) : IClientMessage;

public sealed record PartyLeave : IClientMessage;

/// <summary>HU-015: salir del mundo a la selección de personaje o para cerrar el juego. Fuera de combate guarda y saca al
/// jugador y responde `LoggedOut`; en combate responde `Error{in_combat}` y el jugador sigue dentro.</summary>
public sealed record Logout(int? ReqId = null) : IClientMessage;

/// <summary>HU-063: pide la lista de jugadores conectados (tecla O); el servidor responde `OnlineList`.</summary>
public sealed record OnlineListRequest : IClientMessage;

/// <summary>HU-083: usa un objeto del mapa (una palanca) por su id de Tiled; el cambio llega como `MapObjects` a todo el mapa.</summary>
public sealed record Interact(string? ObjectId, int? ReqId = null) : IClientMessage;

public sealed record PartyKick(string Name) : IClientMessage;

public sealed record SetHotbar(int Slot, string? Kind = null, string? Ref = null) : IClientMessage;

public sealed record Respawn : IClientMessage;

public sealed record UsePortal(string PortalId) : IClientMessage;

public sealed record DuelRequest(string Name) : IClientMessage;

public sealed record DuelRespond(bool Accept) : IClientMessage;

public sealed record DuelForfeit : IClientMessage;

public sealed record TradeRequest(string Name) : IClientMessage;

public sealed record TradeRespond(bool Accept) : IClientMessage;

public sealed record TradeItemDto(string ItemId, int Qty);

public sealed record TradeOffer(IReadOnlyList<TradeItemDto> Items, long Gold) : IClientMessage;

public sealed record TradeConfirm(int Version) : IClientMessage;

public sealed record TradeCancel : IClientMessage;

public sealed record AdminCommand(string Text) : IClientMessage;

/// <summary>Mensaje nuevo C→S de HU-044 (cambio de clase en NPC).</summary>
public sealed record ChangeClass(int NpcId, string ClassId, int? ReqId = null) : IClientMessage;
