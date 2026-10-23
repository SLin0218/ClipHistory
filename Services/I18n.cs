using System;
using System.Globalization;

namespace SlinClip.Services;

public static class I18n
{
    public static bool IsZh =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
        CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    public static string T(string zh, string en) => IsZh ? zh : en;
}
