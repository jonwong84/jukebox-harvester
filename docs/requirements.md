# Jukebox Harvester: Requirements (Draft v0.1)

Drafted October 9, 2026. Status: business requirements and decisions only. No design, no code. Intended home in the repo: `docs/requirements.md`. AGENTS.md should point here, not duplicate it.

Legend: **\[Decided\]** was explicitly agreed. **\[Proposed\]** was suggested and not objected to, but not explicitly confirmed. **\[Open\]** needs a decision.

## 1. Purpose

The Harvester brings music metadata from external sources into the Jukebox catalog as clean, trustworthy records, with minimal manual entry. It is a redesign, as microservices, of a predecessor monolith used in a previous job. Predecessor details stay conceptual in this document.

- **R1.** Turn external music data into canonical Jukebox records with little retyping. \[Decided\]
- **R2.** Nothing becomes canonical on a guess. Either the match is verified, or an operator decides. \[Decided\]

## 2. Scope and non-goals

**Non-goals for v1**

- No playback, recommendations, or syncing to any streaming service.
- A Plex-like streaming service is only an idea. The catalog should be a good thing to build on later (stable identifiers, a clean read boundary, no assumption that the Harvester is its only consumer), but nothing is designed for it now.
- No second data source is built. The design must make adding or replacing one cheap (see section 4).
- No multi-album spreadsheets. One file is one album.
- No separate roles (see section 3).
- No scheduled retry of unmatched records.

## 3. Users and roles

- **R3.** The system supports more than one user. \[Decided\]
- **R4.** There is one shared canonical catalog, the single source of truth for all metadata. \[Decided\]
- **R5.** There is one privileged role. Everyone who operates the Harvester (an "operator") can request extraction, join runs, review, approve, and correct. \[Decided\]
- **R6.** Every request, approval, correction, and rejection records who did it and when. This keeps the audit trail and makes a later role split a permissions change, not a redesign. Operators must therefore be authenticated. \[Decided\]
- **R7.** The catalog enforces its own write rules at its boundary: only an authenticated operator may write. The UI is not the only guard. \[Decided\]
- Listeners in a future streaming product are a different kind of user with no catalog write access. Out of scope here.
- Per-user data (who requested what, who watches which source, later listening history) lives outside the catalog and references catalog records. \[Proposed\]

## 4. Sources and extraction

- **R8.** v1 sources are MusicBrainz and operator-submitted spreadsheets. \[Decided\]
- **R9.** Spotify is excluded. Its developer access now requires Premium and restricts endpoints and quotas, which does not suit this project. A Spotify extractor could be added later behind the same contract. \[Decided\]
- **R10.** Each source is implemented behind an extraction contract (`IExtractor`). Replacing or adding a source means writing a new extractor, with no changes to the pipeline, the catalog, or the review step. \[Decided\]
- **R11.** The reference lookup ("what edition is this?") sits behind its own interface, with MusicBrainz as its only v1 implementation. Losing MusicBrainz must not require touching Transform. \[Decided\]
- **R12.** The pipeline must be runnable end to end with a fake in-memory extractor and no real source. This is the test that coupling has not crept in. \[Proposed\]
- **R13.** Prefer documented APIs or files over scraping. If scraping is ever needed, it sits behind the same contract so its fragility stays inside one extractor. \[Decided\]
- **R14.** Cover art is attached when available and never blocks a record. The Cover Art Archive is the likely source. Its current terms and limits are not yet verified. \[Proposed\]

### Triggers

- Extraction from watched MusicBrainz sources (for example, artists) runs on a schedule. \[Decided\]
- Any extraction can also be triggered on demand by an operator. \[Decided\]
- A scheduled run and a manual run are the same pipeline started by different triggers. They must not diverge. \[Decided\]
- What the scheduled job watches (artists, new releases) is \[Open\].

### Spreadsheet flow

1. An operator creates an Album record in the dashboard (Title, Artist\[\], Year, Label, Edition). These fields are a starting set, not final. \[Decided\]
2. The operator attaches a spreadsheet: one row per track, for that one album.
3. The extractor populates the album's track list from the file.
4. The record proceeds to Transform.

Rules for this flow:

- The operator-entered album fields are human-set and win over any album text in the file. Disagreements are flagged.
- The extractor maps columns to track fields. Disc and position must come from columns or a documented row-order rule.
- A bad row flags that track. The album proceeds.
- Attaching a corrected file to the same record replaces the track list. It does not append. \[Proposed\]
- An operator may supply a MusicBrainz ID. It is verified, never trusted blindly.

