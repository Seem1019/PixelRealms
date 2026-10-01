using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PixelRealms.Server.Hosting;

namespace PixelRealms.Server.Tests.Helpers;

/// <summary>Arranca el servidor real en un puerto libre de 127.0.0.1 (sin Mvc.Testing: el WebSocket real importa).</summary>
public sealed class TestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestServer(WebApplication app) => _app = app;

    public string BaseUrl => _app.Urls.First();

    public string WsUrl => BaseUrl.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws";

    public IServiceProvider Services => _app.Services;

    public static async Task<TestServer> StartAsync(Dictionary<string, string?>? settings = null)
    {
        var app = ServerApp.Build([], b =>
        {
            b.Environment.EnvironmentName = "Development";
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Urls"] = "http://127.0.0.1:0",
                ["Content:Dir"] = TestContent.ContentDir,
                ["Net:RequireTicket"] = "false",
            });
            if (settings is not null) b.Configuration.AddInMemoryCollection(settings);
            b.Logging.ClearProviders();
        }) ?? throw new InvalidOperationException("content inválido");
        await app.StartAsync();
        return new TestServer(app);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
