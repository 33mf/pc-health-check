<#
.SYNOPSIS
    PCR-004：★仮の★アイコンを描いて Assets\ と store-listing\ に書き出す（Code が作った仮のもの。ブランドの確定版ではない）。

.DESCRIPTION
    青い角丸の地に、白いモニターと、その中にレポートの行とチェック印。
    サイズごとにベクターで描き直す（縮小しないので小さいサイズでも線がつぶれにくい）。

    書き出すもの：
      Assets\Square44x44Logo.png    44×44   タスクバー・スタートメニューの一覧   （パッケージに入る）
      Assets\Square150x150Logo.png  150×150 スタートメニューのタイル             （パッケージに入る）
      Assets\StoreLogo.png          50×50   パッケージのロゴ                     （パッケージに入る）
      store-listing\StoreLogo_300x300.png   Partner Center の「ストア ロゴ」に上げるもの（パッケージには入らない）

    確定版のアイコンができたら：同じファイル名・同じピクセル寸法の PNG で上書きし、Build-Msix.ps1 を実行し直すだけ。
    このスクリプトは、確定版に差し替えたあとは使わない（実行すると仮のアイコンで上書きされる）。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-Icon([string]$Path, [int]$Size) {
    $s = [float]$Size
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # 地：青い角丸
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 43, 95, 179))
    $g.FillPath($bg, (New-RoundedRect 0 0 $s $s ($s * 0.18)))

    $white = [System.Drawing.Color]::White
    $stroke = [Math]::Max(1.5, $s * 0.055)
    $pen = New-Object System.Drawing.Pen $white, $stroke
    $pen.LineJoin = 'Round'

    # モニター（外枠）と台
    $mx = $s * 0.17; $my = $s * 0.20; $mw = $s * 0.66; $mh = $s * 0.46
    $g.DrawPath($pen, (New-RoundedRect $mx $my $mw $mh ($s * 0.05)))
    $g.DrawLine($pen, $s * 0.50, $my + $mh, $s * 0.50, $s * 0.76)
    $g.DrawLine($pen, $s * 0.35, $s * 0.78, $s * 0.65, $s * 0.78)

    # 画面の中：レポートの行（44 以下は 1 行に省く）とチェック印
    $thin = New-Object System.Drawing.Pen $white, ([Math]::Max(1.2, $s * 0.04))
    $thin.StartCap = 'Round'; $thin.EndCap = 'Round'
    if ($Size -gt 50) {
        $g.DrawLine($thin, $s * 0.27, $s * 0.32, $s * 0.52, $s * 0.32)
        $g.DrawLine($thin, $s * 0.27, $s * 0.43, $s * 0.46, $s * 0.43)
        $g.DrawLine($thin, $s * 0.27, $s * 0.54, $s * 0.40, $s * 0.54)
    }
    else {
        $g.DrawLine($thin, $s * 0.27, $s * 0.36, $s * 0.46, $s * 0.36)
    }
    $check = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 127, 211, 166)), ([Math]::Max(1.8, $s * 0.065))
    $check.StartCap = 'Round'; $check.EndCap = 'Round'; $check.LineJoin = 'Round'
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($s * 0.55), ($s * 0.45)),
        (New-Object System.Drawing.PointF ($s * 0.62), ($s * 0.53)),
        (New-Object System.Drawing.PointF ($s * 0.75), ($s * 0.33)))
    $g.DrawLines($check, $pts)

    $dir = Split-Path $Path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $pen.Dispose(); $thin.Dispose(); $check.Dispose(); $bg.Dispose()
    Write-Host ("書き出し: {0}（{1}×{1}）" -f $Path, $Size)
}

New-Icon (Join-Path $PSScriptRoot 'Assets\Square44x44Logo.png')   44
New-Icon (Join-Path $PSScriptRoot 'Assets\Square150x150Logo.png') 150
New-Icon (Join-Path $PSScriptRoot 'Assets\StoreLogo.png')         50
New-Icon (Join-Path $PSScriptRoot 'store-listing\StoreLogo_300x300.png') 300
