namespace GreyGray.Api.Storefront;

/// <summary>集中組出前台頁面與公開 API 網址，避免付款與物流各自複製一套設定判斷。</summary>
internal static class StorefrontUrls
{
    public static Uri BuildPublicUrl(
        IConfiguration configuration,
        string relativePath,
        string purpose)
    {
        var origin = configuration["Storefront:PublicOrigin"];
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin.TrimEnd('/'), UriKind.Absolute, out var publicOrigin))
        {
            throw new InvalidOperationException(
                "缺少設定 'Storefront:PublicOrigin'（前台對外的絕對網址，不含結尾斜線）。"
                + purpose);
        }

        return new Uri(
            $"{publicOrigin.GetLeftPart(UriPartial.Path).TrimEnd('/')}"
            + $"/{relativePath.TrimStart('/')}");
    }

    public static Uri BuildPublicApiUrl(
        IConfiguration configuration,
        HttpRequest request,
        string relativePath,
        string purpose)
    {
        var origin = configuration["Storefront:PublicApiOrigin"];
        if (string.IsNullOrWhiteSpace(origin))
        {
            return new Uri(
                $"{request.Scheme}://{request.Host}/{relativePath.TrimStart('/')}");
        }

        if (!Uri.TryCreate(origin.Trim().TrimEnd('/'), UriKind.Absolute, out var publicApiOrigin)
            || (publicApiOrigin.Scheme != Uri.UriSchemeHttp
                && publicApiOrigin.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"設定 'Storefront:PublicApiOrigin' 的值 '{origin}' 不是合法的對外 API 網址。"
                + "它必須是絕對網址、scheme 為 http 或 https（例如 https://greygray.shop），"
                + purpose);
        }

        return new Uri(
            $"{publicApiOrigin.GetLeftPart(UriPartial.Path).TrimEnd('/')}"
            + $"/{relativePath.TrimStart('/')}");
    }
}
