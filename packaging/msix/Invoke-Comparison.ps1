<#
.SYNOPSIS
    PCR-002 AC3：同じ機・同じ権限で、素の exe 版 → MSIX 版 を続けて実行し、Compare-Reports.ps1 で比べる。

.DESCRIPTION
    - exe 版  : src/PcReport/bin/Release/PcReport.exe を直接起動する
    - MSIX 版 : Invoke-CommandInDesktopPackage で、インストール済みパッケージの文脈（Desktop AppX コンテナー）の中で
                WindowsApps 配下の PcReport.exe を起動する。スタートメニューからの起動では引数を渡せないための代替手段
    どちらも --out <OutDir>\<Tag>-<exe|msix> --no-open で実行する。
    ※ --out を渡すので、保存先の退避（exe の場所 → デスクトップ）はこの比較では通らない。そちらはスタートメニューから確かめる

    次のどれかに当たれば、比較表を作っても終了コード 1 にする（結果を一次証拠として使えないため）：
    - exe 版と MSIX 内の exe のハッシュが違う（別のバイナリどうしを比べることになる）
    - MSIX 版がパッケージのコンテナーで動いた証拠（AppModel-Runtime ID 211）が PID で見つからない
    - どちらかが時間内に終わらない
    - HTML に書かれた実行権限が、この PowerShell の権限と食い違う（Compare-Reports.ps1 が判定）

    昇格した PowerShell から実行すれば「昇格した管理者」での比較になる（-Tag で区別する）。

.PARAMETER OutDir
    結果の置き場所。AppData の下は不可（MSIX 版の書き込みがパッケージ専用の場所へ仮想化され、外から見えなくなる）。
.PARAMETER Tag
    実行条件の名前（例 user / admin）。ファイル名に使う。
.PARAMETER TimeoutSec
    1 回の実行を待つ上限（exe 版・MSIX 版で共通）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$OutDir,
    [Parameter(Mandatory)] [string]$Tag,
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'

