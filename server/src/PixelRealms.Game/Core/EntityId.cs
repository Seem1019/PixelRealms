namespace PixelRealms.Game.Core;

/// <summary>Id de entidad en el mundo: int asignado por el servidor, válido durante la sesión (docs/protocol.md).</summary>
public readonly record struct EntityId(int Value)
{
    public static readonly EntityId None = new(0);

    public bool IsNone => Value == 0;

    public override string ToString() => Value.ToString();
}

/// <summary>Generador secuencial de ids de entidad (un solo hilo lo usa: el tick).</summary>
public sealed class EntityIdAllocator
{
    private int _next = 1;

    public EntityId Next() => new(_next++);
}
