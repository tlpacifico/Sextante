using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sextante.SharedKernel;

/// <summary>
/// Value object monetário com Amount + Currency. Tech-stack §7.1: nenhum
/// `decimal` solto em Domain — toda quantia material atravessa este tipo.
/// </summary>
[JsonConverter(typeof(MoneyJsonConverter))]
public sealed partial record Money
{
    /// <summary>
    /// EF Core OwnsOne exige tipo de referência com construtor parameterless;
    /// hidden-but-required para reidratação do read-side.
    /// </summary>
    private Money()
    {
        Amount = 0m;
        Currency = "EUR";
    }

    public Money(decimal amount, string currency)
    {
        if (currency is null)
        {
            throw new ArgumentNullException(nameof(currency));
        }

        if (!CurrencyRegex().IsMatch(currency))
        {
            throw new ArgumentException(
                $"Currency '{currency}' inválido — esperado ISO 4217 (3 letras maiúsculas).",
                nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; init; }
    public string Currency { get; init; }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Divide(decimal divisor)
    {
        if (divisor == 0m)
        {
            throw new DivideByZeroException("Não é possível dividir Money por zero.");
        }

        return new Money(Amount / divisor, Currency);
    }

    public Money ConvertTo(string targetCurrency, decimal exchangeRate)
        => new(Amount * exchangeRate, targetCurrency);

    public static Money Zero(string currency) => new(0m, currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new MoneyCurrencyMismatchException(Currency, other.Currency);
        }
    }

    public override string ToString()
        => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyRegex();
}

public sealed class MoneyCurrencyMismatchException : InvalidOperationException
{
    public MoneyCurrencyMismatchException(string left, string right)
        : base($"Mismatch de moeda: '{left}' vs '{right}'.")
    {
        Left = left;
        Right = right;
    }

    public string Left { get; }
    public string Right { get; }
}

/// <summary>
/// Serializa <see cref="Money"/> como <c>{ "amount": 12.34, "currency": "EUR" }</c>.
/// </summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Esperado objeto Money.");
        }

        decimal? amount = null;
        string? currency = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, "amount", StringComparison.OrdinalIgnoreCase))
            {
                amount = reader.GetDecimal();
            }
            else if (string.Equals(propertyName, "currency", StringComparison.OrdinalIgnoreCase))
            {
                currency = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        if (amount is null || currency is null)
        {
            throw new JsonException("Money exige 'amount' e 'currency'.");
        }

        return new Money(amount.Value, currency);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}
