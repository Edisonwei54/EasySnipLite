using System.Globalization;
using EasySnipLite.Core.Settings;

namespace EasySnipLite.Localization;

/// <summary>语言切换：AppLanguage → CultureInfo + 设置 CurrentUICulture。</summary>
public static class LocaleService
{
    /// <summary>纯逻辑映射（installed 注入以便测试 System 分支）。</summary>
    public static CultureInfo ResolveCulture(AppLanguage lang, CultureInfo installed)
    {
        switch (lang)
        {
            case AppLanguage.English:
                return CultureInfo.GetCultureInfo("en");
            case AppLanguage.SimplifiedChinese:
                return CultureInfo.GetCultureInfo("zh-Hans");
            case AppLanguage.TraditionalChinese:
                return CultureInfo.GetCultureInfo("zh-Hant");
            default: // System：跟随系统 UI 语言
                var name = installed.Name;
                if (name == "zh") return CultureInfo.GetCultureInfo("zh-Hans");
                if (name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                {
                    return name is "zh-TW" or "zh-HK" or "zh-MO" or "zh-Hant"
                        ? CultureInfo.GetCultureInfo("zh-Hant")
                        : CultureInfo.GetCultureInfo("zh-Hans");
                }
                return CultureInfo.GetCultureInfo("en");
        }
    }

    public static void SetLocale(AppLanguage lang)
    {
        CultureInfo.CurrentUICulture = ResolveCulture(lang, CultureInfo.InstalledUICulture);
    }
}
