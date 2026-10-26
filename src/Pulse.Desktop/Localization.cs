using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pulse.Desktop;

/// <summary>Desktop UI translations. The Simplified Chinese source text is the stable resource key.</summary>
public static class Localization
{
    private static string language = "zh-CN";
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Template>> Templates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private sealed record Template(Regex Pattern, string Translation, int Maximum);

    public static string Language
    {
        get => language;
        set => language = Normalize(value);
    }

    public static string Normalize(string? value) => value switch
    {
        "zh-Hans" or "zh-CN" or "zh" => "zh-CN",
        "zh-Hant" or "zh-TW" or "zh-HK" => "zh-Hant",
        "en" or "ja" or "ko" or "ru" => value,
        _ => "zh-CN"
    };

    public static CultureInfo Culture => Language switch
    {
        "zh-Hant" => CultureInfo.GetCultureInfo("zh-TW"),
        "en" => CultureInfo.GetCultureInfo("en-US"),
        "ja" => CultureInfo.GetCultureInfo("ja-JP"),
        "ko" => CultureInfo.GetCultureInfo("ko-KR"),
        "ru" => CultureInfo.GetCultureInfo("ru-RU"),
        _ => CultureInfo.GetCultureInfo("zh-CN")
    };

    public static string Format(string source, params object?[] args)
    {
        var translated = Translate(source);
        try { return string.Format(Culture, translated, args); }
        catch (FormatException) { return string.Format(Culture, source, args); }
    }

    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text) || Language == "zh-CN") return text;
        var catalog = GetCatalog(Language);
        if (catalog.TryGetValue(text, out var direct)) return direct;

        // Interpolated labels arrive here after the caller has filled in numbers or names.
        // Resource keys use {0}, {1}, ... so their grammar can change in each language.
        foreach (var template in GetTemplates(Language))
        {
            var found = template.Pattern.Match(text);
            if (!found.Success) continue;
            var values = new object[template.Maximum + 1];
            for (var i = 0; i < values.Length; i++) values[i] = found.Groups[$"v{i}"].Value;
            try { return string.Format(Culture, template.Translation, values); }
            catch (FormatException) { return text; }
        }

        // Provider names are user supplied, while the trailing UI action is translatable.
        foreach (var suffix in new[] { " · 设置", " 额外账号" })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal) && catalog.TryGetValue(suffix.Trim(), out var translatedSuffix))
                return text[..^suffix.Length] + (suffix[0] == ' ' ? " " : "") + translatedSuffix;
        }
        return text;
    }

    private static Dictionary<string, string> GetCatalog(string code)
    {
        lock (Gate)
        {
            if (Catalogs.TryGetValue(code, out var cached)) return cached;
            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var directory in ResourceDirectories())
            {
                var file = Path.Combine(directory, code + ".json");
                if (!File.Exists(file)) continue;
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                    if (loaded != null) catalog = loaded;
                }
                catch (JsonException) { /* A damaged resource falls back to source text. */ }
                break;
            }
            Catalogs[code] = catalog;
            return catalog;
        }
    }

    private static List<Template> GetTemplates(string code)
    {
        lock (Gate)
        {
            if (Templates.TryGetValue(code, out var cached)) return cached;
            var templates = new List<Template>();
            foreach (var (key, translation) in GetCatalog(code).OrderByDescending(entry => entry.Key.Length))
            {
                if (!key.Contains("{0}", StringComparison.Ordinal)) continue;
                var matches = Regex.Matches(key, @"\{(\d+)\}");
                if (matches.Count == 0) continue;
                var pattern = "^";
                var offset = 0;
                var maximum = 0;
                var used = new HashSet<int>();
                foreach (Match placeholder in matches)
                {
                    pattern += Regex.Escape(key[offset..placeholder.Index]);
                    var index = int.Parse(placeholder.Groups[1].Value, CultureInfo.InvariantCulture);
                    maximum = Math.Max(maximum, index);
                    pattern += used.Add(index) ? $"(?<v{index}>.+?)" : $"\\k<v{index}>";
                    offset = placeholder.Index + placeholder.Length;
                }
                pattern += Regex.Escape(key[offset..]) + "$";
                templates.Add(new Template(new Regex(pattern, RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant), translation, maximum));
            }
            Templates[code] = templates;
            return templates;
        }
    }

    private static IEnumerable<string> ResourceDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Resources", "Localizations");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            yield return Path.Combine(directory.FullName, "src", "Pulse.Desktop", "Resources", "Localizations");
            yield return Path.Combine(directory.FullName, "Resources", "Localizations");
            directory = directory.Parent;
        }
    }
}
