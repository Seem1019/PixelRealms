using Shouldly;
using Xunit;

namespace PixelRealms.Game.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void TestProject_Compiles_AndRuns() => (1 + 1).ShouldBe(2); // HU-001 CA2
}
