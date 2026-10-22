# MusicBrainz fixtures

Recorded MusicBrainz responses, trimmed to the fields the Transform stage reads, plus two small synthetic sets. They feed the grouping tests for the rule in `docs/requirements.md` section 9. Releases are referred to by the first 8 characters of their `id`.

## Files

| File | Releases | Origin | Recorded | Exercises |
| --- | ---: | --- | --- | --- |
| `remaster-releases.trimmed.json` | 27 | 27 of the 81 releases of Fleetwood Mac's *Rumours* release group | 2026-10-15 | Marker split and propagation (G7, MP1), flattening across media (G1), re-sequenced releases (RS1), shared barcodes across different lists, earliest-period dates (G3), album titles with parentheticals (G2), missing status, missing date, missing durations |
| `editions-cheek-to-cheek.trimmed.json` | 16 | All 16 releases of the *Cheek to Cheek* release group | 2026-10-13 | Typography folding (TN1), edition names and retailer wording ignored, one large clear group, a Target-exclusive release that differs in two tracks |
| `status-cases.trimmed.json` | 6 | 2 real Promotion releases, 2 real Bootleg releases, 2 synthetic | 2026-10-13 | Status filter (Step 1). Withdrawn and Cancelled are synthetic |
| `synthetic-bridging.json` | 3 | Hand-built | not recorded | A group with a pair that matches under neither signal (bridging) |

Each file has a `_fixture` block (sources, date, whether it is trimmed or synthetic) and a `releases` array. Dates are local (US Eastern).

## What was kept

Core fields only: `id`, `title`, `status`, `date`, `country`, `barcode`, `disambiguation`, the label name from `label-info`, and for each medium its `format` and its tracks (`position`, `number`, `title`, `length`, `recording.id`). Values are exactly as recorded. Nothing was normalized, so curly quotes, odd hyphens, and quote characters in format names (`12" Vinyl`) are real data.

- A missing or null field in the source stays missing or null here. A barcode appears as a value, as `null`, or as an empty string in the source, and all three forms are kept on purpose.
- The two real Bootleg releases have no track lists in the source, only `track-count` on the medium, so that is what they carry.
- The 27-release file is in the order of the selection list used to build it, not the order of the source pages. Cheek to Cheek is in source order.

## Things the tests need to know

- **No real bridging group, Withdrawn, or Cancelled release exists** in the recorded data. Those cases are synthetic. Synthetic releases use ids starting `00000000-0000-4000-8000-` and `disambiguation` `synthetic`.
- **Known vocabulary gap:** release `1cee732b` has the disambiguation "Atmos mix". The starting marker vocabulary does not match it, so it is treated as an ordinary member of its group. Tests record this as a known gap.
- **Label data is irregular.** One release has a null label with only a catalog number (`5c976ee2`), some have no label list, and one real Promotion release carries MusicBrainz's placeholder label `[no label]`. See open item 26 in the requirements.
- **Releases without tracks (the two Bootlegs), null `country` (5 of the 27), and year-only dates (9 of the 27)** all occur. Code that reads these fixtures must tolerate them. A null medium `format` occurs in the source recordings but in none of the releases kept here, so that case needs a synthetic test input.
- Two of the 27 releases lack a date, and many have a year-only date, which is why edition dates are chosen by the earliest-period rule.

## Rules for changing these files

- Never edit a recorded value by hand. Synthetic files are ours to edit, and `_fixture` metadata can be corrected.
- To add a release, record it again and trim it with the same field list. Keep untrimmed recordings in the git-ignored `scratch/musicbrainz/` folder, never in this directory.
- Do not add tags, genres, ratings, or annotations.

## Source and license

Core MusicBrainz data (artists, releases, release groups, recordings, labels, mediums) is released under CC0. Tags, ratings, annotations, and other supplementary data carry a different license, and none of it is kept here. MusicBrainz is credited as the source of the recorded data.