using PixelRealms.Server.Hosting;

var app = ServerApp.Build(args);
if (app is null) return 1;
app.Run();
return 0;
