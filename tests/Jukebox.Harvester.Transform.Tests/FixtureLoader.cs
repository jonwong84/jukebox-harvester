using System.Text.Json;
using Jukebox.Harvester.Transform;

namespace Jukebox.Harvester.Transform.Tests;

public static class FixtureLoader
{
    public static IReadOnlyList<ReleaseCandidate> Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "musicbrainz", fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement
            .GetProperty("releases")
            .EnumerateArray()
            .Select(MapRelease)
            .ToArray();
    }

    private static ReleaseCandidate MapRelease(JsonElement release)
    {
        var media = release.TryGetProperty("media", out var mediaElement)
            ? mediaElement.EnumerateArray().Select(MapMedium).ToArray()
            : [];

        return new ReleaseCandidate(
            release.GetProperty("id").GetString()!,
            release.GetProperty("title").GetString()!,
            GetNullableString(release, "status"),
            GetNullableNonEmptyString(release, "date"),
            GetNullableString(release, "country"),
            GetNullableNonEmptyString(release, "barcode"),
            GetStringOrEmpty(release, "disambiguation"),
            GetLabelName(release),
            media);
    }

    private static MediumCandidate MapMedium(JsonElement medium)
    {
        var tracks = medium.TryGetProperty("tracks", out var tracksElement)
            ? tracksElement.EnumerateArray().Select(MapTrack).ToArray()
            : [];

        return new MediumCandidate(GetNullableString(medium, "format"), tracks);
    }

    private static TrackCandidate MapTrack(JsonElement track)
    {
        var recordingId = track.TryGetProperty("recording", out var recording)
            ? GetNullableString(recording, "id")
            : null;

        var duration = track.TryGetProperty("length", out var length) && length.ValueKind != JsonValueKind.Null
            ? (int?)length.GetInt32()
            : null;

        return new TrackCandidate(
            track.GetProperty("position").GetInt32(),
            GetNullableString(track, "number"),
            track.GetProperty("title").GetString()!,
            duration,
            recordingId);
    }

    private static string? GetLabelName(JsonElement release)
    {
        if (!release.TryGetProperty("label-info", out var labelInfo) || labelInfo.GetArrayLength() == 0)
        {
            return null;
        }

        var first = labelInfo[0];
        if (!first.TryGetProperty("label", out var label) || label.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return GetNullableString(label, "name");
    }

    private static string? GetNullableNonEmptyString(JsonElement element, string propertyName)
    {
        var value = GetNullableString(element, propertyName);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName)
        => GetNullableString(element, propertyName) ?? string.Empty;

    private static string? GetNullableString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;
}
