namespace Jukebox.Harvester.Transform;

public sealed record ReleaseCandidate(
    string Id,
    string Title,
    string? Status,
    string? Date,
    string? Country,
    string? Barcode,
    string VariantNote,
    string? LabelName,
    IReadOnlyList<MediumCandidate> Media);

public sealed record MediumCandidate(
    string? Format,
    IReadOnlyList<TrackCandidate> Tracks);

public sealed record TrackCandidate(
    int Position,
    string? NumberAsPrinted,
    string Title,
    int? DurationMs,
    string? RecordingId);

public enum EditionLabel { Clear, Ambiguous }

public enum AmbiguityReason { Marked, Resequenced, Bridging }

public sealed record CandidateEdition(
    IReadOnlyList<string> MemberIds,
    EditionLabel Label,
    IReadOnlyList<AmbiguityReason> Reasons,
    string RepresentativeId,
    string? EditionDate);

public sealed record SetAsideRelease(string Id, string? Status);

public sealed record GroupingResult(
    IReadOnlyList<CandidateEdition> Editions,
    IReadOnlyList<SetAsideRelease> SetAside);

public sealed record GroupingOptions(
    IReadOnlyList<string> RegionPreference,
    IReadOnlyList<string> MarkerStems,
    IReadOnlyList<string> FormatPreference)
{
    public static GroupingOptions Empty { get; } = new([], [], []);
}

public static class EditionGrouper
{
    public static GroupingResult Group(
        IReadOnlyList<ReleaseCandidate> candidates,
        GroupingOptions options)
        => new([], []);
}