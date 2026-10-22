using Jukebox.Harvester.Transform;

namespace Jukebox.Harvester.Transform.Tests;

public static class Candidates
{
    public static ReleaseCandidate Create(
        string id = "test-release",
        string title = "Test Album",
        string? status = "Official",
        string? date = null,
        string? country = null,
        string? barcode = null,
        string variantNote = "",
        string? labelName = null,
        IReadOnlyList<MediumCandidate>? media = null)
        => new(
            id,
            title,
            status,
            date,
            country,
            barcode,
            variantNote,
            labelName,
            media ?? [new MediumCandidate("CD", [])]);
}
