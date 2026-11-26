using System.Globalization;

namespace CursorPress;

internal static class UiText
{
    public static string DetectLanguage() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    public static string Language { get; set; } = DetectLanguage();
    public static string T(string ru, string en) => Language == "ru" ? ru : en;
}
