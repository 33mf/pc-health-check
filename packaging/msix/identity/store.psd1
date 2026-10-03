# Microsoft Store 提出用の Identity。★ Partner Center の値を一字一句そのまま転記したもの（変えるときも転記で） ★
#
# 転記元：Partner Center → アプリと ゲーム → （予約したアプリ）→ 製品管理 → 製品 ID（Product identity）
#   Package/Identity/Name                  → Name
#   Package/Identity/Publisher             → Publisher（"CN=" から始まる長い文字列）
#   Package/Properties/PublisherDisplayName → PublisherDisplayName
#   予約したアプリ名                        → DisplayName（スタートメニューに出る名前）
#   Package Family Name (PFN)              → PackageFamilyName（照合用。マニフェストには入らない）
#
# <<…>> が 1 つでも残っていると、Build-Msix.ps1 -Target Store は止まる（転記漏れのまま作らないため）。
# PackageFamilyName は Name と Publisher から計算した PFN と照合し、合わなければ止まる（Publisher の 1 文字違いを見逃さないため）。
# 版（Version）はここに書かない。Directory.Build.props の Version に ".0" を付けて作る（Store は 4 桁目を 0 にする決まり）。
#
# 2026-09-27 に予約（アプリ名「PC 健康診断」、Store ID 9ND8961QRX1X）。値の正典は specification/store/IDENTITY.md。
# どれも Store で公開される情報で、秘密ではない。
@{
    Name                 = 'MicroForge.PC'
    Publisher            = 'CN=7EE8B8FD-7E10-4A73-B4F7-5DB0938E2BAC'
    PublisherDisplayName = 'MicroForge'
    DisplayName          = 'PC 健康診断'
    PackageFamilyName    = 'MicroForge.PC_ptpbgrtq4ytwy'
}