## 5. The album candidate (output of Extract)

- **R15.** Every extractor emits one source-neutral, versioned album candidate with its tracks inside. Transform consumes only that shape. \[Decided\]
- The candidate carries: the source's name, the source's own ID or row reference, the fields it could read, the raw payload, and any problems found.
- All fields except the claim of what it is are optional. Missing is a valid state, not an error.
- Values are claims. An identifier supplied by a source is "this source says," and Transform decides whether to trust it.
- A track carries its disc and position. Problems attach at track level and album level. One bad track never discards the album.
- Source vocabulary must not appear in the shape. No "release-group," "playlist," or "sheet." Any such word indicates leakage.
- Extract translates (parsing, pagination, authentication, column mapping, rate limits). Transform interprets (normalizing names, matching editions, detecting duplicates and conflicts). Extractors do not clean up values.
- A source that only provides tracks groups them into albums inside its own extractor. Transform never learns it was track-level.
- Raw payloads are kept, since explaining why a match was chosen requires them. \[Decided\]

## 6. Identity: editions

- **R16.** Each edition is its own catalog entity. Original, remaster, and deluxe are separate albums, and the same applies to songs. \[Decided\]
- **R17.** An edition is one record. Pressings and regional variants of the same edition collapse into it. \[Decided\]
- **R18.** The catalog generates its own identifier for every edition. External identifiers are optional attributes. An edition may have zero, one, or several MusicBrainz release IDs, with one marked representative. \[Decided\]
- **R19.** The catalog rejects a second edition that claims a MusicBrainz release ID already mapped to an existing edition. Identifiers are stored as (source, external ID) pairs so other sources can be added. \[Decided / Proposed\]
- Tracks are keyed by the track as it appears on its edition, not by recording. The recording ID is an attribute. \[Proposed\]
- The release-group ID is stored as a plain attribute, so a future grouped view needs no migration. \[Proposed\]
- An edition can enter the catalog with no MusicBrainz ID, so independent music MusicBrainz does not know is not excluded. \[Decided\]
- Records carry a match status (matched, unmatched, re-matched), separate from approval status. \[Proposed\]

## 7. Matching (Transform)

1. If an ID was supplied, fetch that release and verify it against the record.
2. Otherwise search MusicBrainz `release` by artist and title, with query special characters escaped.
3. Fetch details for the top few candidates and cache responses.
4. Score each candidate against the record and its tracks.
5. Group survivors into editions and pick a representative release.
6. Emit a proposal that lists the candidates considered, their scores, and the winner if any.

Signal strength, strongest first: track count, titles, and durations; title and artist (order-insensitive artists); year; label; edition text. Edition text is a hint or tiebreaker, never a filter.

Outcomes: clear winner (shown pre-checked for approval), ambiguous (side-by-side choice by an operator), no match.

- The representative-release rule must be explicit and documented. Its output is a candidate, not a verdict. The exact rule is \[Open\].
- Ambiguity always goes to an operator, even on bypass.

## 8. Approval and bypass

- **R20.** After transformation, every item waits for an operator by default. \[Decided\]
- **R21.** An individual extraction request may opt out of approval and go straight to the backend. \[Decided\]
- Bypass skips the approval step only. It never skips validation, duplicate checks, ambiguity, or the catalog's uniqueness rule. \[Decided\]
- Bypassed records are marked as auto-approved, with the requester recorded, so they can be found and reviewed later. \[Proposed\]
- On bypass, only ID-verified matches go through unattended. Unmatched, ambiguous, invalid, and conflicting items stop for an operator. A confidence threshold may be added later as an explicit setting. \[Proposed\]
- A bypassed run finishes by reporting how many items still need attention.
- Previously rejected items are not resurfaced as new. The catalog records "seen and declined." \[Proposed\]
- When several requests join one run, the strictest wins: if any requires approval, items wait. \[Proposed\]
- Scheduled runs require approval by default. Bypass on a watched source is an explicit setting. \[Proposed\]

## 9. Runs, joining, and fairness

- **R22.** Extraction belongs to the source. Requests belong to users. \[Decided\]
- **R23.** A request for a source that already has a run in progress joins that run. Everyone sees the same progress and result. A scheduled trigger follows the same rule. \[Decided\]
- **R24.** Cancelling withdraws only that user's request. The run stops when no requests remain. Approved items stay. \[Decided\]
- A request arriving right after a run finished returns that result instead of re-extracting. The window is a setting. Its value is \[Open\].
- No-duplicates must hold between different sources that share an album, not only between runs of the same source.
- A run has an identity and a visible status. Every request records who asked.
- MusicBrainz's limit (one request per second, per IP) is shared by all runs combined, including scheduled and manual ones. On-demand requests take priority over bulk work. The exact rule is \[Open\].
- The client sends a User-Agent with contact information.

