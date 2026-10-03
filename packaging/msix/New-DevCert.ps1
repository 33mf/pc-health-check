<#
.SYNOPSIS
    MSIX に署名するための自己署名証明書を作る（開発機だけで使う。PCR-002・PCR-005）。

.DESCRIPTION
    1. 【管理者不要】CurrentUser\My（個人）にコード署名用の自己署名証明書を作る。
       秘密鍵はこのストアの中だけ（NonExportable。.pfx に書き出せない）。有効期限は 30 日。
    2. 公開鍵だけの .cer を out\ に書き出す（out\PcReport-dev.cer / out\PcReport-store.cer）。
    3. -Trust を付けたときだけ、その .cer を LocalMachine\TrustedPeople（信頼されたユーザー）に入れる。
       ここだけ管理者権限が要る（管理者の PowerShell で実行する。自分では昇格しない）。
       これが無いと MSIX をインストールできない。「信頼されたルート証明機関」には入れない。

    消すときは Remove-DevCert.ps1。

.PARAMETER Identity
    Dev（既定）：identity\dev.psd1 の Publisher を Subject にする（PcReport-dev.msix 用）
    Store      ：identity\store.psd1 の Publisher を Subject にする（Build-Msix.ps1 -Target Store -Sideload 用。
                 Store の Identity のまま開発機に入れて PFN と表示名を確かめるためだけに使う）

.PARAMETER Trust
    .cer を LocalMachine\TrustedPeople に入れる（管理者として実行したときだけ指定する）。
#>
[CmdletBinding()]
param(
    [ValidateSet('Dev', 'Store')] [string]$Identity = 'Dev',
    [switch]$Trust
)

$ErrorActionPreference = 'Stop'

$here   = $PSScriptRoot
$outDir = Join-Path $here 'out'
$suffix = $Identity.ToLowerInvariant()
$cerOut = Join-Path $outDir "PcReport-$suffix.cer"
# sideload で使う Identity（identity\dev.psd1 か identity\store.psd1）の Publisher を Subject にする
$subject = (Import-PowerShellDataFile (Join-Path $here "identity\$suffix.psd1")).Publisher

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not $Trust) {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
            Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Select-Object -First 1
    if (-not $cert) {
        # MSIX の署名には「コード署名」EKU と Basic Constraints（CA ではない）が要る
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $subject `
            -KeyUsage DigitalSignature `
            -KeyExportPolicy NonExportable `
            -FriendlyName "PcReport MSIX sideload $suffix (delete after verification)" `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -NotAfter (Get-Date).AddDays(30) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host '作りました: Cert:\CurrentUser\My' -ForegroundColor Green
    }
    else {
        Write-Host '既にあります: Cert:\CurrentUser\My' -ForegroundColor Gray
    }
    Export-Certificate -Cert $cert -FilePath $cerOut -Type CERT | Out-Null
    Write-Host ("  Subject : {0}" -f $cert.Subject)
    Write-Host ("  拇印    : {0}" -f $cert.Thumbprint)
    Write-Host ("  期限    : {0:yyyy-MM-dd}" -f $cert.NotAfter)
    Write-Host ("  公開鍵  : {0}" -f $cerOut)
    return
}

# ---- -Trust：管理者として .cer を「信頼されたユーザー」に入れる ------------------

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw '-Trust は管理者として実行してください（LocalMachine のストアに書くため）。' }
if (-not (Test-Path $cerOut)) { throw "先に -Trust なしで（同じ -Identity で）実行して .cer を作ってください: $cerOut" }

$imported = Import-Certificate -FilePath $cerOut -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
Write-Host ("入れました: Cert:\LocalMachine\TrustedPeople  拇印 {0}" -f $imported.Thumbprint) -ForegroundColor Green
