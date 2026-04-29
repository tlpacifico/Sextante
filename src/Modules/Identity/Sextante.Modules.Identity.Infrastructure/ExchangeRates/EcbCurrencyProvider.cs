using System.Globalization;
using System.Xml.Linq;

namespace Sextante.Modules.Identity.Infrastructure.ExchangeRates;

/// <summary>
/// Lê <c>eurofxref-daily.xml</c> da ECB. Resilience (retry 3× / backoff
/// 2s exp / total timeout 10s) é aplicada via
/// <c>Microsoft.Extensions.Http.Resilience</c> no <c>AddHttpClient</c> do
/// host (ver <c>DependencyInjection.AddIdentityInfrastructure</c>).
/// </summary>
public sealed class EcbCurrencyProvider : ICurrencyProvider
{
    public const string FeedPath = "stats/eurofxref/eurofxref-daily.xml";

    private static readonly XNamespace GesmesNs = "http://www.gesmes.org/xml/2002-08-01";
    private static readonly XNamespace EurofxrefNs = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    private readonly HttpClient _http;

    public EcbCurrencyProvider(HttpClient http)
    {
        _http = http;
    }

    public async Task<EcbSnapshot> FetchLatestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _http
                .GetStreamAsync(FeedPath, cancellationToken)
                .ConfigureAwait(false);

            var doc = await XDocument
                .LoadAsync(stream, LoadOptions.None, cancellationToken)
                .ConfigureAwait(false);

            return Parse(doc);
        }
        catch (EcbProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new EcbProviderException(
                "Falha ao contactar ECB: " + ex.Message,
                ex);
        }
        catch (Exception ex)
        {
            throw new EcbProviderException(
                "Falha ao processar resposta ECB: " + ex.Message,
                ex);
        }
    }

    internal static EcbSnapshot Parse(XDocument doc)
    {
        // Estrutura: <gesmes:Envelope> <Cube> <Cube time="YYYY-MM-DD"> <Cube currency="USD" rate="1.0856"/>
        var envelope = doc.Element(GesmesNs + "Envelope")
            ?? throw new EcbProviderException("XML ECB sem Envelope.");

        var outerCube = envelope.Element(EurofxrefNs + "Cube")
            ?? throw new EcbProviderException("XML ECB sem Cube exterior.");

        var dayCube = outerCube.Element(EurofxrefNs + "Cube")
            ?? throw new EcbProviderException("XML ECB sem Cube por dia.");

        var dateAttr = dayCube.Attribute("time")?.Value
            ?? throw new EcbProviderException("Cube ECB sem atributo time.");

        if (!DateOnly.TryParseExact(dateAttr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var rateDate))
        {
            throw new EcbProviderException($"Cube ECB com time inválido: '{dateAttr}'.");
        }

        var rates = new List<EcbDailyRate>();
        foreach (var rateCube in dayCube.Elements(EurofxrefNs + "Cube"))
        {
            var currency = rateCube.Attribute("currency")?.Value;
            var rateValue = rateCube.Attribute("rate")?.Value;

            if (string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(rateValue))
            {
                throw new EcbProviderException(
                    "Cube de rate sem currency/rate em " + rateDate.ToString("yyyy-MM-dd"));
            }

            if (!decimal.TryParse(rateValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            {
                throw new EcbProviderException(
                    $"Rate inválido '{rateValue}' para {currency} em {rateDate:yyyy-MM-dd}.");
            }

            rates.Add(new EcbDailyRate(currency, rate));
        }

        if (rates.Count == 0)
        {
            throw new EcbProviderException("XML ECB sem rates.");
        }

        return new EcbSnapshot(rateDate, rates);
    }
}
