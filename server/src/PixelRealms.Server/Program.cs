using Microsoft.Extensions.DependencyInjection;
using PixelRealms.Persistence.Repositories;
using PixelRealms.Server.Hosting;

// HU-070 CA4: `dotnet run --project server/src/PixelRealms.Server -- make-admin <usuario>` marca la cuenta como admin y sale.
if (args.Length >= 2 && args[0] == "make-admin")
{
    var cli = ServerApp.Build([]);
    if (cli is null) return 1;
    var accounts = cli.Services.GetRequiredService<IAccountRepository>();
    var account = await accounts.FindByUsernameAsync(args[1]);
    if (account is null) { Console.Error.WriteLine($"Cuenta '{args[1]}' no encontrada"); return 2; }
    await accounts.SetAdminAsync(account.Id, true);
    Console.WriteLine($"{account.Username} ahora es admin");
    return 0;
}

var app = ServerApp.Build(args);
if (app is null) return 1;
app.Run();
return 0;
