using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PixelRealms.Game.Core;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>Arranca el servidor real en un puerto libre de 127.0.0.1 (sin Mvc.Testing: el WebSocket real importa).</summary>
public sealed class TestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestServer(WebApplication app) => _app = app;

    private readonly System.Collections.Concurrent.ConcurrentQueue<(Action<TickContext> Action, TaskCompletionSource Done)> _tickActions = new();

    /// <summary>Ejecuta `action` en el hilo del tick, después de procesar los mensajes de ese tick (regla 2: solo el tick toca el mundo).</summary>
    public Task RunOnTickAsync(Action<TickContext> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tickActions.Enqueue((action, done));
        return done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private void DrainTickActions(TickContext tick)
    {
        while (_tickActions.TryDequeue(out var item))
        {
            try { item.Action(tick); item.Done.TrySetResult(); }
            catch (Exception ex) { item.Done.TrySetException(ex); }
        }
    }

    public string BaseUrl => _app.Urls.First();

    public string WsUrl => BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws";

    public IServiceProvider Services => _app.Services;

    public static async Task<TestServer> StartAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? overrideServices = null)
    {
        var app = ServerApp.Build([], b =>
        {
            b.Environment.EnvironmentName = "Development";
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://127.0.0.1:0",
                ["Content:Dir"] = TestContent.ContentDir,
                ["Net:RequireTicket"] = "false",
                ["Persistence:Provider"] = "InMemory",
            });
            if (settings is not null) b.Configuration.AddInMemoryCollection(settings);
            b.Logging.ClearProviders();
        }, overrideServices) ?? throw new InvalidOperationException("content inválido");
        var server = new TestServer(app);
        // Antes de arrancar el loop: la lista de ganchos no se toca con el tick en marcha.
        app.Services.GetRequiredService<Simulation>().OnPreTick(server.DrainTickActions);
        await app.StartAsync();
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
