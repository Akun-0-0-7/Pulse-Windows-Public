using System.Globalization;

namespace Pulse.Core;

public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly string[] prerelease;

    private ReleaseVersion(Version numericVersion, string displayVersion, string[] prerelease)
    {
        NumericVersion = numericVersion;
        DisplayVersion = displayVersion;
        this.prerelease = prerelease;
    }

    public Version NumericVersion { get; }
    public string DisplayVersion { get; }

    public static ReleaseVersion Parse(string value) => TryParse(value, out var version)
        ? version!
        : throw new FormatException("The release version is invalid.");

    public static bool TryParse(string? value, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(value)) return false;

        var metadataStart = value.IndexOf('+');
        var display = metadataStart < 0 ? value : value[..metadataStart];
        if (metadataStart >= 0 && !ValidIdentifiers(value[(metadataStart + 1)..], false)) return false;
        var prereleaseStart = display.IndexOf('-');
        var numeric = prereleaseStart < 0 ? display : display[..prereleaseStart];
        var identifiers = Array.Empty<string>();
        if (prereleaseStart >= 0)
        {
            var label = display[(prereleaseStart + 1)..];
            if (!ValidIdentifiers(label, true)) return false;
            identifiers = label.Split('.');
        }

        var parts = numeric.Split('.');
        if (parts.Length is not (3 or 4)) return false;
        var numbers = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.Length == 0 || part.Length > 1 && part[0] == '0' || !part.All(IsDigit) ||
                !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index])) return false;
        }
        var numericVersion = parts.Length == 3
            ? new Version(numbers[0], numbers[1], numbers[2])
            : new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        version = new ReleaseVersion(numericVersion, display, identifiers);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var first = NumericVersion;
        var second = other.NumericVersion;
        var result = first.Major.CompareTo(second.Major);
        if (result == 0) result = first.Minor.CompareTo(second.Minor);
        if (result == 0) result = first.Build.CompareTo(second.Build);
        if (result == 0) result = Math.Max(0, first.Revision).CompareTo(Math.Max(0, second.Revision));
        if (result != 0) return result;
        if (prerelease.Length == 0) return other.prerelease.Length == 0 ? 0 : 1;
        if (other.prerelease.Length == 0) return -1;

        for (var index = 0; index < Math.Min(prerelease.Length, other.prerelease.Length); index++)
        {
            var left = prerelease[index];
            var right = other.prerelease[index];
            var leftNumeric = left.All(IsDigit);
            var rightNumeric = right.All(IsDigit);
            if (leftNumeric && rightNumeric)
            {
                result = left.Length.CompareTo(right.Length);
                if (result == 0) result = string.CompareOrdinal(left, right);
            }
            else if (leftNumeric != rightNumeric) result = leftNumeric ? -1 : 1;
            else result = string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }
        return prerelease.Length.CompareTo(other.prerelease.Length);
    }

    private static bool ValidIdentifiers(string value, bool prerelease)
    {
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || identifier.Any(character => !IsDigit(character) &&
                character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not '-')) return false;
            if (prerelease && identifier.Length > 1 && identifier[0] == '0' && identifier.All(IsDigit)) return false;
        }
        return true;
    }

    private static bool IsDigit(char character) => character is >= '0' and <= '9';
}
