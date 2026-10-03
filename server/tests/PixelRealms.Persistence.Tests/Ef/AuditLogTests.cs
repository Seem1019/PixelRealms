using Microsoft.EntityFrameworkCore;
using PixelRealms.Persistence.Ef;
using PixelRealms.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace PixelRealms.Persistence.Tests.Ef;

/// <summary>HU-057 CA3: la auditoría de items viaja en lote con el guardado y acaba en `item_audit_log`.</summary>
public sealed class AuditLogTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    private const int Max = 4;

    private async Task<CharacterSaveDto> NewCharacterAsync(string name, CancellationToken ct)
    {
        var accounts = new EfAccountRepository(pg.Factory);
        var chars = new EfCharacterRepository(pg.Factory);
        var acc = (await accounts.CreateAsync(name.ToLowerInvariant(), "hash", ct)).ShouldNotBeNull();
        return (await chars.CreateAsync(new NewCharacter(acc.Id, name, "warrior", "meadow", 10, 12, 60, 0, [], []), Max, ct)).Character.ShouldNotBeNull();
    }

    private async Task<List<Entities.ItemAuditLog>> AuditRowsAsync(Guid characterId, CancellationToken ct)
    {
        await using var db = await pg.Factory.CreateDbContextAsync(ct);
        return await db.ItemAuditLog.AsNoTracking().Where(r => r.CharacterId == characterId).ToListAsync(ct);
    }

    [Fact]
    public async Task Save_WithAnAuditBatch_WritesOneRowPerAction_AndTheNextSaveDoesNotRepeatThem() // HU-057 CA3
    {
        var ct = TestContext.Current.CancellationToken;
        var chars = new EfCharacterRepository(pg.Factory);
        var created = await NewCharacterAsync("Auditada", ct);
        var admin = Guid.NewGuid(); var partner = Guid.NewGuid();
        AuditEntry[] batch =
        [
            new(Guid.CreateVersion7(), "loot", "bread", 5),
            new(Guid.CreateVersion7(), "buy", "minor_healing_potion", 3),
            new(Guid.CreateVersion7(), "sell", "boar_tusk", 2),
            new(Guid.CreateVersion7(), "destroy", "slime_goo", 1),
            new(Guid.CreateVersion7(), "split", "bread", 2),
            new(Guid.CreateVersion7(), "merge", "bread", 2),
            new(Guid.CreateVersion7(), "admin_give", "iron_sword", 1, admin),
            new(Guid.Empty, "admin_give", "gold", int.MaxValue, admin), // `/give gold` audita sin item y con la cantidad recortada a int
            new(Guid.CreateVersion7(), "trade_out", "copper_ore", 10, partner),
        ];
        await chars.SaveAsync(created with { Audit = batch }, ct);

        var rows = await AuditRowsAsync(created.Id, ct);
        rows.Count.ShouldBe(batch.Length);
        foreach (var a in batch)
            rows.ShouldContain(r => r.ItemId == a.ItemId && r.Action == a.Action && r.TemplateId == a.TemplateId && r.Quantity == a.Quantity
                                    && r.CounterpartyCharacterId == a.CounterpartyCharacterId, $"{a.Action} {a.TemplateId}");
        rows.Select(r => r.At).Distinct().Count().ShouldBe(1); // un solo lote, con el guardado
        rows.Select(r => r.Id).Distinct().Count().ShouldBe(batch.Length);

        // El siguiente guardado sin auditoría pendiente no repite ni borra filas.
        await chars.SaveAsync(created with { Audit = [] }, ct);
        (await AuditRowsAsync(created.Id, ct)).Count.ShouldBe(batch.Length);
    }

    [Fact]
    public async Task Save_ThatFails_WritesNoAuditRows() // HU-057 CA3 (borde: la auditoría va en la misma transacción que el guardado)
    {
        var ct = TestContext.Current.CancellationToken;
        var chars = new EfCharacterRepository(pg.Factory);
        var created = await NewCharacterAsync("Revertida", ct);
        // Dos items en la misma casilla violan el índice único de character_items: el guardado entero se revierte.
        SavedItem[] clash = [new(Guid.CreateVersion7(), "bread", 1, 0, 0), new(Guid.CreateVersion7(), "bread", 1, 0, 0)];
        AuditEntry[] audit = [new(clash[0].Id, "loot", "bread", 1)];
        await Should.ThrowAsync<DbUpdateException>(() => chars.SaveAsync(created with { Items = clash, Audit = audit }, ct));
        (await AuditRowsAsync(created.Id, ct)).ShouldBeEmpty();
    }
}
