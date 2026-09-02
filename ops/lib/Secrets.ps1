<#
    機密產生與落地的共用函式。

    這裡不是「新機制」——ops/install-dev-environment.ps1 已經在用的那一套
    （$InstallRoot\secrets\ 目錄 ＋ ACL 收緊到目前使用者 ＋ 明文檔案 ＋
    「已存在就重用、不存在就生成」）被抽出來給 ops/deploy.ps1 用，
    讓正式機跟開發機走同一個形狀。

    ★ 刻意不去改 install-dev-environment.ps1 既有的 New-RandomPassword／
      Protect-SecretDirectory：那兩個函式已經在跑、已經驗證過，把它們改成
      呼叫這裡的版本是純粹多出來的風險，換不到任何東西（見 docs/22 §1）。
      install-dev-environment.ps1 只從這裡取 New-DataProtectionKey 一個函式；
      它 dot-source 這個檔之後才定義自己那兩個同名函式，所以它用到的仍然是自己那份。
#>
Set-StrictMode -Version Latest

function New-DataProtectionKey {
    <#
        .SYNOPSIS
        產生 Identity:DataProtectionKey——32 bytes 亂數的 Base64 字串。

        .DESCRIPTION
        ★ 不做任何字元替換，這一點不能改。
        IdentityDataProtector（src/Modules/Identity/GreyGray.Modules.Identity.Infra/
        IdentityDataProtector.cs:13-30）會 Convert.FromBase64String 之後要求
        「解碼結果正好 32 bytes」（AES-256 金鑰長度）。

        不可以沿用 New-RandomPassword／New-SecretPassword：它們會把 Base64 裡的
        + / = 換成 x。換掉之後要嘛直接 FormatException，要嘛（更糟）湊巧仍然解得開，
        但解出來的位元組已經不是原本產生的那組亂數，而且 Host 不會有任何抱怨——
        直到需要 Unprotect 舊資料時才會炸。

        ★ 不要用 [RandomNumberGenerator]::Fill($bytes)：那個靜態方法是
        .NET Core 3.0／.NET 5+ 才有的，Windows PowerShell 5.1 跑在 .NET Framework 4.x 上，
        呼叫下去會得到「不包含名為 'Fill' 的方法」。正式機 YC 只有 5.1，deploy.ps1 第一次
        真跑就是炸在這裡（17 支 migration 之後）。::Create() ＋ .GetBytes() 兩邊都有。
    #>
    [CmdletBinding()]
    param()

    $bytes = [byte[]]::new(32)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($bytes)
}

function New-SecretPassword {
    <#
        .SYNOPSIS
        產生資料庫角色用的隨機密碼。

        .DESCRIPTION
        邏輯與 ops/install-dev-environment.ps1:54-59 的 New-RandomPassword 相同：
        Base64 之後把 + / = 換成 x，讓密碼在連線字串、SQL 字面值裡都不需要跳脫。
        這裡取另一個名字，是為了讓「同名不同檔」不會發生——dev 腳本 dot-source
        這個檔之後仍然定義自己的 New-RandomPassword，兩個名字分開才看得出誰是誰。

        ★ 同上：不要用 ::Fill，Windows PowerShell 5.1（.NET Framework 4.x）沒有那個方法。
    #>
    [CmdletBinding()]
    param([ValidateRange(16, 256)][int]$Length = 32)

    $bytes = [byte[]]::new($Length)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x')
}

function Protect-SecretDirectory {
    <#
        .SYNOPSIS
        把明文機密目錄的 ACL 收緊到「目前使用者 ＋ Administrators ＋ SYSTEM」。

        .DESCRIPTION
        邏輯與 ops/install-dev-environment.ps1:74-83 相同，這裡是給 ops/deploy.ps1
        用的獨立一份（deploy.ps1 不 dot-source install-dev-environment.ps1）。
        SetAccessRuleProtection($true, $false) 是關鍵：斷開繼承，而且不保留
        繼承下來的規則，否則父目錄的 Users 讀取權會原封不動留著。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @(
            [Security.Principal.WindowsIdentity]::GetCurrent().Name,
            'BUILTIN\Administrators',
            'NT AUTHORITY\SYSTEM')) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule(
            $identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}
