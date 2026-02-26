using System.Globalization;
using Umbraco.Cms.Core.Services;

namespace Storage.HealthChecks.Extensions;

/// <summary>
/// Extension methods for <see cref="ILocalizedTextService"/> that provide fallback to English
/// when a localized string is not available for the current culture.
/// </summary>
public static class LocalizedTextServiceExtensions
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// Localizes a string with fallback to English if no translation exists for the current culture.
    /// </summary>
    /// <param name="localizedTextService">The localized text service.</param>
    /// <param name="area">The area/namespace of the localization key.</param>
    /// <param name="key">The localization key.</param>
    /// <returns>The localized string, or the English fallback if not found.</returns>
    public static string LocalizeWithFallback(
        this ILocalizedTextService localizedTextService,
        string area,
        string key)
    {
        var currentCulture = Thread.CurrentThread.CurrentUICulture;
        
        // Try current culture first
        var result = localizedTextService.Localize(area, key, currentCulture);

        // If the result equals the key in bracket format, it means no translation was found
        // Umbraco returns "[key]" when a key is not found
        if (IsNotLocalized(result, key))
        {
            // Fall back to English culture
            result = localizedTextService.Localize(area, key, EnglishCulture);
        }

        return result;
    }

    /// <summary>
    /// Localizes a string with tokens and fallback to English if no translation exists for the current culture.
    /// </summary>
    /// <param name="localizedTextService">The localized text service.</param>
    /// <param name="area">The area/namespace of the localization key.</param>
    /// <param name="key">The localization key.</param>
    /// <param name="tokens">The tokens to replace in the localized string using string.Format syntax.</param>
    /// <returns>The localized string, or the English fallback if not found.</returns>
    public static string LocalizeWithFallback(
        this ILocalizedTextService localizedTextService,
        string area,
        string key,
        string[]? tokens)
    {
        var currentCulture = Thread.CurrentThread.CurrentUICulture;

        // Try current culture first
        var result = localizedTextService.Localize(area, key, currentCulture);

        // If the result equals the key in bracket format, it means no translation was found
        if (IsNotLocalized(result, key))
        {
            // Fall back to English culture
            result = localizedTextService.Localize(area, key, EnglishCulture);
        }

        // Apply string.Format tokens manually since Umbraco doesn't do this automatically
        if (tokens != null && tokens.Length > 0 && !IsNotLocalized(result, key))
        {
            try
            {
                result = string.Format(result, tokens);
            }
            catch (FormatException)
            {
                // If format fails, return the unformatted string
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if the localization result indicates that no translation was found.
    /// Umbraco returns "[key]" or "[area/key]" when a key is not found.
    /// </summary>
    private static bool IsNotLocalized(string result, string key)
    {
        if (string.IsNullOrEmpty(result))
            return true;

        // Umbraco returns the key in brackets when not found: "[key]" or "[area/key]"
        return result.StartsWith("[") && result.EndsWith("]") && result.Contains(key);
    }
}
