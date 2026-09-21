[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$StorefrontBaseUrl = 'https://greygray.shop',

    [Parameter()]
    [ValidateRange(5, 60)]
    [int]$TimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ResponseText {
    param([Parameter(Mandatory)][System.Net.Http.HttpResponseMessage]$Response)

    return $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

function Get-VisibleText {
    param([Parameter(Mandatory)][string]$Html)

    $withoutExecutableContent = [regex]::Replace(
        $Html,
        '(?is)<(?:script|style)\b.*?</(?:script|style)>',
        ' ')
    $withoutTags = [regex]::Replace($withoutExecutableContent, '(?is)<[^>]+>', ' ')
    return [regex]::Replace(
        [System.Net.WebUtility]::HtmlDecode($withoutTags),
        '\s+',
        ' ').Trim()
}

$baseUri = [Uri]$StorefrontBaseUrl
if ($baseUri.Scheme -ne [Uri]::UriSchemeHttps) {
    throw '正式物流驗收只允許 https Storefront URL。'
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = $baseUri
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)

try {
    $sessionBody = [System.Net.Http.StringContent]::new(
        '{"device":"Desktop"}',
        [System.Text.Encoding]::UTF8,
        'application/json')
    $sessionResponse = $client.PostAsync(
        '/v1/logistics/cvs-map-sessions',
        $sessionBody).GetAwaiter().GetResult()
    $sessionText = Get-ResponseText -Response $sessionResponse
    if (-not $sessionResponse.IsSuccessStatusCode) {
        throw "Storefront 選店 session 建立失敗（HTTP $([int]$sessionResponse.StatusCode)）。"
    }

    $session = $sessionText | ConvertFrom-Json
    if ($session.method -cne 'POST') {
        throw "選店 session 的 method 必須是 POST，實得 '$($session.method)'。"
    }

    $providerUri = [Uri][string]$session.action
    if ($providerUri.Scheme -ne [Uri]::UriSchemeHttps -or
        ($providerUri.Host -cne 'ecpay.com.tw' -and
            -not $providerUri.Host.EndsWith('.ecpay.com.tw', [StringComparison]::OrdinalIgnoreCase))) {
        throw '選店 session 沒有指向綠界 https 網址。'
    }

    $requiredFields = @(
        'MerchantID',
        'LogisticsType',
        'LogisticsSubType',
        'IsCollection',
        'ServerReplyURL',
        'ExtraData'
    )
    foreach ($name in $requiredFields) {
        $property = $session.fields.PSObject.Properties[$name]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "選店 session 缺少必要欄位 $name。"
        }
    }
    if ($session.fields.LogisticsType -cne 'CVS' -or
        $session.fields.LogisticsSubType -cne 'UNIMARTC2C' -or
        $session.fields.IsCollection -cne 'N') {
        throw '選店 session 的 CVS/C2C/代收設定不符合正式規格。'
    }

    $pairs = [System.Collections.Generic.List[
        System.Collections.Generic.KeyValuePair[string, string]]]::new()
    foreach ($property in $session.fields.PSObject.Properties) {
        $pairs.Add([System.Collections.Generic.KeyValuePair[string, string]]::new(
            $property.Name,
            [string]$property.Value))
    }

    $form = [System.Net.Http.FormUrlEncodedContent]::new($pairs)
    $providerResponse = $client.PostAsync($providerUri, $form).GetAwaiter().GetResult()
    $providerHtml = Get-ResponseText -Response $providerResponse
    if (-not $providerResponse.IsSuccessStatusCode) {
        throw "綠界電子地圖拒絕請求（HTTP $([int]$providerResponse.StatusCode)）。"
    }

    $visibleText = Get-VisibleText -Html $providerHtml
    $failurePatterns = @(
        '找不到加密金鑰',
        '請確認是否有申請開通此物流方式',
        'LogisticsType Is Not Match',
        'MerchantID Is Not Match',
        '系統發生錯誤'
    )
    foreach ($pattern in $failurePatterns) {
        if ($visibleText.Contains($pattern, [StringComparison]::OrdinalIgnoreCase)) {
            throw "綠界電子地圖驗收失敗：$pattern。請確認正式 MerchantID 已開通 7-ELEVEN C2C 店到店。"
        }
    }

    if ($providerHtml.Length -lt 1000) {
        throw '綠界回應內容過短，無法證明電子地圖已載入。'
    }

    Write-Host 'PASS 正式物流：Storefront 已產生 C2C 表單，且綠界正式電子地圖接受請求。'
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
