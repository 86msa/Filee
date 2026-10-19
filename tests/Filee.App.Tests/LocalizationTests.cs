using System.Text.Json;
using System.Text.RegularExpressions;
using Filee.App.Services;

namespace Filee.App.Tests;

/// <summary>Keeps every translation in sync so no language silently falls back to English.</summary>
public partial class LocalizationTests
{
    private static string I18nDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Filee.slnx")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "Filee.App", "Assets", "i18n");
        }
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(I18nDirectory, code + ".json")))!;

    public static TheoryData<string> Languages => new() { "ar", "es", "ko", "zh-CN", "pl" };

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-IQ", "ar")]
    [InlineData("AR-SA", "ar")]
    [InlineData("ar-EG", "ar")]
    [InlineData("en-US", "en")]
    [InlineData("ko-KR", "ko")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("pl-PL", "pl")]
    [InlineData("es-ES", "es")]
    [InlineData("fa-IR", "en")]
    public void System_cultures_resolve_to_a_shipped_language(string culture, string expected) =>
        Assert.Equal(expected, LocalizationService.ResolveSystemLanguage(culture));

    [Fact]
    public void Arabic_is_registered_with_its_native_name_and_has_no_empty_translations()
    {
        Assert.Contains(("ar", "العربية"), LocalizationService.Languages);
        Assert.All(Load("ar"), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Fact]
    public void Arabic_preserves_placeholder_counts_and_file_name_tokens()
    {
        var english = Load("en");
        foreach (var (key, value) in Load("ar"))
            Assert.Equal(
                AllPlaceholderPattern().Matches(english[key]).Select(m => m.Value).Order(),
                AllPlaceholderPattern().Matches(value).Select(m => m.Value).Order());
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Load("en").Keys.ToHashSet();
        var other = Load(code).Keys.ToHashSet();

        Assert.Empty(english.Except(other));
        Assert.Empty(other.Except(english));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Format_placeholders_match_english(string code)
    {
        var english = Load("en");
        foreach (var (key, value) in Load(code))
            Assert.True(
                Placeholders(english[key]).SetEquals(Placeholders(value)),
                $"{code}:{key} placeholders differ from English");
    }

    [Fact]
    public void Every_key_used_in_xaml_exists()
    {
        var english = Load("en");
        var src = Path.GetFullPath(Path.Combine(I18nDirectory, "..", ".."));
        var missing = Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => DynamicResourceKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(k => k.Contains('.') && !k.StartsWith("Icon.", StringComparison.Ordinal))
            .Where(k => !english.ContainsKey(k))
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{[^{}]+\}")]
    private static partial Regex AllPlaceholderPattern();

    [GeneratedRegex(@"\{DynamicResource ([\w.]+)\}")]
    private static partial Regex DynamicResourceKey();
}
