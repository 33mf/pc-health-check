<#
.SYNOPSIS
    製品の PcReport.exe を変えずに MSIX に包む。Dev（sideload の検証用。自己署名）と Store（提出用。未署名）を作り分ける。

.DESCRIPTION
    1. src/PcReport を Release でビルドする
    2. MakeAppx.exe / SignTool.exe を NuGet（Microsoft.Windows.SDK.BuildTools）から取る（Windows SDK はインストールしない）
    3. AppxManifest.xml（ひな形）の差し込み口を埋める
       - Identity・表示名：identity\dev.psd1 か identity\store.psd1
       - Version：Directory.Build.props の Version に ".0" を付ける（Store は 4 桁目を 0 にする決まり）
    4. out\layout-<target> に exe・マニフェスト・Assets\ のアイコンを並べ、MakeAppx でパッケージにする
    5. Dev だけ：CurrentUser\My に Publisher と同じ Subject の証明書があれば署名する（New-DevCert.ps1 で作る）
       Store は署名しない（Microsoft が認定後に署名し直す）

    Store は identity\store.psd1 に <<…>>（Partner Center の値の転記漏れ）が残っていると止まる。
    タイムスタンプは付けない（署名時にも通信しない）。管理者権限は要らない。

.PARAMETER Target
    Dev（既定）：out\PcReport-dev.msix（sideload 用。自己署名）
    Store      ：out\PcReport-store.msix（Partner Center に上げる。未署名）

.PARAMETER SkipBuild
    exe のビルドを飛ばす（直前にビルド済みのとき）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\packaging\msix\Build-Msix.ps1
    powershell -ExecutionPolicy Bypass -File .\packaging\msix\Build-Msix.ps1 -Target Store
#>
[CmdletBinding()]
param(
    [ValidateSet('Dev', 'Store')] [string]$Target = 'Dev',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$here     = $PSScriptRoot
$appRoot  = Resolve-Path (Join-Path $here '..\..')
$csproj   = Join-Path $appRoot 'src\PcReport\PcReport.csproj'
$binDir   = Join-Path $appRoot 'src\PcReport\bin\Release'
$outDir   = Join-Path $here 'out'
$suffix   = $Target.ToLowerInvariant()
$layout   = Join-Path $outDir "layout-$suffix"
$msix     = Join-Path $outDir "PcReport-$suffix.msix"
$template = Join-Path $here 'AppxManifest.xml'
$assets   = Join-Path $here 'Assets'

function Invoke-Tool {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw ("{0} が終了コード {1} で失敗しました" -f (Split-Path $Exe -Leaf), $LASTEXITCODE) }
}

# ---- 0. Identity と版 ------------------------------------------------------------

$identity = Import-PowerShellDataFile (Join-Path $here "identity\$suffix.psd1")
$missing = @($identity.GetEnumerator() | Where-Object { [string]$_.Value -match '<<.*>>' } | ForEach-Object Key)
if ($missing.Count -gt 0) {
    throw ("identity\{0}.psd1 に Partner Center の値が転記されていません：{1}" -f $suffix, ($missing -join ', '))
}
[xml]$props = Get-Content (Join-Path $appRoot 'Directory.Build.props') -Raw -Encoding UTF8
$productVersion = [version](@($props.Project.PropertyGroup.Version) | Where-Object { $_ } | Select-Object -First 1)
if (-not $productVersion -or $productVersion.Revision -gt 0) { throw "Directory.Build.props の Version を読めません（3 桁で書く）: $productVersion" }
$version = '{0}.{1}.{2}.0' -f $productVersion.Major, $productVersion.Minor, [Math]::Max(0, $productVersion.Build)

# ---- 1. 製品のビルド（製品コードは変えない） -----------------------------------

if (-not $SkipBuild) {
    Invoke-Tool 'dotnet' @('build', $csproj, '-c', 'Release', '-nologo', '-v', 'q')
}
$exe = Join-Path $binDir 'PcReport.exe'
if (-not (Test-Path $exe)) { throw "PcReport.exe がありません: $exe" }

# ---- 2. MakeAppx / SignTool を NuGet から取る ---------------------------------

Invoke-Tool 'dotnet' @('restore', (Join-Path $here 'tools.proj'), '-nologo', '-v', 'q')
[xml]$toolsProj = Get-Content (Join-Path $here 'tools.proj') -Raw -Encoding UTF8
$ver = ($toolsProj.Project.ItemGroup.PackageReference |
        Where-Object { $_.Include -eq 'Microsoft.Windows.SDK.BuildTools' }).Version
