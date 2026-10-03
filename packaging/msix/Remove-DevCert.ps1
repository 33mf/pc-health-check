<#
.SYNOPSIS
    後片付け：sideload で入れた MSIX と自己署名証明書を開発機から消す（PCR-002・PCR-005）。

.DESCRIPTION
    - sideload で入れたパッケージをアンインストールする（管理者不要）
    - CurrentUser\My の証明書（秘密鍵つき）を消す（管理者不要）
    - LocalMachine\TrustedPeople の証明書を消す（管理者が要る。昇格していなければ飛ばして案内を出す）

    どれも Subject（identity\<dev|store>.psd1 の Publisher）が一致するものだけを消す。
    パッケージは自己署名で入れたもの（SignatureKind が Developer）だけを消す。
    Store から入れた本物（同じ Name・Publisher で、Microsoft の署名）は消さない。
    -Thumbprint を渡すと、その拇印の証明書だけに絞る（パッケージの絞り込みには効かない）。

.PARAMETER Identity
    Dev（既定）：検証版（Microforge.PcReport.Dev）と、その証明書
    Store      ：Build-Msix.ps1 -Target Store -Sideload で入れたもの（MicroForge.PC）と、その証明書
#>
[CmdletBinding()]
param(
    [ValidateSet('Dev', 'Store')] [string]$Identity = 'Dev',
    [string]$Thumbprint
)

$ErrorActionPreference = 'Stop'

$suffix = $Identity.ToLowerInvariant()
$identityData = Import-PowerShellDataFile (Join-Path $PSScriptRoot "identity\$suffix.psd1")
$subject = $identityData.Publisher
$pkgName = $identityData.Name
if (-not $subject -or -not $pkgName) { throw "identity\$suffix.psd1 から Publisher / Name を読めませんでした。何も消していません。" }
if ($Thumbprint) { $Thumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant() }

function Select-Target($certs) {
    $certs | Where-Object { $_.Subject -eq $subject -and (-not $Thumbprint -or $_.Thumbprint -eq $Thumbprint) }
}

# 自己署名で入れたものだけ（Store から入れた本物は SignatureKind が Store なので外れる）
function Get-SideloadedPackage {
    Get-AppxPackage -Name $pkgName | Where-Object { $_.Publisher -eq $subject -and $_.SignatureKind -eq 'Developer' }
}

# 1. パッケージ
Get-SideloadedPackage | ForEach-Object {
    Remove-AppxPackage -Package $_.PackageFullName
    Write-Host ("アンインストール: {0}" -f $_.PackageFullName) -ForegroundColor Green
}

# 2. CurrentUser\My（秘密鍵つき）
Select-Target (Get-ChildItem Cert:\CurrentUser\My) | ForEach-Object {
    Remove-Item $_.PSPath -DeleteKey
    Write-Host ("削除: Cert:\CurrentUser\My  {0}" -f $_.Thumbprint) -ForegroundColor Green
}

# 3. LocalMachine\TrustedPeople（管理者のみ）
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$trusted = @(Select-Target (Get-ChildItem Cert:\LocalMachine\TrustedPeople))
if ($trusted.Count -gt 0) {
    if ($isAdmin) {
        $trusted | ForEach-Object {
            Remove-Item $_.PSPath
            Write-Host ("削除: Cert:\LocalMachine\TrustedPeople  {0}" -f $_.Thumbprint) -ForegroundColor Green
        }
    }
    else {
        Write-Host 'LocalMachine\TrustedPeople に残っています。管理者として次を実行してください：' -ForegroundColor Yellow
        $trusted | ForEach-Object {
            Write-Host ("  Remove-Item 'Cert:\LocalMachine\TrustedPeople\{0}'" -f $_.Thumbprint)
        }
    }
}

# 4. 書き出した公開鍵（.cer）。害は無いが、置いておく理由も無い
$cer = Join-Path $PSScriptRoot "out\PcReport-$suffix.cer"
if (Test-Path $cer) {
    Remove-Item $cer
    Write-Host ("削除: {0}" -f $cer) -ForegroundColor Green
}

# 確認
$left = @(Select-Target (Get-ChildItem Cert:\CurrentUser\My) | ForEach-Object { "Cert:\CurrentUser\My\$($_.Thumbprint)" }) +
        @(Select-Target (Get-ChildItem Cert:\LocalMachine\TrustedPeople) | ForEach-Object { "Cert:\LocalMachine\TrustedPeople\$($_.Thumbprint)" }) +
        @(Get-SideloadedPackage | ForEach-Object { "パッケージ $($_.PackageFullName)" })
if ($left.Count -eq 0) {
    Write-Host '残っているものはありません。' -ForegroundColor Green
}
else {
    Write-Host '残っているもの：' -ForegroundColor Yellow
    $left | ForEach-Object { Write-Host ("  {0}" -f $_) -ForegroundColor Yellow }
    exit 1
}
