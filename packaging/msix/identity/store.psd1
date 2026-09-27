# Microsoft Store 提出用の Identity。★ Partner Center の値を、ここに一字一句そのまま転記する ★
#
# 転記元：Partner Center → アプリと ゲーム → （予約したアプリ）→ 製品管理 → 製品 ID（Product identity）
#   Package/Identity/Name                  → Name
#   Package/Identity/Publisher             → Publisher（"CN=" から始まる長い文字列）
#   Package/Properties/PublisherDisplayName → PublisherDisplayName
#   予約したアプリ名                        → DisplayName（スタートメニューに出る名前）
#
# <<…>> が 1 つでも残っていると、Build-Msix.ps1 -Target Store は止まる（転記漏れのまま作らないため）。
# 版（Version）はここに書かない。Directory.Build.props の Version に ".0" を付けて作る（Store は 4 桁目を 0 にする決まり）。
@{
    Name                 = '<<Partner Center: Package/Identity/Name>>'
    Publisher            = '<<Partner Center: Package/Identity/Publisher>>'
    PublisherDisplayName = '<<Partner Center: Package/Properties/PublisherDisplayName>>'
    DisplayName          = '<<Partner Center: 予約したアプリ名>>'
}
