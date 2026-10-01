using PixelRealms.Tools.LoadBot;

// HU-089: `dotnet run -c Release --project server/tools/LoadBot -- [--duration 300] [--bots 30] [--monsters 300] [--size 60] [--seed 7] [--bench]`
var options = LoadOptions.Parse(args);
if (options.Bench) { MicroBench.Run(); return 0; }
var result = CombatScenario.Run(options, Console.Out);
Console.WriteLine(result.Report());
return result.Passed ? 0 : 1;
