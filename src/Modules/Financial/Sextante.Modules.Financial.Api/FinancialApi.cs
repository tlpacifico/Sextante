using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Api.Endpoints;

namespace Sextante.Modules.Financial.Api;

public static class FinancialApi
{
    public static IEndpointRouteBuilder MapFinancialModule(this IEndpointRouteBuilder routes)
    {
        routes.MapAccountsEndpoints();
        routes.MapCategoriesEndpoints();
        routes.MapTransactionsEndpoints();
        routes.MapImportProfilesEndpoints();
        routes.MapCategorizationRulesEndpoints();
        routes.MapCsvImportEndpoints();
        routes.MapRecurringRulesEndpoints();
        routes.MapBudgetsEndpoints();
        return routes;
    }
}
