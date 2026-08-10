using System.Globalization;
using EasySnipLite.Core.Settings;
using EasySnipLite.Localization;

namespace EasySnipLite.Tests;

public class LocaleServiceTests
{
    [Fact]
    public void English_ResolvesToEn()
    {
        Assert.Equal("en", LocaleService.ResolveCulture(AppLanguage.English, CultureInfo.GetCultureInfo("zh-CN")).Name);
    }

    [Fact]
    public void SimplifiedChinese_ResolvesToZhHans()
    {
        Assert.Equal("zh-Hans", LocaleService.ResolveCulture(AppLanguage.SimplifiedChinese, CultureInfo.GetCultureInfo("en-US")).Name);
    }

    [Fact]
    public void TraditionalChinese_ResolvesToZhHant()
    {
        Assert.Equal("zh-Hant", LocaleService.ResolveCulture(AppLanguage.TraditionalChinese, CultureInfo.GetCultureInfo("en-US")).Name);
    }

    [Theory]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh", "zh-Hans")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-MO", "zh-Hant")]
    public void System_FollowsInstalledChinese(string installed, string expected)
    {
        var culture = LocaleService.ResolveCulture(AppLanguage.System, CultureInfo.GetCultureInfo(installed));
        Assert.Equal(expected, culture.Name);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ja-JP")]
    [InlineData("de-DE")]
    public void System_NonChineseInstalled_FallsBackToEnglish(string installed)
    {
        var culture = LocaleService.ResolveCulture(AppLanguage.System, CultureInfo.GetCultureInfo(installed));
        Assert.Equal("en", culture.Name);
    }
}
