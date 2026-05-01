using FluentAssertions;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.ImportProfilesSpec;

public sealed class ImportProfileTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private static IReadOnlyList<ColumnMapping> ValidMappings() => new[]
    {
        new ColumnMapping("Data", TransactionField.Date, null),
        new ColumnMapping("Valor", TransactionField.Amount, null),
        new ColumnMapping("Descrição", TransactionField.Description, null),
    };

    [Fact]
    public void Create_succeeds_with_defaults()
    {
        var profile = ImportProfile.Create("Millennium CSV", ValidMappings(), Tenant);

        profile.Name.Should().Be("Millennium CSV");
        profile.Delimiter.Should().Be(";");
        profile.HasHeaderRow.Should().BeTrue();
        profile.DateFormat.Should().Be("dd-MM-yyyy");
        profile.DecimalSeparator.Should().Be(",");
        profile.SkipRows.Should().Be(0);
        profile.ColumnMappings.Should().HaveCount(3);
        profile.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_name(string name)
    {
        var act = () => ImportProfile.Create(name, ValidMappings(), Tenant);
        act.Should().Throw<ImportProfileNameRequiredException>();
    }

    [Fact]
    public void Create_rejects_name_above_max_length()
    {
        var name = new string('a', ImportProfile.NameMaxLength + 1);
        var act = () => ImportProfile.Create(name, ValidMappings(), Tenant);
        act.Should().Throw<ImportProfileNameTooLongException>();
    }

    [Fact]
    public void Create_rejects_empty_mappings()
    {
        var act = () => ImportProfile.Create("X", Array.Empty<ColumnMapping>(), Tenant);
        act.Should().Throw<ImportProfileMappingsRequiredException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(",,")]
    [InlineData("ab")]
    public void Create_rejects_invalid_delimiter(string delimiter)
    {
        var act = () => ImportProfile.Create("X", ValidMappings(), Tenant, delimiter: delimiter);
        act.Should().Throw<ImportProfileDelimiterInvalidException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(",,")]
    [InlineData("ab")]
    public void Create_rejects_invalid_decimal_separator(string decimalSeparator)
    {
        var act = () => ImportProfile.Create("X", ValidMappings(), Tenant, decimalSeparator: decimalSeparator);
        act.Should().Throw<ImportProfileDecimalSeparatorInvalidException>();
    }

    [Fact]
    public void Create_rejects_negative_skip_rows()
    {
        var act = () => ImportProfile.Create("X", ValidMappings(), Tenant, skipRows: -1);
        act.Should().Throw<ImportProfileSkipRowsNegativeException>();
    }

    [Fact]
    public void Create_trims_name()
    {
        var profile = ImportProfile.Create("  Millennium  ", ValidMappings(), Tenant);
        profile.Name.Should().Be("Millennium");
    }

    [Fact]
    public void Update_replaces_state_and_mappings()
    {
        var profile = ImportProfile.Create("Old", ValidMappings(), Tenant);
        var newMappings = new[]
        {
            new ColumnMapping("Date", TransactionField.Date, null),
            new ColumnMapping("Amount", TransactionField.Amount, null),
        };

        profile.Update("New", newMappings, ",", false, "yyyy-MM-dd", ".", 2);

        profile.Name.Should().Be("New");
        profile.Delimiter.Should().Be(",");
        profile.HasHeaderRow.Should().BeFalse();
        profile.DateFormat.Should().Be("yyyy-MM-dd");
        profile.DecimalSeparator.Should().Be(".");
        profile.SkipRows.Should().Be(2);
        profile.ColumnMappings.Should().HaveCount(2);
    }

    [Fact]
    public void Update_rejects_invalid_delimiter()
    {
        var profile = ImportProfile.Create("X", ValidMappings(), Tenant);
        var act = () => profile.Update("X", ValidMappings(), "ab", true, "dd-MM-yyyy", ",", 0);
        act.Should().Throw<ImportProfileDelimiterInvalidException>();
    }

    [Fact]
    public void Update_rejects_invalid_decimal_separator()
    {
        var profile = ImportProfile.Create("X", ValidMappings(), Tenant);
        var act = () => profile.Update("X", ValidMappings(), ";", true, "dd-MM-yyyy", "ab", 0);
        act.Should().Throw<ImportProfileDecimalSeparatorInvalidException>();
    }

    [Fact]
    public void Update_rejects_empty_mappings()
    {
        var profile = ImportProfile.Create("X", ValidMappings(), Tenant);
        var act = () => profile.Update("X", Array.Empty<ColumnMapping>(), ";", true, "dd-MM-yyyy", ",", 0);
        act.Should().Throw<ImportProfileMappingsRequiredException>();
    }

    [Fact]
    public void Archive_sets_deleted_at()
    {
        var profile = ImportProfile.Create("X", ValidMappings(), Tenant);
        profile.DeletedAt.Should().BeNull();
        profile.Archive();
        profile.DeletedAt.Should().NotBeNull();
    }
}
