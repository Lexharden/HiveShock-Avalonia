using System.Text.Json.Serialization;

namespace HiveShock.Updates;

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseInfo(
    string Tag,
    string Version,
    string Title,
    DateTimeOffset? PublishedAt,
    bool Prerelease,
    string Notes,
    string HtmlUrl,
    IReadOnlyList<ReleaseAsset> Assets)
{
    [JsonIgnore]
    public SemVer? Semver => SemVer.TryParse(Version);

    public ReleaseAsset? FindAsset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
}
