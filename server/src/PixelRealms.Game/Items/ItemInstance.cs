namespace PixelRealms.Game.Items;

/// <summary>Copia viva de un item (skill inventory-items). El Id (UUID v7) nunca se reutiliza.</summary>
public sealed class ItemInstance(Guid id, string templateId, int qty)
{
    public Guid Id { get; } = id;

    public string TemplateId { get; } = templateId;

    public int Qty { get; set; } = qty;

    public static ItemInstance New(string templateId, int qty = 1) => new(Guid.CreateVersion7(), templateId, qty);
}

/// <summary>Bolsa de 24 casillas y oro en cobre. Las operaciones (mover, apilar, dividir…) llegan en HU-051 (`InventoryOps`).</summary>
public sealed class Inventory
{
    public const int BagSize = 24;

    public ItemInstance?[] Bag { get; } = new ItemInstance?[BagSize];

    public long Gold { get; set; }

    public int FreeSlots { get { var n = 0; foreach (var s in Bag) if (s is null) n++; return n; } }
}

/// <summary>Índices de EquipSlot del protocolo: 0 head, 1 neck, 2 chest, 3 hands, 4 legs, 5 feet, 6 ring, 7 main_hand, 8 off_hand.</summary>
public sealed class Equipment
{
    public const int SlotCount = 9;

    public ItemInstance?[] Slots { get; } = new ItemInstance?[SlotCount];

    public ItemInstance? MainHand => Slots[7];
}
