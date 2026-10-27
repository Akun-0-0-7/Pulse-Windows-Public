using System.IO.Compression;
using System.Security.Cryptography;
using Pulse.Core;

namespace Pulse.Tests;

public static class ReleaseVersionTests
{
    [Check] public static void ParsesReleaseLabelsAndDropsBuildMetadata()
    {
        foreach (var (input, display, numeric) in new[]
        {
            ("0.1.6", "0.1.6", new Version(0, 1, 6)),
            ("0.1.6-preview.6+build.123", "0.1.6-preview.6", new Version(0, 1, 6)),
            ("0.1.6.4", "0.1.6.4", new Version(0, 1, 6, 4)),
            ("2147483647.0.0+build-01", "2147483647.0.0", new Version(int.MaxValue, 0, 0))
        })
        {
            Assert.True(ReleaseVersion.TryParse(input, out var parsed), input);
            Assert.Equal(display, parsed!.DisplayVersion);
            Assert.Equal(numeric, parsed.NumericVersion);
            Assert.Equal(display, ReleaseVersion.Parse(input).DisplayVersion);
        }
    }

    [Check] public static void ComparesNumericPreviewIdentifiersBeforeStable()
    {
        foreach (var (older, newer) in new[]
        {
            ("0.1.6-preview.9", "0.1.6-preview.10"),
            ("0.1.6-preview.10", "0.1.6"),
            ("0.1.6", "0.1.7-preview.1"),
            ("0.1.6.1", "0.1.6.2"),
            ("0.1.6-alpha", "0.1.6-alpha.1"),
            ("0.1.6-1", "0.1.6-alpha"),
            ("0.1.6-preview.99999999999999999999", "0.1.6-preview.100000000000000000000")
        })
        {
            var first = ReleaseVersion.Parse(older);
            var second = ReleaseVersion.Parse(newer);
            Assert.True(first.CompareTo(second) < 0, older + " < " + newer);
            Assert.True(second.CompareTo(first) > 0, newer + " > " + older);
        }
        Assert.True(ReleaseVersion.Parse("0.1.6").CompareTo(null) > 0);
    }

    [Check] public static void MetadataAndOmittedZeroRevisionDoNotOfferReinstallation()
    {
        foreach (var (installed, published) in new[]
        {
            ("0.1.6-preview.6+old", "0.1.6-preview.6+new"),
            ("0.1.6", "0.1.6.0"),
            ("0.1.6+old", "0.1.6.0+new")
        })
            Assert.Equal(0, ReleaseVersion.Parse(published).CompareTo(ReleaseVersion.Parse(installed)));
        Assert.True(ReleaseVersion.Parse("0.1.5").CompareTo(ReleaseVersion.Parse("0.1.6")) < 0);
        Assert.True(ReleaseVersion.Parse("0.1.6-preview.6").CompareTo(ReleaseVersion.Parse("0.1.6")) < 0);
    }

    [Check] public static void RejectsMalformedOverflowAndUntrustedCharacters()
    {
        foreach (var input in new string?[]
        {
            null, "", "0.1", "0.1.6.0.0", "v0.1.6", " 0.1.6", "0.1.6 ", "0.1.6\n",
            "-1.1.6", "00.1.6", "0.01.6", "0.1.06", "0.1.6.01", "2147483648.0.0",
            "0.1.2147483648", "0.1.6.2147483648", "0.1.6-", "0.1.6+", "0.1.6-preview..6",
            "0.1.6-preview.06", "0.1.6+build..123", "0.1.6+one+two", "0.1.6-preview/6",
            "0.1.6-preview\\6", "0.1.6-预览.6", "0.1.6-ａ", "0.1.6-preview_6", "0.1.6\0"
        })
        {
            Assert.True(!ReleaseVersion.TryParse(input, out var parsed), input ?? "null");
            Assert.True(parsed is null);
            Assert.Throws<FormatException>(() => ReleaseVersion.Parse(input!));
        }
    }

    [Check] public static void PreviewManifestUsesItsNumericVersion()
    {
        var manifest = UpdatePackage.ParseManifest(ManifestJson("0.1.6-preview.6+build.123", new string('A', 64)));
        Assert.Equal(new Version(0, 1, 6), manifest.Version);
        Assert.Equal("0.1.6-preview.6", manifest.ReleaseLabel);
        Assert.Equal("0.1.6-preview.6", manifest.VersionText);
    }

    [Check] public static void LegacyManifestConstructorPreservesVersionText()
    {
        var manifest = new UpdateManifest(new Version(0, 1, 6, 4), new Uri("https://updates.invalid/update.zip"), new string('A', 64));
        Assert.Equal("0.1.6.4", manifest.VersionText);
        var parsed = UpdatePackage.ParseManifest(ManifestJson("0.1.6.4", new string('A', 64)));
        Assert.Equal(new Version(0, 1, 6, 4), parsed.Version);
        Assert.Equal("0.1.6.4", parsed.VersionText);
    }

    [Check] public static async Task RejectsManifestLabelsThatAreInvalidOrDisagreeWithNumericVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "pulse-release-version-tests-" + Guid.NewGuid().ToString("N"));
        foreach (var label in new[] { "0.1.7-preview.6", "0.1.6.1", "0.1.6-preview/6", "" })
        {
            var manifest = new UpdateManifest(new Version(0, 1, 6), new Uri("https://updates.invalid/update.zip"), new string('A', 64)) { ReleaseLabel = label };
            try
            {
                await UpdatePackage.PrepareArchiveAsync(new MemoryStream(), manifest, root);
                throw new Exception("Invalid manifest release label was accepted: " + label);
            }
            catch (InvalidDataException) { }
            Assert.True(!Directory.Exists(root), "Invalid label created a staging directory");
        }
        foreach (var version in new[] { "0.1.6-preview.06", "0.1.6+bad/build", "2147483648.0.0" })
            Assert.Throws<InvalidDataException>(() => UpdatePackage.ParseManifest(ManifestJson(version, new string('A', 64))));
    }

    [Check] public static async Task PreparedUpdateRetainsPreviewVersion()
    {
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            foreach (var path in new[] { "Pulse.exe", "Pulse-cli.exe" })
            {
                using var output = new StreamWriter(zip.CreateEntry(path).Open());
                output.Write("synthetic update fixture");
            }
        }
        var bytes = archive.ToArray();
        var manifest = UpdatePackage.ParseManifest(ManifestJson("0.1.6-preview.6+build.123", Convert.ToHexString(SHA256.HashData(bytes))));
        var root = Path.Combine(Path.GetTempPath(), "pulse-release-version-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var prepared = await UpdatePackage.PrepareArchiveAsync(new MemoryStream(bytes), manifest, root);
            Assert.Equal("0.1.6-preview.6", prepared.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string ManifestJson(string version, string hash) =>
        "{\"version\":\"" + version + "\",\"url\":\"https://updates.invalid/update.zip\",\"sha256\":\"" + hash + "\"}";
}
