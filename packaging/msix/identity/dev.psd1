# sideload（開発機で試す）用の Identity。自己署名証明書で署名してインストールするときに使う。
# Publisher は New-DevCert.ps1 が作る証明書の Subject と一字一句一致していなければならない。
# Store に出すときは使わない（identity\store.psd1 を使う）。
@{
    Name                 = 'Microforge.PcReport.Dev'
    Publisher            = 'CN=Microforge PCR-002 Dev (self-signed)'
    PublisherDisplayName = 'MicroForge'
    DisplayName          = 'PC 健康診断 (検証版)'
    PackageFamilyName    = 'Microforge.PcReport.Dev_0h149wtcvgt50'
}
