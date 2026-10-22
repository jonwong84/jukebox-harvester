using Jukebox.Harvester.Transform;
using Xunit;

namespace Jukebox.Harvester.Transform.Tests;

public sealed class FixtureLoaderTests
{
    [Theory]
    [InlineData("remaster-releases.trimmed.json", 27)]
    [InlineData("editions-cheek-to-cheek.trimmed.json", 16)]
    [InlineData("status-cases.trimmed.json", 6)]
    [InlineData("synthetic-bridging.json", 3)]
    public void LoadsExpectedReleaseCount(string fileName, int expectedCount)
    {
        var releases = FixtureLoader.Load(fileName);

        Assert.Equal(expectedCount, releases.Count);
    }

    [Fact]
    public void LoadsRemasterReleaseFieldsAndMediaInOrder()
    {
        var releases = FixtureLoader.Load("remaster-releases.trimmed.json");
        var release = Find(releases, "34879cc9");

        Assert.Equal("2011-04-16", release.Date);
        Assert.Equal("US", release.Country);
        Assert.Equal("Official", release.Status);
        Assert.Equal("093624979340", release.Barcode);
        Assert.Equal("Reprise Records", release.LabelName);
        Assert.Equal(2, release.Media.Count);
        Assert.Equal("12\" Vinyl", release.Media[0].Format);
        Assert.Equal("12\" Vinyl", release.Media[1].Format);
        Assert.Equal(6, release.Media[0].Tracks.Count);
        Assert.Equal(5, release.Media[1].Tracks.Count);

        release = Find(releases, "02face5e");
        Assert.Equal("2011", release.Date);
        Assert.Equal("XW", release.Country);
        Assert.Single(release.Media);
        Assert.Equal("Digital Media", release.Media[0].Format);
        Assert.Equal(11, release.Media[0].Tracks.Count);
        Assert.Equal("Rhino", release.LabelName);
    }

    [Fact]
    public void MapsRemasterOptionalAndLabelFields()
    {
        var releases = FixtureLoader.Load("remaster-releases.trimmed.json");

        Assert.Null(Find(releases, "081ea37e").Barcode);
        Assert.Null(Find(releases, "1cbf8f07").Barcode);
        Assert.Equal("075990301024", Find(releases, "1a8c4ac3").Barcode);
        Assert.Null(Find(releases, "1cee732b").Date);
        Assert.Null(Find(releases, "1cee732b").Country);
        Assert.Null(Find(releases, "5c976ee2").LabelName);
        Assert.Null(Find(releases, "df29954f").LabelName);
    }

    [Fact]
    public void MapsStatusCasesAndTracks()
    {
        var releases = FixtureLoader.Load("status-cases.trimmed.json");

        var promotion = Find(releases, "ec1ecfcc");
        Assert.Equal("[no label]", promotion.LabelName);
        Assert.Null(promotion.Barcode);
        var promotionTrack = promotion.Media[0].Tracks[0];
        Assert.Equal("A", promotionTrack.NumberAsPrinted);
        Assert.Equal("Autechre", promotionTrack.Title);
        Assert.Null(promotionTrack.DurationMs);
        Assert.Equal("3c867bc1-9222-4ca7-ab9d-5455123d460d", promotionTrack.RecordingId);

        var firstRelease = Find(releases, "a163d744");
        var firstTrack = firstRelease.Media[0].Tracks[0];
        Assert.Equal(1, firstTrack.Position);
        Assert.Equal("A1", firstTrack.NumberAsPrinted);
        Assert.Equal("Kalpol Introl", firstTrack.Title);
        Assert.Equal(198600, firstTrack.DurationMs);
        Assert.Equal("bb588c43-1ea1-46de-a4de-ac4f2ad4353b", firstTrack.RecordingId);

        var bootleg = Find(releases, "15ab569b");
        Assert.Single(bootleg.Media);
        Assert.Equal("Digital Media", bootleg.Media[0].Format);
        Assert.Empty(bootleg.Media[0].Tracks);
    }

    [Fact]
    public void MapsSyntheticBridgingReleaseOrderAndTrackValues()
    {
        var releases = FixtureLoader.Load("synthetic-bridging.json");

        Assert.Equal("synthetic", releases[0].VariantNote);
        Assert.Equal("synthetic", releases[1].VariantNote);
        Assert.Equal("synthetic", releases[2].VariantNote);
        Assert.Equal(["One", "Two", "Three"], releases[0].Media[0].Tracks.Select(track => track.Title));
        Assert.Equal(["Four", "Five", "Six"], releases[2].Media[0].Tracks.Select(track => track.Title));
        Assert.Equal(
            ["101", "102", "103"],
            releases[0].Media[0].Tracks.Select(track => track.RecordingId!.AsSpan()[(track.RecordingId!.Length - 3)..].ToString()));
        Assert.Equal(
            ["201", "202", "203"],
            releases[2].Media[0].Tracks.Select(track => track.RecordingId!.AsSpan()[(track.RecordingId!.Length - 3)..].ToString()));
    }

    [Fact]
    public void CandidatesUsesTheSpecifiedDefaults()
    {
        var candidate = Candidates.Create();

        Assert.Equal("test-release", candidate.Id);
        Assert.Equal("Test Album", candidate.Title);
        Assert.Equal("Official", candidate.Status);
        Assert.Null(candidate.Date);
        Assert.Null(candidate.Country);
        Assert.Null(candidate.Barcode);
        Assert.Equal(string.Empty, candidate.VariantNote);
        Assert.Null(candidate.LabelName);
        var medium = Assert.Single(candidate.Media);
        Assert.Equal("CD", medium.Format);
        Assert.Empty(medium.Tracks);
    }

    [Fact]
    public void CandidatesOverrideLeavesOtherDefaultsIntact()
    {
        var candidate = Candidates.Create(title: "Overridden Album");

        Assert.Equal("Overridden Album", candidate.Title);
        Assert.Equal("test-release", candidate.Id);
        Assert.Equal("Official", candidate.Status);
        Assert.Single(candidate.Media);
        Assert.Equal("CD", candidate.Media[0].Format);
        Assert.Empty(candidate.Media[0].Tracks);
    }

    private static ReleaseCandidate Find(IReadOnlyList<ReleaseCandidate> releases, string idPrefix)
        => Assert.Single(releases, release => release.Id.StartsWith(idPrefix, StringComparison.Ordinal));
}
