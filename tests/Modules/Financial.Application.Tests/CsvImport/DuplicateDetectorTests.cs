using FluentAssertions;
using Sextante.Modules.Financial.Infrastructure.CsvImport;

namespace Sextante.Modules.Financial.Application.Tests.CsvImport;

/// <summary>
/// Unit tests for the static normalization helper used by
/// <see cref="DuplicateDetector"/>. The DB-level scenarios
/// (match / no-match / tenant isolation) live in
/// <c>tests/Sextante.IntegrationTests/Financial/DuplicateDetectorIntegrationTests.cs</c>
/// because they require a relational provider that handles the
/// owned <c>Money</c> projection (Postgres in our setup).
/// </summary>
public sealed class DuplicateDetectorTests
{
    [Theory]
    [InlineData("Café Lisboa", "cafe lisboa")]
    [InlineData("CONTINENTE, Lisboa.", "continente lisboa")]
    [InlineData("  Açaí; Bowl  ", "acai bowl")]
    [InlineData("São_João", "sao joao")]
    [InlineData("Café-Lisboa", "cafe lisboa")]
    [InlineData("Multi   spaces   here", "multi spaces here")]
    public void NormalizeDescription_strips_diacritics_punctuation_and_collapses_spaces(string input, string expected)
    {
        DuplicateDetector.NormalizeDescription(input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeDescription_returns_empty_for_null()
    {
        DuplicateDetector.NormalizeDescription(null).Should().Be(string.Empty);
    }

    [Fact]
    public void NormalizeDescription_returns_empty_for_blank()
    {
        DuplicateDetector.NormalizeDescription("   ").Should().Be(string.Empty);
    }

    [Fact]
    public void NormalizeDescription_lowercases()
    {
        DuplicateDetector.NormalizeDescription("CONTINENTE").Should().Be("continente");
    }
}
