using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LanguageTests
{
    [Theory]
    [InlineData("de_DE.UTF-8", "de")]
    [InlineData("zh_CN.UTF-8", "zh-Hans")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("pt_PT.UTF-8", "pt-BR")]
    [InlineData("fr_CA@euro", "fr")]
    [InlineData("ja_JP.eucJP", "ja")]
    [InlineData("C", null)]
    [InlineData("C.UTF-8", null)]
    [InlineData("POSIX", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("en_US.UTF-8", null)]
    [InlineData("xx_YY", null)]
    [InlineData("zh_TW.UTF-8", null)]
    public void TheLocaleNamesATranslatedLanguage(string? locale, string? expected) =>
        Assert.Equal(expected, Language.FromLocale(locale));

    [Fact]
    public void LcAllWinsOverLang()
    {
        var variables = new Dictionary<string, string> { ["LC_ALL"] = "fr_FR.UTF-8", ["LANG"] = "de_DE.UTF-8" };

        Assert.Equal("fr", Language.FromEnvironment(name => variables.GetValueOrDefault(name)));
    }

    [Fact]
    public void AnEmptyVariableFallsThrough()
    {
        var variables = new Dictionary<string, string> { ["LC_ALL"] = "", ["LC_MESSAGES"] = "it_IT.UTF-8", ["LANG"] = "de_DE.UTF-8" };

        Assert.Equal("it", Language.FromEnvironment(name => variables.GetValueOrDefault(name)));
    }
}