# 出力を受けきってから解析する（パイプで Select-Object -First を使うと dotnet が途中で止められ、終了コードが狂う）
$locals = @(& dotnet nuget locals global-packages --list)
if ($LASTEXITCODE -ne 0) { throw 'dotnet nuget locals が失敗しました' }
$pkgRoot = $locals | ForEach-Object { if ($_ -match '^global-packages:\s*(.+)$') { $Matches[1].Trim() } } |
           Select-Object -First 1
if (-not $pkgRoot) { throw 'NuGet のパッケージフォルダを特定できませんでした' }
$toolDir = Get-ChildItem (Join-Path $pkgRoot "microsoft.windows.sdk.buildtools\$ver\bin") -Directory |
           Where-Object { $_.Name -as [version] } |
           Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 |
           ForEach-Object { Join-Path $_.FullName 'x64' }
$makeappx = Join-Path $toolDir 'makeappx.exe'
$signtool = Join-Path $toolDir 'signtool.exe'
foreach ($t in $makeappx, $signtool) { if (-not (Test-Path $t)) { throw "見つかりません: $t" } }

# ---- 3. マニフェストの差し込み口を埋める ---------------------------------------

function Esc([string]$s) { [System.Security.SecurityElement]::Escape($s) }
$xml = Get-Content $template -Raw -Encoding UTF8
$xml = $xml.Replace('$IDENTITY_NAME$', (Esc $identity.Name)).
            Replace('$PUBLISHER$', (Esc $identity.Publisher)).
            Replace('$PUBLISHER_DISPLAY_NAME$', (Esc $identity.PublisherDisplayName)).
            Replace('$DISPLAY_NAME$', (Esc $identity.DisplayName)).
            Replace('$VERSION$', $version)
# コメント以外に $…$ が残っていたら埋め忘れ
$body = [regex]::Replace($xml, '<!--.*?-->', '', 'Singleline')
$left = [regex]::Matches($body, '\$[A-Z_]+\$') | ForEach-Object Value | Sort-Object -Unique
if ($left) { throw ("マニフェストの差し込み口が埋まっていません：{0}" -f ($left -join ', ')) }

# ---- 4. レイアウトを並べてパッケージにする ------------------------------------

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $layout 'Assets') | Out-Null

Copy-Item $exe $layout
$config = "$exe.config"
if (Test-Path $config) { Copy-Item $config $layout }
[System.IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $xml, (New-Object System.Text.UTF8Encoding($false)))
foreach ($name in 'Square44x44Logo.png', 'Square150x150Logo.png', 'StoreLogo.png') {
    $src = Join-Path $assets $name
    if (-not (Test-Path $src)) { throw "アイコンがありません: $src（New-ProvisionalIcons.ps1 で仮のものを作れる）" }
    Copy-Item $src (Join-Path $layout 'Assets')
}

Invoke-Tool $makeappx @('pack', '/o', '/d', $layout, '/p', $msix)

# ---- 5. 署名（Dev で、証明書があるときだけ） ----------------------------------

if ($Target -eq 'Dev') {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object { $_.Subject -eq $identity.Publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($cert) {
        Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $cert.Thumbprint, $msix)
        Write-Host ("署名しました（拇印 {0}）" -f $cert.Thumbprint) -ForegroundColor Green
    }
    else {
        Write-Host ("署名用の証明書が CurrentUser\My にありません（Subject: {0}）。未署名のまま置きます。" -f $identity.Publisher) -ForegroundColor Yellow
        Write-Host '→ New-DevCert.ps1 を実行してから、もう一度このスクリプトを実行してください。' -ForegroundColor Yellow
    }
}
else {
    Write-Host 'Store 用は署名しません（Microsoft が認定後に署名し直す）。Partner Center にこの .msix を上げる。' -ForegroundColor Green
}

Write-Host ''
Write-Host ("Target  : {0}" -f $Target)
Write-Host ("Identity: {0} / {1}" -f $identity.Name, $identity.Publisher)
Write-Host ("Version : {0}" -f $version)
Write-Host ("MSIX    : {0}" -f $msix)
Write-Host ("exe     : SHA256 {0}" -f (Get-FileHash $exe -Algorithm SHA256).Hash)
