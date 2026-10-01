using PixelRealms.Server.Api;
using Shouldly;
using Xunit;

namespace PixelRealms.Server.Tests.Api;

/// <summary>Anclas de las regex de validación: un salto de línea al borde no debe colarse (suplantación "Bob\n" ≈ "Bob").</summary>
public sealed class ValidationTests
{
    [Theory]
    [InlineData("Bob\n")]
    [InlineData("Bob\r\n")]
    [InlineData("\nBob")]
    public void Username_WithLineBreakAtEdge_IsInvalidChars(string username) => // HU-010 CA3
        Validation.Username(username).ShouldNotBeNull().Code.ShouldBe("invalid_chars");

    [Theory]
    [InlineData("Bob\n")]
    [InlineData("Bob\r\n")]
    [InlineData("\nBob")]
    public void CharacterName_WithLineBreakAtEdge_IsInvalidChars(string name) => // HU-012 CA2
        Validation.CharacterName(name).ShouldNotBeNull().Code.ShouldBe("invalid_chars");

    [Fact]
    public void ValidNames_Pass()
    {
        Validation.Username("Bob_99").ShouldBeNull();
        Validation.CharacterName("Bob99").ShouldBeNull();
    }
}