# 相対パスのままだと、MSIX 版は起動されたプロセスの作業ディレクトリ基準で解決してしまう
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
foreach ($ad in $env:LOCALAPPDATA, $env:APPDATA) {
    if ($OutDir.StartsWith($ad.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutDir を AppData の下にしないでください（MSIX 版の書き込みが仮想化されます）: $OutDir"
    }
}

$appRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$exe     = Join-Path $appRoot 'src\PcReport\bin\Release\PcReport.exe'
[xml]$m  = Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw -Encoding UTF8
$pkgName = (Import-PowerShellDataFile (Join-Path $PSScriptRoot 'identity\dev.psd1')).Name
$pkg     = Get-AppxPackage -Name $pkgName
if (-not $pkg) { throw ('パッケージがインストールされていません: {0}' -f $pkgName) }
$appId   = $m.Package.Applications.Application.Id
$pkgExe  = Join-Path $pkg.InstallLocation 'PcReport.exe'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$runMode = if ($isAdmin) { '管理者として実行' } else { '通常の権限で実行' }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { $log.Add($s); Write-Host $s }
function Save-Log { [System.IO.File]::WriteAllLines((Join-Path $OutDir "$Tag-run.txt"), $log, (New-Object System.Text.UTF8Encoding($true))) }
$problems = New-Object System.Collections.Generic.List[string]

function New-RunDir([string]$Kind) {
    $dir = Join-Path $OutDir "$Tag-$Kind"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $dir
}

# 指定パスの PcReport.exe が現れて消えるまで待つ。見えた PID と、時間切れかどうかを返す
function Wait-PcReport([string]$Path, [datetime]$Deadline) {
    $pids = @{}
    $appeared = $false
    while ((Get-Date) -lt $Deadline) {
        $procs = @(Get-CimInstance Win32_Process -Filter "Name='PcReport.exe'" -ErrorAction SilentlyContinue |
                   Where-Object { $_.ExecutablePath -eq $Path })
        foreach ($p in $procs) { $pids[[int]$p.ProcessId] = $true; $appeared = $true }
        if ($appeared -and $procs.Count -eq 0) { return @{ Pids = @($pids.Keys); TimedOut = $false } }
        Start-Sleep -Milliseconds 100
    }
    @{ Pids = @($pids.Keys); TimedOut = $true }
}

Say ("=== PCR-002 比較実行 [{0}] {1:yyyy-MM-dd HH:mm:ss}" -f $Tag, (Get-Date))
Say ("実行者の昇格: {0}" -f $(if ($isAdmin) { '昇格した管理者' } else { '非昇格' }))
$hashA = (Get-FileHash $exe).Hash
$hashB = (Get-FileHash $pkgExe).Hash
Say ("exe SHA256        : {0}" -f $hashA)
Say ("MSIX              : {0}" -f $pkg.PackageFullName)
Say ("MSIX 内 exe SHA256: {0}" -f $hashB)
if ($hashA -ne $hashB) {
    Save-Log
    throw 'exe 版と MSIX 内の exe が別のバイナリです。Build-Msix.ps1 で作り直して入れ直してください'
}

# ---- exe 版 -------------------------------------------------------------------

$dirA   = New-RunDir 'exe'
$stderr = Join-Path $OutDir "$Tag-exe-stderr.txt"
$t0 = Get-Date
$p = Start-Process -FilePath $exe -ArgumentList @('--out', "`"$dirA`"", '--no-open') -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $OutDir "$Tag-exe-stdout.txt") -RedirectStandardError $stderr
$null = $p.Handle   # これを触っておかないと Start-Process -PassThru の ExitCode が空になる
if ($p.WaitForExit($TimeoutSec * 1000)) {
    Say ("[exe]  終了コード {0} / {1:N1} 秒 / 実体 {2}" -f $p.ExitCode, ((Get-Date) - $t0).TotalSeconds, $exe)
    if ($p.ExitCode -ne 0) { $problems.Add("exe 版の終了コードが $($p.ExitCode)") }
}
else {
    Say ("[exe]  {0} 秒以内に終わりませんでした（PID {1} は動いたまま）" -f $TimeoutSec, $p.Id)
    $problems.Add('exe 版が時間切れ')
}
$err = Get-Content $stderr -Raw -Encoding Default -ErrorAction SilentlyContinue
if ($err) { Say ("[exe]  stderr {0}" -f $err.Trim()) }
$a = Get-ChildItem $dirA -Filter 'pc-report_*.html' | Select-Object -First 1
Say ("[exe]  HTML {0}" -f $(if ($a) { $a.FullName } else { '（生成されず）' }))

# ---- MSIX 版（パッケージの文脈） -----------------------------------------------
# 終了コードはこの経路では取れない。失敗すると PcReport はコンソールを持たないので MessageBox で止まり、時間切れとして表れる

$dirB = New-RunDir 'msix'
$t0 = Get-Date
Invoke-CommandInDesktopPackage -PackageFamilyName $pkg.PackageFamilyName -AppId $appId `
    -Command $pkgExe -Args ("--out `"{0}`" --no-open" -f $dirB)
$w = Wait-PcReport $pkgExe $t0.AddSeconds($TimeoutSec)
Say ("[msix] {0:N1} 秒 / 実体 {1} / PID {2}" -f ((Get-Date) - $t0).TotalSeconds, $pkgExe, ($w.Pids -join ','))
if ($w.TimedOut) {
    $still = @(Get-CimInstance Win32_Process -Filter "Name='PcReport.exe'" -ErrorAction SilentlyContinue |
               Where-Object { $_.ExecutablePath -eq $pkgExe } | ForEach-Object ProcessId)
    Say ("[msix] {0} 秒以内に終わりませんでした（まだ動いている PID: {1}）" -f $TimeoutSec, $(if ($still) { $still -join ',' } else { 'なし＝起動を観測できず' }))
    $problems.Add('MSIX 版が時間切れ、または起動を観測できず')
}

# パッケージのコンテナーで動いた証拠（AppModel-Runtime ID 211：コンテナーにプロセスを追加）
Start-Sleep -Seconds 1
$ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-AppModel-Runtime/Admin'; StartTime = $t0.AddSeconds(-1) } -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -in 201, 211 -and $_.Message.Contains($pkg.PackageFullName) })
$contained = $false
foreach ($e in ($ev | Sort-Object TimeCreated)) {
    # GUID の数字だけの区切り（-9509- など）に誤一致しないよう、前後が英数字でもハイフンでもない PID だけを拾う
    if ($w.Pids | Where-Object { $e.Message -match "(?<![\w-])$_(?![\w-])" }) {
        Say ("[msix] AppModel-Runtime {0:HH:mm:ss} ID {1}: {2}" -f $e.TimeCreated, $e.Id, ($e.Message -replace '\s+', ' ').Trim())
        if ($e.Id -eq 211) { $contained = $true }
    }
}
if (-not $contained) {
    Say '[msix] パッケージのコンテナーで動いた証拠（ID 211）が見つかりません'
    $problems.Add('MSIX 版のコンテナー証拠なし')
}
$b = Get-ChildItem $dirB -Filter 'pc-report_*.html' | Select-Object -First 1
Say ("[msix] HTML {0}" -f $(if ($b) { $b.FullName } else { '（生成されず）' }))

# ---- 比較 ---------------------------------------------------------------------

if (-not $a -or -not $b) {
    $problems.Add('HTML が生成されなかった')
    foreach ($x in $problems) { Say ("問題: {0}" -f $x) }
    Save-Log
    exit 1
}
& (Join-Path $PSScriptRoot 'Compare-Reports.ps1') -ExeHtml $a.FullName -MsixHtml $b.FullName `
    -OutPath (Join-Path $OutDir "$Tag-compare.md") -ExpectedRunMode $runMode `
    -Title ("exe 版 と MSIX 版 の比較（{0}）" -f $(if ($isAdmin) { '昇格した管理者' } else { '非昇格' }))
$cmp = $LASTEXITCODE
if ($cmp -ne 0) { $problems.Add('比較で食い違い（取得可否または実行権限）') }

foreach ($x in $problems) { Say ("問題: {0}" -f $x) }
Say ("判定: {0}" -f $(if ($problems.Count -eq 0) { '一致（一次証拠として使える）' } else { '要確認' }))
Save-Log
if ($problems.Count -gt 0) { exit 1 }
exit 0
