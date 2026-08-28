using GreyGray.Modules.Identity.Contracts;

namespace GreyGray.Api.Storefront;

/// <summary>只用於 M0 垂直切片的開發環境端點。</summary>
internal static class M0CustomerEndpoints
{
    public static IEndpointRouteBuilder MapM0CustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/customers", RegisterAsync)
            .ExcludeFromDescription();
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        CreateCustomerRequest request,
        ICustomerProvisioning provisioning,
        CancellationToken cancellationToken)
    {
        var result = await provisioning.CreateAsync(request.DisplayName, cancellationToken);
        if (result.IsFailure)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: result.Error.Message,
                type: $"https://greygray.tw/errors/{result.Error.Code}",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.Error.Code,
                });
        }

        var customer = result.Value;
        return Results.Json(
            new CustomerResponse(
                customer.Id.ToString(),
                customer.DisplayName,
                customer.Tier.ToString(),
                customer.IsActive),
            statusCode: StatusCodes.Status201Created);
    }

    private sealed record CreateCustomerRequest(string DisplayName);

    private sealed record CustomerResponse(
        string Id,
        string DisplayName,
        string Tier,
        bool IsActive);
}
