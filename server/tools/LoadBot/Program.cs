using PixelRealms.Tools.LoadBot;

// HU-089: `dotnet run -c Release --project server/tools/LoadBot -- [--duration 300] [--bots 30] [--monsters 300] [--size 60] [--seed 7] [--bench]`
//         con `--network http://127.0.0.1:5099`: bots reales por WebSocket contra un servidor en marcha (NetworkScenario).
var options = LoadOptions.Parse(args);
if (options.Bench) return MicroBench.Run(options) ? 0 : 1;
if (options.Network is not null) return await NetworkScenario.RunAsync(options, Console.Out) ? 0 : 1;
var result = CombatScenario.Run(options, Console.Out);
Console.WriteLine(result.Report());
return result.Passed ? 0 : 1;
