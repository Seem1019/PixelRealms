using PixelRealms.Persistence.Repositories;
using PixelRealms.Protocol;
using PixelRealms.Protocol.Messages;
using PixelRealms.Server.Auth;
using PixelRealms.Server.Hosting;
using PixelRealms.Server.Net;

namespace PixelRealms.Server.Players;

/// <summary>HU-014 CA3/CA4: versión igual, ticket válido (un solo uso, 30 s) y personaje leído de BD fuera del tick.</summary>
public sealed class HelloGate(TicketService tickets, ICharacterRepository characters, IAccountRepository accounts, Microsoft.Extensions.Options.IOptions<NetOptions> net,
    SaveService? saver = null) : IHelloGate
{
    /// <summary>Máximo que un Hello espera a los guardados pendientes de su personaje.</summary>
    public static readonly TimeSpan SaveWaitTimeout = TimeSpan.FromSeconds(3);

    public async Task<HelloResult> ProcessAsync(Hello hello, WebSocketSession session, CancellationToken ct)
    {
        if (hello.ProtocolVersion != ProtocolVersion.Current) return HelloResult.Fail(ErrorCodes.BadVersion);
        var ticket = tickets.Redeem(hello.Ticket ?? "", DateTimeOffset.UtcNow);
        if (ticket is null)
        {
            // Flag de desarrollo: sin ticket, el ticket es "dev:<characterId>" para probar sin REST (solo si RequireTicket = false).
            if (net.Value.RequireTicket || hello.Ticket is null || !hello.Ticket.StartsWith("dev:", StringComparison.Ordinal) || !Guid.TryParse(hello.Ticket[4..], out var devId))
                return HelloResult.Fail(ErrorCodes.BadTicket);
            var devGen = saver?.WrittenGeneration(devId) ?? 0;
            var devChar = await characters.LoadAsync(devId, ct);
            if (devChar is null) return HelloResult.Fail(ErrorCodes.BadTicket);
            session.AccountId = devChar.AccountId;
            session.CharacterId = devChar.Id;
            session.IsAdmin = (await accounts.GetAsync(devChar.AccountId, ct))?.IsAdmin ?? false;
            return HelloResult.Ok(new LoadedCharacter(devChar, devGen));
        }
        // HU-015 CA5: si acaba de salir (Logout o cierre), su guardado puede seguir en cola: se espera a que se escriba. Si no
        // llega a tiempo, la generación leída antes de cargar le dice al tick que use el estado que tiene en memoria.
        if (saver is not null) await saver.WaitForCharacterAsync(ticket.CharacterId, SaveWaitTimeout, ct);
        var gen = saver?.WrittenGeneration(ticket.CharacterId) ?? 0;
        var character = await characters.LoadAsync(ticket.CharacterId, ct);
        if (character is null || character.AccountId != ticket.AccountId) return HelloResult.Fail(ErrorCodes.BadTicket);
        session.AccountId = ticket.AccountId;
        session.CharacterId = ticket.CharacterId;
        session.IsAdmin = ticket.Admin;
        return HelloResult.Ok(new LoadedCharacter(character, gen));
    }
}

/// <summary>
/// Personaje leído de la BD fuera del tick y la generación de guardado que ya estaba escrita antes de leerlo
/// (<see cref="SaveService.WrittenGeneration"/>). El tick decide con ella si la lectura está atrasada (HU-015 CA5).
/// </summary>
public sealed record LoadedCharacter(CharacterSaveDto Dto, long WrittenGen);
