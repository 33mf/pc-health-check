<#
.SYNOPSIS
    PCR-002：製品の PcReport.exe を変えずに MSIX に包み、（証明書があれば）署名する。

.DESCRIPTION
    1. src/PcReport を Release でビルドする
    2. MakeAppx.exe / SignTool.exe を NuGet（Microsoft.Windows.SDK.BuildTools）から取る
       ※ Windows SDK はインストールしない
    3. out/layout に exe・マニフェスト・仮アイコンを並べ、out/PcReport-dev.msix を作る
    4. CurrentUser\My にマニフェストの Publisher と同じ Subject の証明書があれば署名する
       （証明書は New-DevCert.ps1 で作る。秘密鍵をファイルに書き出さず、ストアから直接署名する）

    タイムスタンプは付けない（検証用。署名時にも通信しない）。
    管理者権限は要らない。

.PARAMETER SkipBuild
    exe のビルドを飛ばす（直前にビルド済みのとき）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\packaging\msix\Build-Msix.ps1
#>
[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'

$here     = $PSScriptRoot
$appRoot  = Resolve-Path (Join-Path $here '..\..')
$csproj   = Join-Path $appRoot 'src\PcReport\PcReport.csproj'
$binDir   = Join-Path $appRoot 'src\PcReport\bin\Release'
$outDir   = Join-Path $here 'out'
$layout   = Join-Path $outDir 'layout'
$msix     = Join-Path $outDir 'PcReport-dev.msix'
$manifest = Join-Path $here 'AppxManifest.xml'

function Invoke-Tool {
    param([string]$Exe, [string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw ("{0} が終了コード {1} で失敗しました" -f (Split-Path $Exe -Leaf), $LASTEXITCODE) }
}

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

# ---- 3. レイアウトを並べる ----------------------------------------------------

if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $layout 'Assets') | Out-Null

Copy-Item $exe $layout
$config = "$exe.config"
if (Test-Path $config) { Copy-Item $config $layout }
Copy-Item $manifest (Join-Path $layout 'AppxManifest.xml')

# 仮アイコン（検証用。ブランドの確定アイコンではない）。リポジトリに画像を置かないよう毎回描く
Add-Type -AssemblyName System.Drawing
function New-Icon([string]$Path, [int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::FromArgb(255, 43, 95, 179))
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float]($Size * 0.36)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString('PC', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $Size, $Size), $sf)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $font.Dispose()
}
New-Icon (Join-Path $layout 'Assets\StoreLogo.png')         50
New-Icon (Join-Path $layout 'Assets\Square150x150Logo.png') 150
New-Icon (Join-Path $layout 'Assets\Square44x44Logo.png')   44

# ---- 4. パッケージ化 ----------------------------------------------------------

Invoke-Tool $makeappx @('pack', '/o', '/d', $layout, '/p', $msix)

# ---- 5. 署名（証明書があるときだけ） ------------------------------------------

[xml]$m = Get-Content $manifest -Raw -Encoding UTF8
$publisher = $m.Package.Identity.Publisher
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1

if ($cert) {
    Invoke-Tool $signtool @('sign', '/fd', 'SHA256', '/s', 'My', '/sha1', $cert.Thumbprint, $msix)
    Write-Host ("署名しました（拇印 {0}）" -f $cert.Thumbprint) -ForegroundColor Green
}
else {
    Write-Host "署名用の証明書が CurrentUser\My にありません（Subject: $publisher）。未署名のまま置きます。" -ForegroundColor Yellow
    Write-Host '→ New-DevCert.ps1 を実行してから、もう一度このスクリプトを実行してください。' -ForegroundColor Yellow
}

Write-Host ''
Write-Host ("MSIX : {0}" -f $msix)
Write-Host ("exe  : SHA256 {0}" -f (Get-FileHash $exe -Algorithm SHA256).Hash)