## 10. Duplicates and bad data

- **Exact match** on an existing identifier is never an insert. It is either skip or enrich.
- **Probable match** with no identifier match goes to an operator, even on bypass. Remasters are not duplicates. The probable cases are the same edition spelled differently, and two MusicBrainz IDs for one real release.
- **Existing duplicates** need a merge tool: keep both records' identifiers and redirect anything that referenced the loser.
- **Invalid** (missing required fields, unparseable identifier, track with no album) never reaches the catalog, with or without bypass. It goes to a needs-attention list with a reason.
- **Doubtful** (low-confidence match, contradiction with existing data) goes to operator review.
- Human-set fields are never overwritten. A significant disagreement is shown as a proposed change, not applied silently.
- Unmatched editions get a fallback duplicate check on normalized artist, title, edition, and year. It warns the operator and does not silently reject. Track lists from the spreadsheet strengthen it.
- Unmatched editions can never go through on bypass.

## 11. Failure handling and scale

- **R25.** One bad item never stops the batch. \[Decided\]
- **R26.** Temporary outages are retried automatically, a limited number of times. Persistent failures surface with a reason. The limits are \[Open\].
- **R27.** Re-running a failed or interrupted run is safe: no duplicates, no lost approvals.
- Losing one source degrades a run and does not stop it.
- Scale (assumed, not confirmed): typical album 20 to 200 tracks; a first bulk import may be 1,000 to 10,000 lookups. At one request per second that is about 17 minutes and 2.8 hours. Slow is acceptable. Getting banned is not.

## 12. Refresh (second pass)

- **R28.** An operator can request a refresh of existing records. It updates in place, never creates a duplicate, and never overwrites human-set fields. \[Decided\]
- Unmatched editions are re-checked only on manual refresh, for one record or in batch. There is no scheduled retry. \[Decided\]
- A refresh that finds a likely match proposes the ID. An operator confirms it, even on bypass.
- Operators need an easy unmatched filter showing how long each record has been unmatched and when it was last checked.

## 13. Provenance and audit

- Each field remembers its supplier: a source, or a person (who and when).
- An operator's attestation on an unmatched edition is recorded as human-set.
- First decision on a review item wins and is recorded. A later change is an explicit new edit.
- The catalog must outlive its sources. Everything it holds is stored as its own data with provenance, so losing MusicBrainz does not damage existing records.

## 14. Lessons carried over from the predecessor

- The source-neutral record shape worked: every site produced one common list, and all later stages relied only on that shape.
- The extraction step was welded to one proprietary scraping tool that was later deprecated. The extraction logic could not be moved, tested, or replaced, and it died with the tool. The shape was fine. The producers were not owned.
- Every user was an admin, so the predecessor had a human checkpoint but no requester/approver split. Jukebox is not inheriting roles from it.
- Hence R10, R11, and R12: producers are code the project owns, behind a contract, testable with a fake.

## 15. Learning goals (counted as requirements)

- **L1.** Use RabbitMQ with a real decision about what lives in the database versus what travels as messages.
- **L2.** Separate services with clear owners of status.
- **L3.** Practice idempotency, retries, and a human checkpoint with a bypass.

## 16. Open questions

1. What a record's status means, and who owns it (the `StagingRecord` question). This most affects service boundaries.
2. Whether the Harvester writes through jukebox-data-manager's API or to its own store before hand-off.
3. Service boundaries, and whether each extractor is a service or a plug-in.
4. The representative-release rule.
5. The freshness window.
6. Retry limits and policy.
7. The priority and quota rule for on-demand versus bulk work.
8. What the scheduled job watches.
9. Final Album record fields beyond Title, Artist\[\], Year, Label, Edition. Whether operators can enter a MusicBrainz ID at creation.
10. Source priority per field, if a second reference source is ever added.
11. Whether one operator's correction can replace another's.
12. How the Cover Art Archive's terms and limits constrain R14.

## 17. Assumptions to challenge

- The scale numbers in section 11 were invented.
- Operators are authenticated through the Entra ID work planned for Azure.
- MusicBrainz's rate limit and identification rules are as documented at musicbrainz.org/doc/MusicBrainz\_API. Recheck before building.
