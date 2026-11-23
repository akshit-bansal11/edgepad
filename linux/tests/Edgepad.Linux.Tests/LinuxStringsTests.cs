using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Every language the Linux app ships has a full set of LinuxStrings: the keys the English file has, no more and no
/// fewer, each with the same {0} placeholders and the same number of access-key ampersands (none on Linux). The shared
/// CoreStrings are held to the same by the Windows tests. A string changed in English ships with all 14 translations
/// in the same change, and this is what holds the translations to it.
/// </summary>
public sealed partial class LinuxStringsTests
{
    public static TheoryData<string> Cultures
    {
        get
        {
            var all = new TheoryData<string>();
            foreach (var culture in new[] { "zh-Hans", "es", "hi", "de", "fr", "pt-BR", "ja", "ar", "ru", "ko", "it", "nl", "tr", "pl" })
            {
                all.Add(culture);
            }

            return all;
        }
    }

    [Fact]
    public void TheEnglishSetIsNotEmpty() => Assert.NotEmpty(Read(CultureInfo.InvariantCulture));

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryCultureHasExactlyTheEnglishKeys(string name)
    {
        var english = Read(CultureInfo.InvariantCulture);
        var translated = Read(CultureInfo.GetCultureInfo(name));

        Assert.Empty(english.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(english.Keys));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void EveryTranslationKeepsThePlaceholdersAndTheAccessKey(string name)
    {
        var english = Read(CultureInfo.InvariantCulture);
        var translated = Read(CultureInfo.GetCultureInfo(name));

        foreach (var (key, source) in english)
        {
            var value = translated[key];
            Assert.True(Placeholders(source).SequenceEqual(Placeholders(value)), $"{name}: {key} has different placeholders");
            Assert.True(source.Count(c => c == '&') == value.Count(c => c == '&'), $"{name}: {key} has a different number of &");
        }
    }

    [Theory]
    [InlineData("ar", FlowDirection.RightToLeft)]
    [InlineData("de", FlowDirection.LeftToRight)]
    [InlineData("ja", FlowDirection.LeftToRight)]
    public void OnlyArabicReadsRightToLeft(string name, FlowDirection expected) =>
        Assert.Equal(expected, Look.DirectionOf(CultureInfo.GetCultureInfo(name)));

    /// <summary>The set the culture itself carries, with no fall back to its parent, so a missing file is a failure.</summary>
    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        var set = LinuxStrings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        var all = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in set)
        {
            all[(string)entry.Key] = (string)entry.Value!;
        }

        return all;
    }

    private static List<string> Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
