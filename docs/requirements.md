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

## 18. Scaling with more operators

Scope: more operators using the Harvester. Scaling for a future audience reading the catalog (the Plex-like idea) is out of scope here.

- **N1.** No service assumes a single instance. Services are stateless and consumers are idempotent, so running more than one of anything is safe. \[Proposed\]
- **N2.** The MusicBrainz limiter is shared across all instances, runs, and triggers combined. It is not kept inside each worker. \[Proposed\]
- **N3.** More operators raise correctness risks (races), not throughput needs. Races are handled by joining, the catalog's uniqueness rule, duplicate checks, and first-decision-wins review. \[Proposed\]
- **N4.** Throughput is fixed by MusicBrainz's limit, so the design saves calls instead of adding capacity: joining concurrent requests, caching responses, skipping unchanged sources, and preferring ID-verified matches (one call instead of about six). \[Proposed\]
- **N5.** The on-demand-over-bulk priority rule must still serve small requests promptly when several operators run bulk work at once. \[Open\]

Deferred: capacity figures, autoscaling, and load testing. They need a real operator count. The expected number of operators is \[Open\].

## 19. Pipeline scaling and cover art

Expected operators: about ten. This answers the open question in section 18. Ten is small enough that the correctness rules cover it, and large enough that several operators will often have work running at once.

Predecessor lesson: its bottlenecks were at every stage. Extraction depended on the target sites being up and on the number of lots per auction. Transform downloaded images on worker threads inside one process, with a variable number and size of images per lot. Load was simple API calls. Capacity could only grow by enlarging the one monolith, so no stage could scale alone.

- **N6.** Each pipeline stage scales independently of the others. No stage's capacity is bound to another's. Queues between stages are the intended mechanism, which also serves learning goal L1. \[Proposed\]
- **N7.** A slow or failing external dependency must not stall unrelated stages or runs. This means bounded retries, backpressure, and isolation between stages (see R25 and R26). \[Proposed\]
- **N8.** Limits belong to the resource, not to the worker. Workers can be added without breaking an external limit because the limiter is shared (see N2). \[Proposed\]
- **N9.** Fan-out inside a stage, such as downloading several images, happens as messages for separate consumers, not as threads inside one process. \[Proposed\]
- **N10.** Cover art is its own stage. It runs after the record is safe, has its own concurrency limit and failure handling, and a missing or failed image never blocks a record (R14). A later retry can fill it in. \[Proposed\]
- **N11.** Cover art is stored with bounded size and a fixed set of variants (for example thumbnail and full). One large file must not tie up a worker. The exact variants are \[Open\].
- **N12.** The catalog outlives its sources (section 13). Storing the image bytes as the catalog's own data satisfies that, and storing only a source URL does not. Whether the catalog holds the image files or only a reference to where each came from is \[Open\].

Stage bottlenecks for Jukebox, for reference:

| Stage | What limits it | Scales out? |
| --- | --- | --- |
| Spreadsheet parsing and column mapping | Local CPU and I/O | Yes |
| Scoring and duplicate checks | Local CPU and database | Yes |
| MusicBrainz lookups | Shared external limit of one request per second | No. Save calls instead (joining, caching, skipping unchanged sources, ID-verified matches). |
| Cover art download | Cover Art Archive limits (unverified) and network | Within its own limit |
| Catalog writes | Database and the uniqueness rule | Within reason |

The only way to raise MusicBrainz throughput is to stop calling the public service for bulk work, for example by hosting a local copy of its published data. This has not been verified (licensing, setup, operating cost) and is deferred. Because the reference lookup sits behind its own interface (R11), a local mirror would replace that implementation and leave the pipeline unchanged.

Independent scaling adds services, queues, and failure modes. At ten operators the value is learning and headroom, not necessity. That is a stated reason, not a hidden one.

## 20. Progress log

This section is the single place to see where the project stands. Update it at the end of each session.

**Session of October 10, 2026**

- Wrote this requirements document (sections 1 to 17).
- Added section 18 (scaling with more operators) and section 19 (pipeline scaling and cover art).
- Decisions this session: concurrent requests join the run in progress; one shared catalog; one privileged role; approval by default with a per-request bypass that never skips validation or ambiguity; each edition is its own entity; Spotify is excluded; extraction and reference lookup sit behind their own interfaces; Extract emits an album candidate with tracks inside; a spreadsheet is one album per file, attached to an operator-created album record; editions may enter the catalog without a MusicBrainz ID; unmatched records are re-checked only by manual refresh; about ten operators expected.

**Next session: start here**

1. Architecture decisions, beginning with the status question: what a record's status means and who owns it, and what stays in the database versus what travels over RabbitMQ.
2. How the Harvester hands off to the catalog (jukebox-data-manager's API or its own store).
3. Service boundaries, and whether each extractor is a service or a plug-in.
4. Then the remaining policy items in section 16 and the open items in sections 18 and 19.

**Housekeeping from earlier sessions**

- Commit the uncommitted harvester code on a `feature/` branch (check `.gitignore` and secrets first).
- Write AGENTS.md only after the design settles and the solution has a real structure, just before the first mechanical task goes to an agent. It points here and does not duplicate it.
- Unresolved and time-sensitive outside this project: the NY DOL unemployment reopen.

## 21. Architecture decisions: status, staging, and lifecycle

- **A1.** In-flight records live in the Harvester's own staging database. jukebox-data-manager receives only approved records, through its API, at the final commit step. \[Decided\]
- **A2.** Staging is its own SQL Server database, separate from the catalog. Whether the two share an instance is a deployment detail. Connection strings stay configurable. \[Decided\]
- **A3.** No cross-database access in either direction. The only path between them is data-manager's API. No foreign key crosses the boundary (`CatalogEditionId` is a plain reference). Each service has its own credentials and its own migrations. \[Proposed\]
- **A4.** One owner per piece of state, and one writer per status field. A coordinating service is the only writer of lifecycle status. Stage services report outcomes and the coordinator applies transitions. The bypass rule therefore lives in one place. \[Proposed\]
- **A5.** The database holds the truth. Messages are signals that carry IDs, not data. Review is a database state that the dashboard queries, not a message. \[Proposed\]
- **A6.** Reliability techniques: transactional outbox, idempotent consumers, and a reconciliation sweep that re-sends signals for records stuck too long. Each is also a learning exercise (L3). \[Proposed\]
- **A7.** Evidence and workflow are separate. A raw extraction is immutable and records the source, source reference, shape version, time, and payload. A staging album is mutable and holds the album fields, status fields, and tracks. A refresh adds a new raw extraction and never overwrites an old one. \[Proposed\]
- **A8.** Status is split into separate fields: lifecycle, match status, and approval mode. Cover art status belongs to the cover art stage. \[Proposed\]
- **A9.** Staging tables use optimistic concurrency (a row version) so two operators or services cannot silently overwrite each other. \[Proposed\]
- **A10.** ISRC is a track-level attribute, not an identity. The old single-record entity (Source, ExtractedAt, RawPayload, Isrc) was discarded because it modeled one raw response, and nothing had been committed. \[Decided\]

Starting entity sketch (a basis for discussion, not a final schema): `RawExtraction` (Id, StagingAlbumId, Source, SourceRef, CandidateShapeVersion, ExtractedAt, RawPayload); `StagingAlbum` (Id, Title, Artists, Year, Label, Edition, Lifecycle, Match, Approval, CatalogEditionId, CreatedBy, CreatedAt, RowVersion, Tracks, Extractions); `StagingTrack` (Id, StagingAlbumId, Disc, Position, Title, Artists, Duration, Isrc). Deliberately deferred: runs and requests, the outbox, field-level provenance, review decisions, and requester details for bypass. Open: list-valued columns (Artists) as JSON text or a child table.

**Lifecycle states (from the path walk-through).** Created, Extracting, Resolving, AwaitingReview, Approved, Committed, NeedsAttention, Rejected, Cancelled. States where a record does not rest (Extracted, Resolved, Committing) were dropped. AwaitingReview (a choice between valid options) and NeedsAttention (something broken) are separate queues.

- **A11.** After an operator's pick, the duplicate check runs again before approval is accepted. A duplicate error therefore appears at decision time, not at commit. The commit-time uniqueness rule stays as the final guard. \[Decided\]
  - An exact conflict (the chosen MusicBrainz ID is already mapped to an existing edition) rejects the pick with an error. The record stays AwaitingReview.
  - A probable duplicate (the fallback check on normalized artist, title, edition, and year) shows a warning. The operator confirms it is a distinct edition, or chooses to enrich or merge. \[Proposed\]

## 22. Use cases and test cases

Built from walking one spreadsheet album through each path. Add to this section as the design grows.

**UC-1. Normal path.** An operator creates an album record, attaches a spreadsheet, and the album reaches the catalog after approval (Created, Extracting, Resolving, AwaitingReview, Approved, Committed).

- TC-A1. Given a valid file, when extraction finishes, then tracks are stored with disc and position and a raw extraction is saved.
- TC-A2. Given a clear winner, when resolution finishes, then the record is AwaitingReview with a proposal listing candidates and scores.
- TC-A3. Given AwaitingReview, when an operator approves, then the record is Approved, and the approver and time are recorded.
- TC-A4. Given Approved, when commit succeeds, then the record is Committed and `CatalogEditionId` is set.
- TC-A5. Given a duplicate or replayed message, when it arrives, then nothing changes.

**UC-2. Bypass.** A request opts out of approval and an ID-verified match is committed unattended. It is recorded as auto-approved, with the requester.

- TC-B1. Given bypass and an ID-verified match with no conflicts, then the record skips AwaitingReview and is marked auto-approved.
- TC-B2. Given bypass and an unmatched record, then it still goes to AwaitingReview.
- TC-B3. Given bypass and any invalid track, then it does not commit.
- TC-B4. Given two joined requests where one requires approval, then the record waits for review (strictest wins).

**UC-3. Ambiguous match.** An operator picks one candidate, adds the album as unmatched, or rejects it.

- TC-C1. Given equally good candidates, then the proposal lists them side by side with no winner pre-selected.
- TC-C2. Given a pick, then Match becomes Matched with the chosen ID, and the record becomes Approved.
- TC-C3. Given "add as unmatched," then Match stays Unmatched, the operator's attestation is recorded, and the record becomes Approved.
- TC-C4. Given a rejection, then the record is Rejected and remembered, so a later run does not resurface it as new.
- TC-C5. Given two operators deciding at once, then the first decision wins and the second receives a conflict message.
- TC-C6. Given a pick whose MusicBrainz ID is already mapped to an existing edition, then the pick is rejected with a duplicate error and the record stays AwaitingReview.
- TC-C7. Given a pick that is a probable duplicate, then a warning is shown and the operator must confirm, enrich, or merge before approval.

**UC-4. Failures.** Failures surface with a reason and never lose work.

- TC-D1. Given a row with a missing title, then that track is flagged, other tracks still parse, and the album does not commit until an operator handles it.
- TC-D2. Given a MusicBrainz outage, then retries stop at the limit and the record lands in NeedsAttention with the reason.
- TC-D3. Given a re-run after failure, then there are no duplicate tracks and no lost approvals.
- TC-D4. Given data-manager is down, then Approved records are not lost and commit once it recovers.
- TC-D5. Given commit finds the edition already exists, then it counts as success only if the identity matches, otherwise the record goes to NeedsAttention.
- TC-D6. Given an operator attaches a corrected file after NeedsAttention, then the track list is replaced, not appended, and the record returns to Extracting.

**UC-5. Cancellation.** An operator cancels a request.

- TC-E1. Given a cancel during Extracting, then the record is Cancelled and a late "extraction finished" message is ignored.
- TC-E2. Given a cancel after approval, then the record still commits (approved items stay).
- TC-E3. Given joined requests where one cancels, then the run continues for the others.

**Progress log update (supersedes the "Next session" list in section 20).** Also decided this session: staging database and its separation from the catalog (section 21), lifecycle states, and re-running the duplicate check after an operator's pick. Next: the coordinator design (runs, requests, the outbox, how joining attaches to staging albums), field-level provenance and review decisions, the commit hand-off to data-manager (idempotency), then service boundaries and the open policy items in sections 16, 18, and 19.

## 23. Runs, requests, and artist extraction

**Requests and runs**

- **R29.** A run is the work of bringing in one source. A request is one operator's (or the scheduler's) ask for that work. Many requests can attach to one run. \[Decided\]
- **R30.** Two requests join only if they have the same source key: (extractor, canonical subject). For MusicBrainz in v1 the key is (MusicBrainz, artist ID), with one scope. A scope is added to the key only if a second scope appears. \[Decided / Proposed\]
- **R31.** A search is a lookup, not a run. The operator searches by name and chooses an artist, and the run is created for the chosen ID. Request text is never matched fuzzily for joining. \[Proposed\]
- **R32.** There is at most one active run per source key, enforced by a unique index in the database, not by workflow behavior. When two requests race, one insert fails and that request attaches to the winner. \[Proposed\]
- A run records its source key, status, and start and finish times. A request records the run, the operator or scheduler, the trigger (manual or scheduled), approval mode, when it was made, and when it was withdrawn if it was.
- Cancelling sets the request's withdrawn time. The run stops only when no active requests remain (R24).
- A spreadsheet album is a run with one source and one requester. Its source key is the album record itself, so it is unique by construction. \[Proposed\]
- A watched artist is a standing request for the same source key as a manual request, so scheduled and manual requests share one run. \[Decided\]

**Artist requests**

- **R33.** An operator can request an artist from MusicBrainz, meaning all of that artist's releases. \[Decided\]
- **R34.** An artist run covers only releases credited to that artist at the album level. Guest appearances and compilations where the artist appears only on some tracks are out of scope for artist runs in v1. They can be added through the spreadsheet path. This is a stated limitation, not a defect. \[Decided\]
- **R35.** Placeholder credits such as "Various Artists" cannot be requested or watched. The extractor refuses them with a reason. \[Proposed\]
- **R36.** Album-level and track-level artist credits are separate fields everywhere, including the spreadsheet and the candidate shape. For compilations, scoring and the fallback duplicate check reduce the weight of the artist signal and rely more on the track list. \[Proposed\]
- A joint album credited to two or more artists at the album level appears in each artist's run. The catalog's uniqueness rule and the duplicate check keep it to one edition.
- To verify: how the MusicBrainz API pages results for an artist, whether track-level credits are returned when browsing by artist, and the real size of large discographies.

**Candidates and grouping**

- **R37.** The MusicBrainz extractor emits one candidate per MusicBrainz release. Transform groups releases into editions using the representative-release rule. The extractor does not group. \[Decided\]
- Each release is stored as an immutable raw extraction tied to the run. `StagingAlbumId` on a raw extraction is nullable until grouping assigns it. Grouping creates the edition-level staging album, and the album lists its member releases. \[Proposed\]
- Lifecycle differs by path. A spreadsheet album starts at Created and passes through Extracting. An artist-run album does not exist until grouping, so it starts at Resolving, and during extraction the run's own status shows progress (for example, releases fetched). \[Proposed\]
- Grouping must be safe to repeat. A release arriving in a later run attaches to the existing staging or committed edition and never creates another. \[Proposed\]
- A doubtful grouping (members disagree, for example on track count) goes to review even on bypass. \[Proposed\]
- On artist runs nearly every record carries a MusicBrainz ID, so the earlier bypass rule (only ID-verified matches go through) lets most of them pass. What still needs a human is the grouping and representative-release choice. \[Proposed\]
- Artist runs can produce dozens of review items. The dashboard needs a batch action, such as approving all clear winners together. \[Proposed\]

**Join meets bypass**

- **R38.** Strictness applies from the moment a request joins. If a request requiring approval joins a run that started with bypass, albums already committed unattended stay in the catalog, marked auto-approved so they can be reviewed later. Every later album waits for review. The joiner is told how many were already committed. \[Decided\]
- Open: a request that arrives within the freshness window after a finished bypassed run, but requires approval. It returns the finished result, and what happens to its unreviewed auto-approved albums is \[Open\].

**Use cases and test cases**

**UC-6. Artist run.** An operator searches for an artist, chooses one, and the artist's releases are extracted, grouped into editions, reviewed, and committed.

- TC-F1. Given a name search with several artists of that name, then the operator must choose one before any run is created.
- TC-F2. Given an artist run with three releases of one edition, then one staging album is created listing all three.
- TC-F3. Given a later run delivering a fourth release for that edition, then it attaches to the existing album and no duplicate is created.
- TC-F4. Given a grouping whose members disagree on track count, then it goes to review even on bypass.
- TC-F5. Given an artist run, then a compilation where the artist appears only on tracks is not returned.
- TC-F6. Given a joint album credited to two artists, then it appears in both runs and produces one edition.
- TC-F7. Given a request for the "Various Artists" placeholder, then it is refused with a reason.

**UC-7. Joining.** A second request for a source already running attaches to the run.

- TC-G1. Given two requests for the same artist ID at the same moment, then there is one run with two requests.
- TC-G2. Given two requests for different artists, then there are two runs.
- TC-G3. Given two requests and one cancels, then the run stays active.
- TC-G4. Given both cancel, then the run stops and approved items stay.
- TC-G5. Given a scheduled trigger while an operator's run is active, then the trigger joins it.
- TC-G6. Given a request for the same artist within the freshness window after a finished run, then it returns that result and starts no new run.
- TC-G7. Given a bypassed run in progress with albums already committed, when a request requiring approval joins, then committed albums stay marked auto-approved, later albums wait for review, and the joiner is told the count.

**Progress log update (supersedes the lists in sections 20 and 22).** Also decided this session: the source key and join rule, the run and request model, artist requests with album-level scope, one candidate per release with grouping in Transform, and strictness from the moment of joining. Next: the coordinator and outbox mechanics, field-level provenance and review decisions, the commit hand-off to data-manager (idempotency), the representative-release rule, service boundaries, and the remaining policy items in sections 16, 18, and 19.

## 24. Service and data ownership (supersedes parts of section 21)

**Decision: each service owns its own database.** \[Decided\]

The reasons, kept honest about which are real and which are practice:

- **L2 is the real reason.** Learning goal L2 (separate services with clear owners) was chosen deliberately, and the project follows the textbook database-per-service model to learn it. On engineering merit alone, at about ten operators and one developer, a shared staging database with table ownership would have been simpler, with the same correctness. The costs accepted are sagas, an outbox in every service, no cross-service joins, and an eventually consistent dashboard.
- **SIM-1 (simulated requirement, labeled as such).** Extraction, review and audit, and cover art are owned by different teams. Each deploys and changes its schema on its own schedule, and audit data has stricter access than everything else. This exists to give the microservice patterns a constraint to be designed against. It is not a real constraint of the project.
- **Three reasons that are real.** Cover art needs blob storage, since image bytes do not belong in SQL tables (N12). An extractor must be replaceable (section 14), and a service that owns its raw data and tracks is cleaner to replace. Raw payloads are bulky evidence that may be purged sooner, while the audit trail of who decided what should be kept longer, so separate stores allow separate retention.
- **The load analysis is unchanged.** Ten or thirty operators would still fit comfortably on one database. The ceiling is MusicBrainz's shared one-request-per-second limit, which database separation does not raise. Operator count is not an argument for this decision.
- Because boundaries stay clean and ownership strict, merging two services later is easier than splitting a tangled one.

**Supersession.** Where section 21 disagrees with this section, this section wins.

- A1 and A2 ("one staging database") become: each Harvester service has its own database, and none of them is the catalog.
- A3 (no cross-database access) now applies between Harvester services as well as between the Harvester and the catalog. Services reach each other's data only through APIs or messages.
- A7 (raw extraction and staging album kept apart) still holds, but they now live in different services. A raw extraction's reference to a staging album is a plain ID with no foreign key.
- A9 (row version) applies within each service's own tables.

**Data ownership (starting map)**

| Service | Owns |
| --- | --- |
| Coordinator | Runs, requests, lifecycle status, match status, approval mode, its outbox |
| Extractor(s) | Raw extractions, parsed tracks |
| Resolver | Grouping, proposals, scores |
| Review | Operator decisions, audit of who decided |
| Cover art | Image records, image storage, cover art status |
| Catalog (jukebox-data-manager) | Canonical editions; holds no workflow status |

Whether the resolver and review are one service is open. Start coarse, since splitting too finely is a common mistake. Four or five services is the target.

**Writers and messaging**

- **A13.** Stage services write their own output data and report an outcome. The coordinator writes only lifecycle status and handles small outcome messages, so it never touches bulk data and is not a bottleneck. \[Decided\]
- **A14.** Each service writes its output and its outbox row in one local transaction. A relay publishes outbox rows to RabbitMQ with at-least-once delivery. Consumers are idempotent, checking both the record's current state and whether the message ID was already processed. \[Proposed\]
- **A15.** The coordinator is an orchestrator (a saga). It issues commands, receives outcomes, validates each transition against the current state, and compensates for failures instead of rolling back. Transition rules live in one state-machine class that is unit-tested without RabbitMQ. \[Proposed\]
- **A16.** RabbitMQ settings: durable queues, persistent messages, publisher confirms, manual acknowledgement after the database commit, prefetch limits, dead-letter queues, and retries with increasing delay up to a limit. A dead-lettered message moves the record to NeedsAttention with a reason. The retry limit is \[Open\]. \[Proposed\]
- **A17.** A periodic sweep re-sends commands for records stuck in active states (Extracting, Resolving) too long. It excludes AwaitingReview and NeedsAttention. \[Proposed\]
- **A18.** Message order is not relied on. Handlers check state, and a stale write loses the race harmlessly. \[Proposed\]
- **A19.** The outbox, relay, and inbox logic is built once as a shared, tested library. \[Proposed\]
- **A20.** A correlation ID is set when a request begins and appears on every message and log line, so one album can be followed across services. \[Proposed\]
- **A21.** Build a walking skeleton first: the coordinator and one extractor with a real message loop (outbox, inbox, relay). Add other services after that works. \[Proposed\]

**Open: how a stage gets its input.** With separate databases a consumer cannot load a record directly. The options are to call the producer's API with the message carrying only IDs, or to carry the needed data in the event (with large payloads stored elsewhere and referenced). Recommended: the producer's API, since it keeps messages small and the coupling is tolerable at this scale. \[Open\]

**Open: how the dashboard shows data held in several services.** The options are API composition (the dashboard calls several services and merges the results) or a read model (a query service built from events). Recommended: start with API composition, and add a read model only if queries prove painful. \[Open\]

## 25. Decisions: stage inputs and dashboard data

These resolve the two open items at the end of section 24.

- **A22.** A stage fetches its input from the producing service's API. Messages carry only IDs, plus whatever small outcome details the coordinator needs. Large payloads are not placed in messages. If one ever has to be, it is stored elsewhere and referenced. \[Decided\]
- **A23.** Consequence of A22: each service exposes a read API for the data it owns, including a batch get (several IDs in one call). Consumers must tolerate the producer being briefly down, by retrying with limits (A16), and not by reading the producer's database. \[Proposed\]
- **A24.** The dashboard shows data held in several services by API composition. A dashboard backend calls each owning service and combines the results into one response. A read model is deferred and is added only if composition proves painful. \[Decided\]
- **A25.** Composition rules: the coordinator owns the list of records and their state, so the list comes from it, with paging. The backend then fetches details for only the records on the current page, using batch gets. If one service is down, the screen shows what it has and marks the missing part, and does not fail entirely. \[Proposed\]
- **A26.** Filtering and sorting by something another service owns (for example, by title, which the coordinator does not own) is a known weakness of composition. The coordinator may keep a few copied display fields (title, primary artist), updated from events, to support common filters. This is a step toward a read model and is \[Open\].

## 26. Commit hand-off to jukebox-data-manager

Based on a review of data-manager's REST Swagger file (REST is the contract for this hand-off; its gRPC host is not used). The Swagger shows the API surface only. Database constraints, authentication, and endpoint behavior behind `200 OK` still need to be checked in the controller code.

**Decisions**

- **C1.** Commit is a stage like any other. The coordinator issues a commit command, a thin committer assembles the payload from the services that own the data (A22), calls data-manager, and reports an outcome. The coordinator owns the move to Committed. The committer decides nothing about readiness. Whether it is its own service or part of review is open, and the smaller option comes first. \[Proposed\]
- **C2.** One atomic endpoint in data-manager. A single request carries the edition, its artists (with external IDs), and all its tracks. The catalog does everything in one database transaction: it finds or creates each artist by external ID, creates the album and songs, and returns either the finished edition or a clear conflict. This enforces all-or-nothing and keeps identity logic in the catalog. \[Decided\]
- **C3.** The staging album's ID is the idempotency key. A retry returns the edition already created and never a second one. The catalog must also handle two requests with the same key arriving at once, with a unique constraint on the key, so the second waits or is told the first is in progress. \[Proposed\]
- **C4.** Outcome mapping: created is success. Already exists with the same key is success (idempotent). Already exists with a different identity is a conflict and the record goes to NeedsAttention with the reason (TC-D5). Rejected as invalid goes to NeedsAttention with the reason. Timeout or server error is transient: retry with limits, then NeedsAttention (TC-D4). The catalog's duplicate check is the final authority. The check at pick time (A11) is advisory. \[Proposed\]
- **C5.** Create and update are different operations. An update carries the version it read, so a concurrent change is rejected instead of overwritten. An update never overwrites a field a person set. \[Proposed\]
- **C6.** Cover art is not part of commit and never blocks it (N10). \[Decided\]
- **C7.** Albums commit one at a time, as each is approved, and not as a whole run. The committer respects backpressure from the catalog. \[Proposed\]
- **C8.** The catalog stores provenance per field: whether a value came from a source or a person, who, and when. This is data about the record, not pipeline status, so the catalog still holds no workflow status. It requires changes in data-manager. \[Proposed\]
- **C9.** Trust boundary. The catalog authenticates the committer as a service, and the payload names the approving operator, or marks the album auto-approved with its requester. The catalog trusts that account of who approved. The alternative, carrying the operator's identity token to the catalog, runs into token expiry while commands wait in queues and into scheduled work with no operator present. Whether to accept the weaker model is a security decision. \[Open\]
- **C10.** Request size. A request is expected to be small, since an album has tens of tracks. Unusual releases (very large box sets) may have hundreds, which is still small as JSON. Timeouts come from the database work and not from payload bytes. Rules: the catalog sets a documented maximum number of tracks per request and rejects larger ones as invalid, so they go to NeedsAttention; the committer uses an explicit timeout and treats a timeout as transient, which is safe because of C3; the catalog's writes are batched in one transaction. The exact limits are \[Open\] and should be set after measuring. \[Proposed\]

**Gaps found in data-manager's current API**

- No external identifiers on albums or artists, so the uniqueness rule (R19) has nothing to enforce.
- Artists are referenced by integer ID only, so artists must exist before an album can be written and need an identity of their own.
- A song has one artist ID, but tracks need a list of artists.
- No disc number, label, edition text, or ISRC.
- No idempotency key, no version on updates, and no atomic create of an album with its songs.
- Responses are documented only as `200 OK`, so conflict, invalid, and transient failures may not be distinguishable.
- No security scheme is shown, and `userId` is a client-supplied string in the request body, so the catalog cannot verify who is writing (R7).
- No field provenance and no merge operation for duplicates already in the catalog.
- To check in code: whether an unknown artist or album ID errors or silently succeeds, and whether deletes are hard deletes.

**How the work sorts.** Small additions: disc number, label, edition text, ISRC, a list of artists per song, distinguishable response codes, and a version token on updates. Moderate: external identifiers with uniqueness on albums and artists, and an idempotency key. Larger: provenance per field, authentication at the catalog boundary, and the atomic create. A Harvester album maps to data-manager's `Album` (extended to be the edition), and a track to its `Song` (a row per edition).

**Test cases**

- TC-H1. A retried commit with the same key returns the existing edition and creates no duplicate.
- TC-H2. Two commits with the same key at the same moment produce one edition.
- TC-H3. A commit conflicting with an existing edition's identity ends in NeedsAttention and nothing is written.
- TC-H4. If any track fails validation, no part of the edition is written.
- TC-H5. A request over the track limit is rejected as invalid and the record goes to NeedsAttention.
- TC-H6. A timeout is retried and stops at the limit.
- TC-H7. An update with a stale version is rejected.
- TC-H8. A refresh does not overwrite a human-set field.
- TC-H9. A bypassed album arrives marked auto-approved with its requester.
- TC-H10. An artist that already exists, by external ID, is reused and not duplicated.

## 27. data-manager code review findings (refines section 26)

Reviewed: the Albums POST action, the manager's `AddAlbumAsync`, the data access layer's `AddAsync`, and the `Album` and `Artist` entity configurations. Not yet seen: `GetUserId` and the authentication setup, the `Song` and album-artist join configuration, migrations, the PUT and DELETE paths, and the Artists and Songs controllers. Findings about those remain assumptions until checked.

**Confirmed**

- An unknown artist ID in a create returns an error naming the missing IDs. It is not silently skipped.
- `AddAsync` saves the album, its artist links, and its description with one `SaveChangesAsync`, so they commit in one transaction. The atomic endpoint (C2) can extend this: build the whole graph (artists, album, songs) and save once.
- The manager layer overwrites the request body's `UserId` with the authenticated user's ID, so a client-supplied value is ignored. Whether that ID is trustworthy depends on `GetUserId`, which has not been seen. This corrects an earlier statement that the catalog could not verify who was writing.
- Every failure, including validation failures and unexpected database errors, returns `400 BadRequest` with the error text. Success returns `201 Created`.
- No unique indexes, concurrency tokens, or soft-delete filters appear in the `Album` and `Artist` configurations. Two artists can share a name, and a retried create makes a second album.
- Deleting an album sets its songs' `AlbumId` to null, so the songs survive as orphans. The album description is removed with the album. Deleting an artist that has songs is blocked. Whether other deletes are hard deletes is unconfirmed.
- Limits in the schema: album title 200 characters, artist name 200, artist bio 2000.
- The data access layer catches every exception, including cancellation, and returns the raw exception text, which the manager passes outward.

**Consequences and required changes**

- **F1.** Transient failures currently look like invalid requests (400). The committer would send them to NeedsAttention and never retry. Add an error kind (invalid, conflict, unexpected) to the access result and to the manager response, keeping the existing `Success` flag so no current caller breaks. The controller maps them to 400, 409, and 5xx. This is what makes the outcome mapping (C4) workable. \[Proposed\]
- **F2.** Revision to C9. The catalog sets `CreatedBy` from the authenticated caller, which for the commit would be the committer's service identity and not the operator. The approving operator (or auto-approved plus the requester) must therefore travel as separate provenance data, stored apart from `CreatedBy`, and must not be erased by the overwrite behavior. Whether the catalog should trust that statement is still the open security decision in C9. \[Proposed\]
- **F3.** Schema additions needed: a unique external ID on artists; an album external-ID mapping with a unique (source, external ID) pair; an idempotency table with a unique key; a row version on albums and artists; label, edition text, disc number, ISRC, a list of artists per song, and ordered album artists. \[Proposed\]
- **F4.** Validation must check the length limits (200, 200, 2000) and report invalid before writing. Long titles occur, for example in classical music. \[Proposed\]
- **F5.** Deleting an edition currently leaves its songs behind. With each edition owning its own tracks, removing an edition should remove its songs, or a merge should reassign them. \[Open\]
- **F6.** Callers get a safe error message and the full exception goes to the logs. Cancellation propagates and is not logged as an error. Unexpected failures are logged as errors, not warnings. \[Proposed\]
- **F7.** Nothing prevents duplicates already in the catalog, so the merge operation (section 10) is still needed. \[Proposed\]

**To check next:** `GetUserId` and the authentication and authorization setup (including whether the controller or the startup code requires authorization), the `Song` and album-artist join configuration, the migrations (to see what the database actually enforces), and whether the PUT and DELETE paths overwrite or remove data.

## 28. Delivery sequencing

The data-manager changes in sections 26 and 27 are split into phases so the repo changes stay small at first and the Harvester can start before any of them. \[Decided\]

**Phase 0: no catalog changes.** Build the Harvester's walking skeleton (A21): the coordinator and one extractor, a real message loop with the outbox, inbox, and relay, the shared library (A19), and the correlation ID (A20). A stub catalog stands in for data-manager. The messaging plumbing is where the learning risk is, and it does not depend on the catalog.

**Phase 1: the first real commit.** The minimum that makes a commit safe, retryable, and traceable:

- Error kinds (invalid, conflict, unexpected) mapped to 400, 409, and 5xx (F1), and safe error messages (F6).
- External IDs with uniqueness on artists, and an album external-ID mapping with a unique (source, external ID) pair (F3).
- The idempotency key and the atomic endpoint (C2, C3).
- The new fields: label, edition text, disc number, ISRC, and a list of artists per song (F3).
- Length validation (F4).
- A simple field for the approving operator, or auto-approved plus the requester, stored apart from `CreatedBy` (F2).

**Phase 2: deferred until a feature needs it.** Row versions and the update path (refresh, R28), field-level provenance (C8), the merge operation (F7), and the edition-delete behavior (F5). Each belongs to a feature that does not exist yet.

**Working rules for the data-manager repo**

- Changes are additive, so the frontend and other consumers are not broken: a new endpoint, new columns, and an added error-kind field alongside the existing `Success` flag.
- Each phase is its own small versioned release through the existing changelog and jw-cicd-tools flow.
- Decisions are made together, and agents do the mechanical work under review, as in the working conventions.
- Define the commit contract (request and response shapes, error kinds, idempotency behavior) before either side is built. The stub catalog in Phase 0 implements the same contract, so replacing it with the real endpoint is a configuration change. \[Proposed\]

**Still open:** the trust boundary (C9), what `GetUserId` returns and how authentication is set up (`[Authorize]` is on the controller class, which rejects unauthenticated callers but admits any authenticated identity), the track and size limits (C10), and the `Song` and join-table configuration and migrations.

**Progress log update (supersedes the lists in sections 20, 22, and 23).** Also decided this session: database per service for learning goal L2 with SIM-1 as a labeled simulated requirement (section 24); stage inputs through the producer's API and dashboard data by API composition (section 25); the atomic commit endpoint and its rules (section 26); the data-manager code findings (section 27); and this phasing.

**Next:**

1. Define the commit contract: request and response shapes, error kinds, idempotency behavior.
2. Field-level provenance and review decisions.
3. The representative-release rule.
4. Resolve C9, then check `GetUserId`, the authentication setup, the `Song` and join-table configuration, and the migrations.
5. The remaining policy items in sections 16, 18, and 19 (freshness window, retry limits, on-demand priority, what the scheduled job watches).
6. Housekeeping: commit the uncommitted harvester code on a `feature/` branch (check `.gitignore` and secrets first); write AGENTS.md only after the design settles; the NY DOL unemployment reopen remains time-sensitive and unconfirmed.

## 29. Approval policy and artist resolution (supersedes R20, R21, and section 8 where they differ)

The predecessor required human vetting because its price database had to be highly accurate. That was a company requirement, not a property of harvesting, and Jukebox sets its own standard. Auto-commit is now the default, with problem records flagged for a human. This is the earlier option of approving only the uncertain items, and it replaces "every item waits for an operator by default."

**Decisions**

- **P1.** Auto-commit is the default. A record that meets every auto-commit criterion is committed without a human. Anything else goes to review. \[Decided\]
- **P2.** Auto-commit is limited to ID-verified matches. Name-based matches, even with a high score, never auto-commit. This is the strict starting point. It can be loosened later with evidence of how often it flags correct records, and loosening is far cheaper than cleaning a polluted catalog. \[Decided\]
- **P3.** A request can ask for approval ("require approval"). When it does, every item waits for an operator, including ones that would have auto-committed. The strictest request wins when requests join a run, from the moment of joining (R38 still applies, with the default flipped). \[Decided\]
- **P4.** Auto-commit criteria (all must hold): every artist is resolved by an exact external ID or an existing catalog ID, never by name alone; the edition is verified by ID, or built from a single release; there are no duplicate warnings, conflicts, or invalid tracks; if several releases were grouped into one edition, their track lists agree; and the request did not ask for approval. A spreadsheet album qualifies only if its supplied ID is verified and its artists resolve by ID. \[Proposed\]
- **P5.** An auto-committed record is marked as auto-committed, with the requester recorded, and `approvedBy` is empty. This is what makes after-the-fact spot checks possible. \[Proposed\]
- **P6.** Scheduled runs follow the same default. A watched source may be set to require approval. \[Proposed, to confirm\]
- **P7.** Because wrong data can now enter unattended, a way to undo it moves up in priority. Before auto-commit is enabled on scheduled runs, there must be a basic way to find auto-committed records (P5, plus a "recently auto-committed" view), and a basic way to merge or remove a wrong edition (F5, F7). Field-level provenance (C8) can stay deferred. \[Proposed\]
- Learning goal L3 (a human checkpoint with a bypass) is still met. The checkpoint is now the exception path.

**Artist resolution** Artnet's parser searched the artist field in a search index and returned the top-scoring match as one unique SortName, which was written into the transformed data. The vetter saw only that one value and no alternatives, so a plausible wrong match was caught only if the vetter knew it was wrong. Jukebox keeps the search-then-score-then-human shape and fixes that.

- **P8.** Each artist in a commit request carries a resolved identity in one of three forms: `existingArtistId` (confirmed), `externalId` (the catalog finds or creates by that ID), or `createNew` with a name (explicitly chosen by an operator). The catalog never matches by name on its own and never guesses. \[Decided\]
- **P9.** Artist identity is the catalog's own `ArtistId`. No unique SortName is required. Names need not be unique, since two real artists can share one. \[Decided\]
- **P10.** The review screen shows the top few catalog artists with their scores, pre-selects a clear winner, and lets the operator pick another or choose "new artist." A weak top score or a close runner-up means ambiguous, with nothing pre-selected. A short disambiguation text is kept as a display field so same-name artists can be told apart. \[Proposed\]
- Matching by name uses a normalized name column and a contains or prefix match to produce candidates. A real search engine is deferred until the simple version proves too weak. \[Open\]

**Commit contract changes (refines section 26)**

- `approval.mode` stays `Operator` or `Auto`. For `Auto`, `approvedBy` is empty and `requestedBy` is required. For `Operator`, `approvedBy` is required.
- Each artist entry uses one of the three forms in P8. The catalog rejects an entry with none.
- The request still carries data and no decisions. Whether a record may auto-commit is decided before the request, by the coordinator, and never by the catalog.

**Test cases (replace TC-B1 to TC-B4, and TC-G7 is reframed by TC-B5)**

- TC-B1. Given the default mode and every auto-commit criterion met, then the record commits without review and is marked auto-committed with its requester.
- TC-B2. Given any artist resolved by name only, then the record goes to AwaitingReview.
- TC-B3. Given an unmatched record, then it goes to AwaitingReview.
- TC-B4. Given any invalid track, then the record does not commit.
- TC-B5. Given a run that has already auto-committed some albums, when a request requiring approval joins, then committed albums stay marked auto-committed, later albums wait for review, and the joiner is told the count.
- TC-B6. Given grouped releases whose track lists disagree, then the record goes to review.
- TC-B7. Given a request that asks for approval, then every item waits, including ones that meet all auto-commit criteria.
- TC-B8. Given auto-committed records, then the "recently auto-committed" view lists them with their requesters.
- TC-B9. Given an artist entry with none of the three identity forms, then the catalog rejects the commit as invalid.

**Vetting dashboard** The predecessor's desktop client had a grid for editing data, but users preferred Excel and Google Sheets. A robust web dashboard is a worthwhile project and expands the Angular frontend. It is also the composition layer from A24.

- It needs: separate queues for AwaitingReview and NeedsAttention; candidates and scores for artists and editions, with a winner pre-selected only when it is clear; editing album fields and tracks; picking or overriding matches; duplicate warnings at decision time (A11); the raw source data; approve, reject, and correct, singly or in batches; first-decision-wins with a clear message for the second operator; and the "recently auto-committed" view (P5, P7).
- A spreadsheet-like grid with inline editing and clipboard paste is the target. Which spreadsheet qualities users actually valued is \[Open\]. The grid component choice, with its current licensing and features checked first, is \[Open\].
- A spreadsheet round trip (export for review, import afterward) is a non-goal for v1, because edits made outside the system bypass the audit trail and the concurrency rules.
- With a smaller review queue, the batch approval action matters less than it did under approval-by-default.

**Progress log update (supersedes the "Next" list in section 28).** Also decided this session: auto-commit as the default limited to ID-verified matches, a per-request require-approval option, artist resolution without name guessing by the catalog, and the web dashboard as a project. Next: update the commit contract with P8 and the approval rules and write it out in full; design the vetting dashboard; the representative-release rule; resolve C9; check `GetUserId`, the authentication setup, the `Song` and join configuration, and the migrations; the remaining policy items; and the housekeeping items.

## 30. Artist collaborations and credits

The predecessor modeled a collaboration as an artist entity flagged as a collaboration, with references to its member artists, so a work always pointed at exactly one artist. That fit its single-value lookup and its small, stable set of collaborations. For music it would create an entity for every ad hoc combination ("A feat. B," "A & C"), would force a person to create each new combination at review (defeating auto-commit, since a new combination has no ID), and cannot say who is the headliner and who is a guest. A list of artists avoids all three problems.

**Decisions**

- **CR1.** For v1, every album and every track carries an ordered list of artists. Each entry has a position and a role (primary or featured), plus the credit as printed (the text shown on the release) kept as a display value. \[Decided\]
- **CR2.** Collaboration entities are deferred. Adding them later is an additive change. The intended rule when they come: a collaboration is an entity only when it has an identity of its own (a duo, a band, a long-running project), and everything else stays a list. An operator decides that, and Transform never infers it. \[Decided\]
- **CR3.** Other roles (remixer, producer, composer) and relationships between a group and its members are non-goals for v1. A group is just an artist. \[Proposed\]
- **CR4.** Transform never splits a credit string on its own, because "&" and "," are ambiguous (a band name versus two artists). Either the operator enters the artists as a list, or the whole string goes through artist resolution as one candidate with the split offered as an alternative. \[Proposed\]
- **CR5.** A "(feat. X)" inside a track title is left as given. Moving the guest into the artist list is interpretation and goes through review. \[Proposed\]
- **CR6.** Album-level and track-level credits stay separate (R36). Artist sets are compared order-insensitively for scoring and duplicate checks, and the artist signal is weighted less when there are many artists. \[Proposed\]
- **CR7.** Auto-commit still requires every artist in the list to be resolved by an ID (P4). A collaboration with one unresolved guest goes to review. \[Decided\]

**Changes this requires**

- Commit contract: each artist entry carries `position` and `role`, and the edition and each track carry the credit as printed. (The earlier draft had position only.)
- data-manager Phase 1: ordered album artists with a role (the current join rows keep neither order nor role), a per-song artist list with position and role (a song has one artist ID today), and a field for the credit as printed. The song artist list was already on the Phase 1 list.
- Staging: tracks and albums keep the same ordered list, with role.
- To verify: how MusicBrainz delivers a credit (believed to be an ordered artist list with join phrases between them). This has not been checked.

**Test cases**

- TC-I1. Given "A feat. B," then A is primary at position 1, B is featured at position 2, and the credit as printed is kept.
- TC-I2. Given the same pair in a different order, then scoring and the duplicate check treat the artist sets as equal.
- TC-I3. Given a credit string like "Earth, Wind & Fire," then Transform does not split it, and the operator sees the whole string as one candidate with the split offered as an alternative.
- TC-I4. Given a new combination of artists that are all already in the catalog, then no artist is created, and the record can auto-commit if every other criterion holds.
- TC-I5. Given one unresolved featured artist, then the record goes to review.
- TC-I6. Given a track title containing "(feat. X)" and no featured artist in the list, then the title is unchanged and the mismatch is flagged for review.

## 31. Commit contract v1 (draft)

This is the contract both the Phase 0 stub catalog and the Phase 1 data-manager endpoint implement. It folds in sections 26, 27, 29, and 30. It carries data and no decisions: whether a record may auto-commit is decided by the coordinator before the request is sent, never by the catalog. Items marked \[Proposed\] are drafting choices not yet confirmed.

**Operation.** `POST /api/Albums/commit`. The existing `POST /api/Albums` is unchanged. The endpoint requires authentication (`[Authorize]`). Which policy or role the committer must hold is part of the open trust-boundary decision (C9).

**Request**

```json
{
  "contractVersion": 1,
  "commitId": "6f1c0b52-...",
  "correlationId": "b8e1-...",
  "approval": {
    "mode": "Operator",
    "approvedBy": "operator-id",
    "requestedBy": "operator-id",
    "approvedAt": "2026-10-12T14:03:00Z"
  },
  "edition": {
    "title": "Abbey Road",
    "releaseDate": "2019-09-27",
    "isCompilation": false,
    "label": "Apple Records",
    "editionText": "50th Anniversary",
    "description": null,
    "creditAsPrinted": "The Beatles",
    "artists": [
      { "position": 1, "role": "Primary", "name": "The Beatles",
        "identity": { "kind": "External", "source": "MusicBrainz", "id": "..." } }
    ],
    "externalIds": [
      { "source": "MusicBrainz", "kind": "release", "id": "...", "isRepresentative": true },
      { "source": "MusicBrainz", "kind": "release-group", "id": "...", "isRepresentative": false }
    ],
    "tracks": [
      { "disc": 1, "position": 1, "title": "Come Together", "duration": "00:04:19",
        "isrc": null, "creditAsPrinted": "The Beatles",
        "artists": [ { "position": 1, "role": "Primary", "name": "The Beatles",
                       "identity": { "kind": "Existing", "artistId": 42 } } ] }
    ]
  }
}
```

- `commitId` is the staging album's ID and is the idempotency key. `correlationId` is for tracing and is logged. Neither affects the content hash, nor does `approvedAt`. \[Proposed\]
- `approval.mode` is `Operator` (requires `approvedBy`) or `Auto` (`approvedBy` empty, `requestedBy` required).
- An artist `identity` takes exactly one form: `Existing` (an `artistId` the catalog must already hold), `External` (`source` and `id`; the catalog finds the artist by that pair or creates it using `name`), or `CreateNew` (an explicit operator choice; `name` required). A missing or unrecognized form is invalid. The catalog never matches by name.
- A reused external artist is not renamed. Human-set fields are never overwritten.
- `role` is `Primary` or `Featured`. Positions are 1-based and unique within a list. The same artist cannot appear twice in one list.
- Edition external IDs: a `release` ID may belong to only one edition. A `release-group` ID is an attribute and may be shared by several editions. Zero external IDs is valid (an unmatched edition).
- Tracks are identified by disc and position within the edition. A recording ID, if any, is an attribute and not part of identity. ISRC is an attribute and not unique.
- Release dates may be partial in sources (a year only). How to carry them is \[Open\]: the current column is a full date and would store a year as an invented day.

**Success response**

- `201 Created` with `outcome: "Created"`, or `200 OK` with `outcome: "AlreadyCommitted"` for a replay with identical content.
- Body: `albumId`, `trackIds` (each with disc, position, and `songId`), and `resolvedArtists` (each with the artist's identity as sent, its `artistId`, and whether it was created by this commit).

**Error response.** A standard problem-details body plus `kind`, a stable machine-readable `code`, a safe `message`, `errors` (a list of `path`, `code`, `message`), and where relevant `conflictingAlbumId` and `retryAfterSeconds`. The committer branches on `kind` and `code` and never on message text. Full exceptions go only to the catalog's logs.

| kind | HTTP | codes (initial set) | Committer outcome |
| --- | --- | --- | --- |
| Invalid | 400 | MissingTitle, TitleTooLong, ArtistNameTooLong, NoTracks, TooManyTracks, InvalidTrackPosition, DuplicateTrackPosition, ArtistIdentityMissing, ArtistNotFound, DuplicateArtistInList, InvalidExternalId, InvalidApproval, UnsupportedContractVersion | NeedsAttention with the reason |
| Conflict | 409 | ExternalIdAlreadyMapped (with `conflictingAlbumId`), IdempotencyKeyReused (same key, different content) | NeedsAttention |
| InProgress | 409 + Retry-After | CommitInProgress | Retry |
| Unavailable | 503 or 429 + Retry-After | ServiceUnavailable | Retry with limits |
| Unexpected | 500 | Unexpected | Retry up to the limit, then NeedsAttention |
| (no kind) | 401 or 403 |  | NeedsAttention and alert |

The same `kind` field is added to the result types of the existing endpoints (F1), keeping the `Success` flag, so every endpoint benefits.

**Idempotency**

1. The catalog stores each `commitId` with a hash of the canonical content and the resulting album ID, in the same transaction as the album. Records are kept permanently.
2. A replay with the same ID and hash returns the original result (200, `AlreadyCommitted`).
3. The same ID with a different hash is `IdempotencyKeyReused` and is never re-applied.
4. While a first request holds the key, a second gets `CommitInProgress`. The unique constraint on `commitId` is what makes two simultaneous requests safe. On a violation the loser re-reads: committed means replay, otherwise in progress.
5. Two commits that race to create the same external artist are protected by a unique index on (source, external ID). The loser re-reads the artist the winner created and reuses it.

**Atomicity.** One transaction creates or reuses the artists, the album, its artist links, description, external IDs, the songs and their artist links, and the idempotency record. Nothing is written unless all of it succeeds.

**Validation limits.** Title and artist name at most 200 characters. At least one track. Disc and position at least 1, unique as a pair. Every track has a title. A maximum track count, a description length limit, and a duration rule are \[Open\] and set after measuring real albums. Over-limit requests are invalid, not truncated.

**Supporting reads** (needed before any commit, for A11 and the review screens)

- `GET /api/Albums/by-external-id?source=&kind=&id=`
- `GET /api/Artists/by-external-id?source=&id=`
- `GET /api/Artists/candidates?name=` returns the top few artists with a score and a short disambiguation text (P10). Starting with a normalized name and a contains or prefix match. \[Proposed\]
- `GET /api/Albums/possible-duplicates` for the fallback check on normalized artist, title, edition, and year. Advisory only: the catalog rejects exact external-ID conflicts at commit, and probable duplicates are handled by review. \[Proposed\]
- `GET /api/Commits/{commitId}` returns the stored outcome or 404, so the committer can check what happened after a timeout instead of guessing. \[Proposed\]

**Versioning.** `contractVersion` is an integer. The catalog rejects an unsupported version as invalid. Adding optional fields or codes does not bump it.

**Mapping to data-manager (starting point)**

- `Album` becomes the edition: add label, edition text, credit as printed, approval mode, approved by, requested by, approved at, and the commit ID.
- An album external-ID table with a unique (source, kind, ID) pair for kind `release` only.
- Album artists gain position and role. Songs gain disc, ISRC, credit as printed, and a unique (album, disc, position). A song artist table with position and role replaces the single artist ID.
- An artist external-ID table with a unique (source, ID) pair.
- A commit record table with a unique commit ID, a content hash, and the album ID.

**Outside this contract:** cover art (N10), updates and refresh (R28), merge (F7), and field-level provenance (C8). All are Phase 2.

**The stub catalog (Phase 0).** An in-memory implementation of the same contract, including idempotency, conflicts, and artist reuse by external ID. It has a test-only way to force each outcome (invalid, conflict, in progress, unavailable, unexpected) so the coordinator's retry and NeedsAttention paths can be exercised.

**Contract tests.** One shared test suite, run against both the stub and the real endpoint. It includes TC-H1 to TC-H10, TC-B9, and TC-I1 to TC-I6, plus: a replay returns the same IDs; a changed payload under the same key conflicts; two simultaneous identical commits create one edition; two simultaneous commits sharing a new external artist create one artist; an unsupported version is rejected; and a reused external artist keeps its existing name.

## 32. Release dates and precision (resolves an open item in section 31)

**Decision: store the release date with a precision.** \[Decided\] Sources often know only a year, or a year and month. A year stored as an invented full date would be wrong data that looks right.

- **RD1.** The contract carries `releaseDate` as text in one of three forms: `YYYY`, `YYYY-MM`, or `YYYY-MM-DD`. Anything else is invalid with the code `InvalidReleaseDate`. \[Decided\]
- **RD2.** The catalog stores a date and a precision (Year, Month, or Day) in two columns. For a partial date, the date holds the first day of the period and the precision says how much of it is real, so sorting still works and nobody mistakes the first of January for a real day. The responses and any display show only what the precision supports. \[Proposed\]
- **RD3.** Matching and duplicate checks compare dates at the lower of the two precisions. A year-only date matches any date in that year. They never treat a stored first-of-the-period date as a real day. \[Proposed\]
- **RD4.** A spreadsheet, or the operator who creates the album record, may enter a year only. \[Proposed\]
- **RD5.** The existing `ReleaseDate` column is kept. Existing rows are treated as Day precision unless a migration says otherwise, and the migration is part of Phase 1. Whether existing rows were ever year-only values is unknown and should be checked. \[Open\]
- A release date is optional. Missing is valid and has no precision.

**Contract changes.** Section 31's `releaseDate` is as described in RD1, and the open note in section 31 about partial dates is closed by this section. Add `InvalidReleaseDate` to the Invalid error codes. Responses and the supporting reads return the date text as stored, in the same three forms.

**Test cases**

- TC-J1. Given `1969`, then the catalog stores the first day of 1969 with Year precision, and a read returns `1969`.
- TC-J2. Given `1969-09`, then it stores Month precision and a read returns `1969-09`.
- TC-J3. Given `1969-09-26`, then it stores Day precision.
- TC-J4. Given `1969-13` or `September 1969`, then the request is invalid with `InvalidReleaseDate`.
- TC-J5. Given an incoming year-only date and a stored day-precision date in the same year, then the dates are treated as consistent in matching and duplicate checks.
- TC-J6. Given an incoming date in a different year, then the dates conflict.

**Progress log update (supersedes the "Next" list in section 29).** Also decided this session: artist credits as ordered lists with roles (section 30), the full commit contract draft (section 31), and release dates stored with a precision (this section). Next: the vetting dashboard; the representative-release rule; resolving C9; checking `GetUserId`, the authentication setup, the `Song` and join configuration, and the migrations (including whether existing release dates were year-only); the remaining policy numbers (freshness window, retry limits, priority, track limit); and the housekeeping items (commit the harvester code on a `feature/` branch, AGENTS.md later, the NY DOL reopen).

## 33. Representative-release rule (draft)

The main piece of Transform logic for MusicBrainz data. It has two separate jobs: **grouping** (which releases are the same edition) and **representing** (which one release stands for the group). Auto-commit depends on it (P4). RD5 is closed: the database is empty, so the date columns are added as new and nothing needs migrating.

**Premises, from memory and not verified.** A MusicBrainz release has a status (Official, Promotion, Bootleg, Pseudo-Release), a country, a date, a label, a barcode, a format per medium (CD, Vinyl, Digital Media), a disambiguation comment, and a track list. Remasters are believed to usually share a release group with the original, so the release group cannot separate editions. Each of these must be checked against real responses before the rule relies on it.

**Decision.** \[Decided\] Releases with the same track list but different edition markers (for example an original and a "2009 Remaster") go to an operator, even when the title matches. This is the starting rule. It can be loosened once real data shows how reliable the markers are. It is the safest choice and will flood the queue at first for discographies with many remasters.

**Steps** \[Proposed\]

1. **Filter.** Bootlegs and pseudo-releases are set aside, and the proposal says so. Promotional releases stay but rank low. Filtering by status is interpretation and happens in Transform, not in the extractor.
2. **Group into editions.** Two releases are the same edition when all hold: the same normalized title with edition markers stripped (remaster, deluxe, anniversary, expanded, mono, stereo, and so on); the same set of edition markers, taken from the title and the disambiguation text; and the same track list (same count, same normalized titles in order, durations within a tolerance).
3. **Classify the group.** Clear: members agree on all three tests. Ambiguous: the same title with different track lists, or the same track list with different edition markers, or a group that fits the record poorly. Only clear groups can auto-commit. P4's requirement that grouped track lists agree is extended to edition markers.
4. **Choose the representative**, deterministically, in this order of preference: Official over Promotion; complete data (a date, a label, a duration on every track); format (Digital Media, then CD, then others); region (worldwide, then a configured preference list); earliest date; and the lowest release ID only to break an exact tie.

**The edition's data.** The representative supplies the title, label, and track list. The edition's date is the earliest date among members, at that member's precision (section 32). All member release IDs are stored, with the representative flagged, and the release-group ID is stored as an attribute.

**Evidence.** The proposal records every candidate, how it was grouped, and why each ranked where it did.

**Spreadsheet path.** The operator's record already defines the edition, so the question is which releases correspond to it. Each candidate is scored against the record's track list, those that agree are kept, and Step 4 picks the representative. If the best candidates disagree with the record's track list, the result is ambiguous.

**Configuration, not code.** The duration tolerance, the edition-marker vocabulary, and the format and region preference lists are parameters. Their defaults are \[Open\] and should be set from real data.

**Fixtures.** The rule is tested against a handful of recorded MusicBrainz responses (an album with a remaster, an album with a regional variant, a single-release album), so tests do not use the live service or its rate limit. Recording the fixtures is the first step.

**Test cases**

- TC-K1. Given three releases of one edition (a CD, a digital release, and a regional release) with the same track list, then they form one clear group and the representative is chosen by Step 4.
- TC-K2. Given an original and a "Remastered" release with the same track list, then the result is ambiguous and goes to an operator.
- TC-K3. Given two releases with the same title and different track lists, then the result is ambiguous.
- TC-K4. Given a bootleg among the releases, then it is set aside and the proposal says so.
- TC-K5. Given the same input twice, then the same representative is chosen.
- TC-K6. Given a tie on every preference, then the lowest release ID wins.
- TC-K7. Given a group whose members agree, then the edition's date is the earliest member date at that member's precision.
- TC-K8. Given a spreadsheet record whose track list differs from all candidates, then the result is ambiguous.

## 34. MusicBrainz API facts (checked October 12, 2026 against musicbrainz.org/doc/MusicBrainz\_API)

These replace several premises in section 33 that were written from memory. Only the main API page was read. The Search page, the Rate Limiting page, and the response shapes themselves have not been checked, and the fixtures will check the shapes.

**Verified**

- **Format and access.** JSON is returned with `fmt=json` or an `Accept: application/json` header. No API key is needed, but a meaningful User-Agent string is required. Non-commercial use is free, and commercial use needs one of MetaBrainz's plans. A commercial Plex-like product would need a license.
- **Rate limit.** No more than one call per second per client application. Exceeding it can get the IP address blocked. This matches N2 and N8.
- **Three request types.** Lookup (`/ws/2/<entity>/<MBID>?inc=`), browse (`/ws/2/<entity>?<linked-entity>=<MBID>&limit=&offset=&inc=`), and search (`/ws/2/<entity>?query=&limit=&offset=`). Only a search works without an MBID, so a name must be searched first and a result chosen.
- **Artist runs use browse by artist.** `GET /ws/2/release?artist=<MBID>` returns releases credited to that artist at the release level. A separate `track_artist` browse returns releases where the artist appears only in a track credit, and is the source of the guest-appearance and compilation cases that R34 excludes. So R34's album-level scope maps directly to the `artist=` browse.
- **Browse paging.** The default `limit` is 25 and the maximum is 100. For releases, a page holds at most about 500 tracks in total (at least one full release is always returned), so a page may hold fewer than `limit` releases. The next `offset` must be the previous offset plus the number of releases actually returned, not the `limit`.
- **Browse can include track lists.** The `inc` values for a release browse are `artist-credits`, `labels`, `recordings`, `release-groups`, `media`, `discids`, and `isrcs` (with `recordings`). One browse request can therefore return many releases with their tracks, instead of one lookup per release. The cost of an artist run is roughly the number of pages, which is about the total track count divided by 500. This corrects the earlier estimate of tens of minutes, which assumed one call per release. A discography of a few hundred releases is likely to take under a minute of calls. This is an estimate and should be measured.
- **Lookups cap linked entities at 25,** so large sets must be browsed.
- **Release statuses:** official, promotion, bootleg, pseudo-release, withdrawn, and cancelled. Withdrawn and cancelled were not in the earlier premises. Releases can be filtered with `status=` and `type=`. Release groups have primary types (album, single, ep, broadcast, other) and secondary types (including compilation, live, remix, and soundtrack).
- **Release groups can be browsed by artist,** and `release-group-status=website-default` excludes groups containing only promotional, bootleg, or pseudo-release items. A release browse can be filtered by release group: `GET /ws/2/release?release-group=<MBID>`.

**Still to verify with fixtures:** the exact artist-credit structure and join phrases; the date formats, including year-only dates; disambiguation text on releases; where edition markers actually appear (title, disambiguation, or neither); track fields (position, number, length); that a release carries its release-group ID; the shape of an artist search result (score, disambiguation); and error responses.

**Changes this implies for section 33**

- Step 1 (filter) must also handle withdrawn and cancelled releases. Setting them aside, as bootlegs are, is the proposed treatment. \[Proposed\]
- The artist-run extractor browses `release?artist=` with the `inc` values above and pages with the offset rule. \[Proposed\]
- Matching an operator's spreadsheet record may still need a search followed by lookups or a browse by release group. \[Proposed\]

**Fixture plan.** Save real responses as files for: an artist search; a first and second page of an artist release browse with track lists; a single release lookup including ISRCs; the releases of one release group that has a remaster; a bootleg (browsed with `status=bootleg`); a compilation (a release with a Various Artists credit); a `track_artist` browse; a not-found response; and a malformed-query response. Before committing the files, check MusicBrainz's data license page to confirm what may be redistributed.

## 35. MusicBrainz data licensing and available fields

Source: the MusicBrainz Database page (musicbrainz.org/doc/MusicBrainz\_Database), pasted into the session on October 12, 2026. This is not legal advice, and the official data license page should still be read before anything is redistributed.

**Licensing**

- **Core data is CC0** (public domain, no restrictions). Core data is: areas, artists, events, genres (the list itself), instruments, labels, mediums, places, recordings, release groups, releases, series, works, relationships and URLs, and CD stubs.
- **Supplementary data is CC BY-NC-SA 3.0** (non-commercial, credit required, derivative works under the same license). It is: user-submitted annotations, tags (including genre associations) and ratings, derived statistics, search indexes, edit history, and non-personal user data.
- All data is available under a commercial license. The API has its own commercial-use rule (section 34).

**Decisions**

- **LI1.** Fixtures use core entity fields only. They never request tags, genres, ratings, or annotations. Search results are produced from search indexes, which are listed as supplementary, so search fixtures are kept minimal and search scores are never stored as data. \[Proposed\]
- **LI2.** In v1 the catalog stores only core MusicBrainz fields. Tags, genre associations, ratings, and annotations are not stored. If they are ever added, they carry the NonCommercial-ShareAlike terms, which conflict with a commercial Plex-like product. \[Proposed\]
- **LI3.** MusicBrainz is credited in the application and documentation as a courtesy. Credit is required for supplementary data. \[Proposed\]

**Fields confirmed to exist (shapes still to be verified by fixtures)**

- Releases: title, artist credit, type, status, language, date, country, label, catalog number, barcode, media, disc IDs, ASIN, a disambiguation comment, and the MBID.
- Mediums: format and a list of tracks (title, artist credit, duration).
- Recordings: title, artist credit, duration, ISRC, a disambiguation comment.
- Release groups: title, artist credit, type, a disambiguation comment.
- Artists: name, sort name, aliases, type, begin and end dates, and a disambiguation comment.

**What this changes in section 33**

- **There is no dedicated edition field.** Edition markers can come only from the title and the disambiguation comment, plus differences in label, catalog number, barcode, date, and country. This confirms why the marker-based rule is unreliable and why the decision to send those cases to an operator is sound.
- **More grouping signals are available.** Two releases with the same barcode, or the same disc ID, are very likely the same product with the same track list and durations. Catalog number and ASIN are further hints. A different barcode does not mean a different edition, since regional pressings often differ. These are proposed as extra signals in Step 2 of the grouping rule, to be evaluated on the fixtures. \[Proposed\]
- To request the disc IDs and barcodes the fixtures need, include `discids` in the `inc` values for the release browse and lookup.

## 36. MusicBrainz rate-limiting rules (corrects and extends section 34)

Source: MusicBrainz's Rate Limiting documentation, pasted into the session on October 12, 2026. The page states its rules are "as of 2012-01-08" and may change at any time, so they should be rechecked before relying on them.

**Verified**

- **The limit is per source IP address, not per application.** The average allowed rate is one request per second per IP. Section 34 said "per client application", and that was wrong for the standard case. User-Agent throttling applies only to specific listed clients and to "anonymous" User-Agents. A global limit of 300 requests per second also exists and is not a concern here.
- **Exceeding the rate is a cliff, not a slope.** If the rate from an IP is too high, all of its requests are declined with `503 Service Unavailable` until the rate drops to one per second or lower. At four requests per second, 100% are refused, not 75%. No `Retry-After` header is documented.
- **User-Agent format.** The suggested form is `Application name/<version> ( contact-url )` or `( contact-email )`, with the version included. A blank User-Agent, or a default such as `Java` or `Python-urllib`, counts as anonymous and is subject to throttling. A .NET `HttpClient` sends no User-Agent unless one is set, so setting it is required.
- **Scheduling.** Applications should not wake at a fixed time (such as 03:00 or the start of an hour) and query a lot of data, because many installs doing so create artificial peaks. A background task should spread its calls at random intervals through the day. Applications that cause impact may be blocked.
- **Polling for changes is discouraged.** The page asks applications not to poll MusicBrainz to check whether metadata has changed, because metadata changes rarely and polling gives poor results. It says there is no good change-notification solution yet.

**Decisions and requirements**

- **RL1.** The shared limiter paces calls below the limit with a margin (for example one call per 1.2 seconds), counted across every worker and every trigger that shares an egress IP. \[Proposed\]
- **RL2.** A `503` from MusicBrainz is treated as Unavailable (transient). Because the penalty is total until the rate drops, the first `503` trips a circuit breaker: the shared limiter pauses all workers for a back-off period (growing with repeated failures) and slows down afterward. Retrying immediately is never done. \[Proposed\]
- **RL3.** Scheduled MusicBrainz work is never run at a fixed clock time or at the top of an hour. Start times are randomized (jitter) and calls are spread out. \[Proposed\]
- **RL4.** The client always sends a User-Agent of the form `JukeboxHarvester/<version> ( <contact email> )`. A missing User-Agent is a startup error. \[Proposed\]
- **RL5.** Fixture recording and any manual testing follow the same pacing and send the same User-Agent. \[Decided\]

**Conflict with the scheduled-watching requirement (open decision).** The requirement for a scheduled job that watches artists for new releases is polling for changes, which the page asks applications not to do. Options:

1. **Low-frequency, low-cost watching.** Each watched artist is checked at most about once a week, at jittered times. The check is one small browse request (`limit=1`) whose `release-count` is compared with the stored count, which detects new releases cheaply. It still polls, so the watch list is kept small and the frequency low. This is a design idea that must be verified against real responses. \[Proposed\]
2. **The Live Data Feed or a local mirror.** MusicBrainz publishes full database snapshots twice a week and a replicated Live Data Feed (sign-up required), which is the sanctioned way to stay current without polling the API. It is a large operational commitment (a PostgreSQL mirror) and is deferred, but the reference-lookup interface (R11) would allow it without changing the pipeline.
3. **Manual only in v1.** No scheduled MusicBrainz work. Operators request artists on demand, and scheduling is added later. The choice is \[Open\]. Whichever is chosen, R-series text about the scheduled job and N5 (priority between scheduled and manual work) should be updated to match.

**Fixture script.** The two-second pause after each request is within the rules. On the first unexpected status, stop and wait before retrying.

## 37. Scheduled watching of artists (resolves the open decision in section 36)

**Decision: low-frequency, low-cost watching.** \[Decided\] This closes the open item "what the scheduled job watches" (section 16, item 8). The scheduled job watches a small list of artists for new releases, using a cheap check at a low frequency, and MusicBrainz's request not to poll for changes is honored by keeping the footprint minimal. The Live Data Feed or a local mirror remains the path if watching grows (section 36, option 2).

**Design** \[Proposed\]

- **SW1.** The coordinator owns a watch list of artists, each with its MusicBrainz ID, the last known release count, and the time of the last check.
- **SW2.** Each watched artist is checked at most about once a week. Check times are jittered, never at a fixed clock time or the top of an hour (RL3). The check goes through the shared limiter at a lower priority than on-demand work (N5).
- **SW3.** The check is one small browse request with `limit=1` for the artist's releases, using the same filters as the artist run. It reads the total release count and compares it with the stored count. The existence of `release-count` in browse responses is to be confirmed by the fixtures.
- **SW4.** If the count rose, a run is started with the Scheduled trigger for the same source key as a manual request (R30). If a run for that artist is already active, the check joins it. The run then follows the normal pipeline and the default approval policy (P1 to P6), and a watched source may be set to require approval.
- **SW5.** When an artist is added to the watch list, the first import is a normal request. The stored count is set when that run finishes, so the first scheduled check does not trigger a duplicate import.
- **SW6.** After a run completes, the stored count is updated to the count the run saw.
- **SW7.** A count that fell is not acted on automatically. It is logged and flagged for an operator, because it can mean releases were merged or removed in MusicBrainz.
- **SW8.** The count detects new releases, not edits to existing ones. A net-zero change (one release added and one removed) is missed. A manual run covers this, and refreshing existing records stays a manual operation (R28).
- **SW9.** The watch list is kept small. The maximum size is \[Open\]. A cap of a few dozen artists would make the weekly cost a few dozen tiny requests, which is an estimate and not a measured value.
- **SW10.** A 503 during a check trips the circuit breaker (RL2) and the check is rescheduled later. It is never retried immediately.

**Test cases**

- TC-L1. Given a watched artist whose count is unchanged, then no run starts.
- TC-L2. Given a count that rose, then a run starts with the Scheduled trigger, or joins an active run for the same artist.
- TC-L3. Given several watched artists, then check times are jittered, are never at the top of an hour, and are spaced by at least the limiter's interval.
- TC-L4. Given a 503 during a check, then the circuit breaker pauses all MusicBrainz work and the check is rescheduled, not retried immediately.
- TC-L5. Given a count that fell, then nothing runs and an operator is flagged.
- TC-L6. Given an on-demand request and a scheduled check waiting at once, then the on-demand request goes first.
- TC-L7. Given a newly watched artist, then the stored count is set only after the first import finishes, and the first check starts no run.

## 38. MusicBrainz response shapes (verified from fixtures recorded October 12, 2026)

The fixtures are saved in a scratch folder and are not in the repo yet. The first browse page (25 releases with tracks) is about 219 KB, roughly 9 KB per release, because the same artist object is repeated in every credit. Committed fixtures should use small pages. Tooling note: PowerShell's `ConvertTo-Json`, when fed by the pipeline, unwraps a one-element array into a bare object, so inspect structure with `-InputObject`, or by checking `.Count`.

**Confirmed behavior**

- **Search** (`artist?query=`): returns `count`, `offset`, `created`, and an `artists` array whose items have `id`, `name`, `score`, `disambiguation`, and an optional `type`. For "autechre" the top score was 100 and the next was 42. For "unbalanced" the top two were two different artists, "UnBalanced" (100) and "Unbalanced" (99). The artist-candidate rule therefore needs both a minimum top score and a gap to the runner-up.
- **Browse** (`release?artist=`): returns `release-count`, `release-offset`, and `releases`. Autechre has 346 releases. Page 2 at offset 25 worked as documented.
- **Watching check (SW3) confirmed.** A browse with `limit=1` returns the full `release-count` (346) and a single release.
- **R34 confirmed.** `track_artist` returned 208 releases for Autechre, every one on the page credited to Various Artists, so an `artist=` browse leaves them out.
- **Status filter confirmed.** `status=bootleg` returned 421 releases, all with the status value `Bootleg` (capitalized in the response). Comparisons in code ignore case.
- **Errors.** An invalid ID returns 400 with `{"error":"Invalid mbid."}`. A well-formed ID that does not exist returns 404 with `{"error":"Not Found"}`. The extractor keys on the status code and uses the message only for logs. An invalid ID is our bug and is never retried, and not found is a data outcome. A search with an unbalanced quote returned 200 with six loosely matched artists and no error, so a malformed query can fail silently. Escaping still needs testing with colons, parentheses, hyphens, and the words AND and OR.

**Release fields seen:** `artist-credit`, `asin`, `barcode`, `country`, `cover-art-archive`, `date`, `disambiguation`, `id`, `label-info`, `media`, `packaging`, `packaging-id`, `quality`, `release-events`, `release-group`, `status`, `status-id`, `text-representation`, `title`. Notes: `disambiguation` is an empty string when absent, so empty strings are normalized to null. `date` was a full date here. The `cover-art-archive` field may tell us whether front art exists before any call to the Cover Art Archive, which would avoid blind calls (N10). Its contents are still to be checked.

**Artist credit.** An ordered array of entries. Each entry has an `artist` object (`id`, `name`, `sort-name`, `type`, `disambiguation`, `country`), a `name` (as credited, which may differ from the artist's own name), and a `joinphrase`. This confirms the list-with-join-phrases structure behind CR1. The credit as printed is the concatenation of each entry's `name` and `joinphrase`. The artist object carries its disambiguation text, so candidate screens need no extra call.

- **There is no role field.** Whether an artist is primary or featured is not stated. It would have to be inferred, for example from a join phrase containing "feat.", which is interpretation and belongs in Transform. When the join phrase is unclear, the role goes to review. This is not yet verified on a multi-artist release. \[Proposed\]

**Release group:** `id`, `title`, `primary-type` (for example Album), `secondary-types` (an array, possibly including Compilation), `first-release-date`, `disambiguation`, and its own `artist-credit`. The catalog's `isCompilation` can be derived from a secondary type of Compilation. \[Proposed\]

**Track:** `number` (a string that can be non-numeric, such as "A1" on vinyl), `position` (an integer), `length` in milliseconds (may be missing), `title`, `artist-credit`, and a `recording` object (`id`, `title`, `length`, `disambiguation`, `artist-credit`, `first-release-date`). ISRCs appear only when requested with `isrcs` (on the lookup). Disc IDs live under each medium, not on the release.

**Changes this implies**

- **Contract (section 31).** A track carries `numberAsPrinted` (a string) in addition to the integer disc and position. Duration is converted from milliseconds and may be absent. \[Proposed\]
- **Contract and section 30.** The role of each artist is derived by Transform and not read from the source.
- **Compilation flag** comes from the release group's secondary types.
- **Cover art.** Use the release's `cover-art-archive` data to decide whether to fetch artwork at all. \[Proposed\]

**Still to check:** the medium object (format, position, title, track count, discs), the `label-info` and `release-events` shapes, the contents of `cover-art-archive`, the spread of `status` and date lengths across a page (including year-only dates), a joint-credit release and a "feat." join phrase, an actual remaster group, a Various Artists compilation, and the ISRC shape in the lookup.

## 39. Findings from one real page of releases (refines sections 33 and 38)

Data: the first browse page for one artist (25 of 346 releases, with tracks), recorded October 12, 2026. One page of one artist's early catalog is a small sample, so every figure below is a sample and not a rule.

**Observed**

- **Statuses:** 23 Official and 2 Promotion.
- **Date precision:** 16 full dates, 8 year-only, and 1 year-and-month. About a third were year-only, which supports storing dates with a precision (RD1 and RD2).
- **Front cover art:** the `cover-art-archive` field reported `front: true` for 15 and `false` for 10. Checking it first would avoid the Cover Art Archive call for about 40% of releases on this page. \[Proposed\]
- **Formats:** `12" Vinyl` (8), `CD` (9), `Cassette` (6), `10" Vinyl` (1), and `Digital Media` (1). Format names contain quote characters and are not a small fixed set. Digital releases are rare in this catalog, so the format preference must degrade gracefully.
- **Country codes:** GB, US, DE, and `XW`, a pseudo-code that stands for worldwide. The region preference must recognize `XW`.
- **Barcodes:** 18 of 25 releases had one and 7 had none. A barcode cannot be the main signal.
  - Different formats of one release carry different barcodes (three distinct barcodes among the GB releases of one album).
  - The same barcode appeared on the GB and DE releases of one album, which is a regional variant of the same product and should collapse into one edition.
  - The same barcode also appeared on two releases of one album whose disambiguations differ ("limited edition silver vinyl" and "Reversed cover"), so a shared barcode does not guarantee identical releases.
- **Titles:** one album had 8 releases on the page and another had 7. One EP appeared under four titles that differ only in a trailing parenthetical ("Basscad, EP", "Basscad, EP (Basscadetmxs)", and two others), so trailing parentheses are not reliably edition markers.
- **Disambiguations seen:** "33rpm", "limited edition silver vinyl", "Reversed cover", and "BASSCADETMXS". They mix edition-like distinctions with unrelated hints and follow no common vocabulary, which supports sending ambiguous cases to an operator.
- **Size:** 204 tracks across the 25 releases, about 8 per release. An artist with 346 releases is about 2,800 tracks, which is at least 6 pages at the 500-track page limit and about 14 pages at a `limit` of 25. At the shared limiter's spacing (RL1) that is well under a minute of calls. This is an estimate to confirm with a full run.

**Proposed refinements to the grouping rule (section 33)**

- **G1.** Compare tracks as a flattened, ordered list of normalized titles. The medium structure (number of discs, vinyl sides versus one CD) never splits an edition on its own.
- **G2.** Titles are compared after removing trailing parenthetical text. The removed text is counted with the edition markers when comparing marker sets, and is not assumed to be an edition marker by itself.
- **G3.** The edition's date is the representative release's own date, with its own precision, replacing the "earliest member date" in section 33. Mixed precisions make "earliest" ambiguous (a year-only 1993 could fall before or after a full date in 1993). The release group's `first-release-date` is kept as an attribute for the original release year. \[Open, with this as the recommendation\]
- **G4.** Region preference treats `XW` as worldwide.
- **G5.** A shared barcode or disc ID supports grouping. A missing or different barcode never splits a group. A shared barcode with conflicting disambiguations is not enough to group automatically.

**Test cases**

- TC-K9. Given the same tracks split across two vinyl discs on one release and one CD on another, then the flattened track lists are treated as equal.
- TC-K10. Given releases whose titles differ only in a trailing parenthetical, then they are compared by base title, and the difference counts as a marker difference.
- TC-K11. Given two releases with the same barcode and conflicting disambiguations, then they are not grouped automatically.
- TC-K12. Given the same barcode in two countries, then they group as one edition.
- TC-K13. Given a release with no barcode, then grouping falls back on the other signals.
- TC-K14. Given a region of `XW`, then it ranks as worldwide.

**Still to check:** a joint-credit release and a "feat." join phrase, a release with several release events, a release with no label, a real remaster group, a Various Artists compilation release, the ISRC shape in the single-release lookup, and the media fields of a multi-disc release.

## 40. Joint and featured credits (verified from search fixtures, October 12, 2026)

Two release searches were recorded, one for a known joint album and one for a known featured credit. Only two credit patterns have been seen, so the rules below are a starting point.

**Observed.** A release's `artist-credit` is an ordered array. Each entry has the credited `name` and a `joinphrase`, and the join phrase belongs to the entry before the next one (the last entry's is empty).

- A joint album: `Tony Bennett[ & ]Lady Gaga[]`. Two entries, joined by `&`.
- A featured credit: `Daft Punk[ feat. ]Pharrell Williams[ & ]Nile Rodgers[]`. Three entries, joined by `feat.` and then `&`.
- The credit as printed is the concatenation of each entry's name and join phrase, for example "Tony Bennett & Lady Gaga".
- Search results have the same top-level shape as artist search (`count`, `created`, `offset`, `releases`), and the credit has the same structure as in a browse.
- Search results are noisy. The featured-credit query matched other artists' covers, medleys, and a title containing "(Daft Punk Remix)". A parenthetical such as "remix" in a title is therefore not an edition marker, which supports G2. One album title appeared on at least eight releases with the same joint credit.

**Role inference (refines CR1 and section 38)**

- **RI1.** MusicBrainz states no roles, so roles are derived from the join phrases by position. Entries are Primary until a featuring phrase appears (`feat.`, `featuring`, and similar), and every later entry is Featured, including entries joined by `&` after it. The same `&` therefore means co-headliners when it comes before a featuring phrase and co-featured guests when it comes after one. \[Proposed\]
- **RI2.** The phrase vocabulary is configuration. A join phrase outside the known set (for example "with", "vs.", "x", a slash, or an unfamiliar word) leaves the roles unresolved and sends the record to review. Only `&` and `feat.` have been seen. \[Proposed\]
- **RI3.** Inference happens in Transform, and the extractor passes the join phrases through unchanged. \[Proposed\]
- **RI4.** A record whose roles were inferred, and not confirmed by an operator, can still auto-commit only if every phrase is in the known set. \[Proposed\]

**Test cases**

- TC-I7. Given `A[ & ]B`, then A and B are both Primary at positions 1 and 2, and the credit as printed is "A & B".
- TC-I8. Given `A[ feat. ]B[ & ]C`, then A is Primary, B and C are Featured, and the credit as printed is "A feat. B & C".
- TC-I9. Given a join phrase outside the known set, then roles are left unresolved and the record goes to review.
- TC-I10. Given a title containing "(Remix)", then the text is not treated as an edition marker.

**Still to check:** other join phrases seen in practice, a Various Artists compilation release, the edition variants within one release group (for example standard versus deluxe), and a remaster group.

## 41. Edition evidence from one release group (refines sections 33 and 39)

Data: one release group with 16 releases (a joint album with a standard edition, deluxe editions, and retailer variants), recorded October 12, 2026. One album is a sample and not a rule. Track lists were compared by hashing the ordered titles, and titles were compared position by position for the pairs below.

**Observed**

- **Track count undercounts editions.** The 16 releases have 7 distinct track counts but 10 distinct raw track lists, so the lists must be compared, not the counts.
- **Most raw differences were typography.** Two 11-track releases that share a barcode differed in four titles only by apostrophe style (a curly ’ against a straight '). The same tracks therefore produced two raw hashes. After folding apostrophes, seven releases (DE, AU, a release with no country, four US releases including the Starbucks one, and an XE release) have one list.
- **Some differences are real.** A "Target exclusive" release and an unmarked release, both with 17 tracks, share the first 15 tracks and differ in tracks 16 and 17 (different bonus tracks).
- **Retailer wording means nothing consistent.** The Starbucks release had the standard list, and the Target release had different bonus tracks.
- **Markers are unreliable.** The same edition had a marker on one release and none on another (18 tracks, XE and BR). "deluxe" and "deluxe edition" were used on two releases with 15 tracks, and the same wording appeared on a 15-track and a 16-track release.
- **Result so far:** 16 releases, 10 raw lists, 9 lists after folding typography (8 if the two 15-track lists also match, which has not been checked yet).

**Rules (refine G1 to G6)**

- **TN1.** Track titles are normalized before comparison: Unicode compatibility normalization, typographic quotes, apostrophes, and dashes folded to plain characters, case folded, and whitespace collapsed. Whether to also fold accents or strip punctuation is decided by testing on fixtures. \[Proposed\]
- **TN2.** The normalized, flattened track list is the main separator of editions, and markers are secondary. Different lists mean different editions, whatever the markers say. The same list means the same edition unless an audio-affecting marker is present (G6). \[Proposed\]
- **TN3.** Retailer and distribution wording (a store name, "exclusive", "limited") is never used to decide anything. Only the track list is. \[Proposed\]
- **TN4.** Raw comparison of titles is never used for grouping. A change that makes titles differ only by typography must not create a new edition. \[Proposed\]

**Test cases**

- TC-K15. Given two track lists that differ only in apostrophe style, then they compare equal.
- TC-K16. Given the same normalized list with "deluxe" on one release and "deluxe edition" on another, then they are one edition.
- TC-K17. Given two releases with the same track count and different bonus tracks, then they are different editions.
- TC-K18. Given a retailer marker on a release whose normalized list equals another release's, then they are one edition.
- TC-K19. Given the same barcode on two releases whose normalized lists differ, then they are not grouped.

**Still to check:** whether the two 15-track lists match after normalization, whether a remaster shows up in the track titles (some sources append text like "Remastered" to titles), and the Various Artists compilation.

## 42. Addendum to section 41 (checked October 12, 2026)

The two 15-track releases ("deluxe" and "deluxe edition") were compared title by title. They differ in six titles, and every difference is only apostrophe style (curly against straight). One title mixes both styles inside itself ("It Don't Mean a Thing (If It Ain’t Got That Swing)"). So the two wordings are one edition, and the normalizer must work per character and not by pattern.

With that result, the 16 releases fall into 8 editions by track list, listed by track count: 11 (seven releases), 12, 15 (two releases), 16, 17 (a Target release with different bonus tracks), 17 (the unmarked release), 18 (two releases), and 19. The raw hashes had shown 10 lists. This count is derived from the title comparisons, and the hash of normalized titles has not been run yet.

The pending decisions from sections 39 and 41 remain open: G3 (the edition's date is the representative release's own date, with the release group's first-release date kept as a separate attribute) and G6 (audio-affecting markers always escalate to an operator, packaging and distribution markers do not when the normalized track lists agree, and unknown markers escalate).

## 43. Grouping and representative-release rule, current version (supersedes section 33, steps 2 and 3, and the date rule)

This consolidates sections 33, 38, 39, 40, 41, and 42, which spread the rule across several places. Where they differ, this section wins. **G3 and G6 are decided.** \[Decided\] The rule below is built from a few real samples (one artist's early catalog and one album with 16 releases), so the vocabulary and thresholds are starting points. The audio-affecting side of the marker rule has not yet been tested on a real remaster.

**Step 1: filter.** Releases with the status bootleg, pseudo-release, withdrawn, or cancelled are set aside, and the proposal says so. Status values are compared without regard to case. Promotion releases stay but rank low. \[Proposed\]

**Step 2: group into editions.** Releases are grouped by their normalized, flattened track list and their base title.

- Track titles are normalized per character before comparison (TN1): Unicode compatibility normalization, typographic quotes, apostrophes, and dashes folded to plain characters, case folded, and whitespace collapsed. Raw titles are never compared (TN4).
- The list is flattened across media, so the number of discs or vinyl sides never splits an edition (G1).
- Base titles are compared after removing trailing parenthetical text, and the removed text counts as a marker (G2).
- A shared barcode or disc ID supports grouping when the lists agree. A shared barcode with different lists never groups (TC-K19). A missing or different barcode never splits a group (G5).
- Different normalized lists mean different editions, whatever the markers say (TN2). Retailer and distribution wording never decides anything (TN3).

**Step 3: classify each group.**

- **Clear:** all members share the same normalized list and base title, and no audio-affecting marker differs between them. Packaging and distribution markers (limited, deluxe wording, exclusive, a store name) are ignored when the lists agree. **\[Decided\]**
- **Ambiguous (goes to an operator):** an audio-affecting marker (remaster, mono, stereo, remix, and similar) present on some members and not others, or different between them; or a marker the vocabulary does not recognize; or a group that fits the operator's record poorly. **\[Decided\]** This narrows the earlier rule that sent every difference in markers to an operator. That rule came from the original-versus-remaster case, which stays protected.
- An operator can classify an unknown marker once, as audio-affecting or packaging, and the choice is remembered so the same wording is not asked about again. \[Proposed\]
- Only clear groups can auto-commit (P4).

**Step 4: choose the representative,** deterministically, in this order: Official over Promotion; complete data (a date, a label, a duration on every track); format (Digital Media, then CD, then others, with names matched tolerantly since formats include `12" Vinyl` and similar); region (worldwide, which is `XW`, then a configured preference list); earliest date, compared at the coarser of the two precisions; and the lowest release ID only to break an exact tie. \[Proposed\]

**What the edition takes from its releases**

- The representative supplies the title, the label (the first entry of the release's label list), and the track list.
- **The edition's date is the representative's own date, at its own precision (G3). \[Decided\]** This replaces the "earliest member date" in section 33. The release group's first-release date is stored as a separate attribute for the original release year.
- All member release IDs are stored with the representative flagged. Member barcodes and catalog numbers are stored as attributes. \[Proposed\]
- The proposal records every candidate, how it was grouped, the markers seen and how each was classified, and why each ranked where it did.

**Test cases (in addition to TC-K1 to TC-K19)**

- TC-K20. Given an audio-affecting marker on one member only, then the group is ambiguous.
- TC-K21. Given only packaging markers and equal normalized lists, then the group is clear.
- TC-K22. Given an unrecognized marker, then the group is ambiguous.
- TC-K23. Given a group whose members have dates of different precision, then the edition takes the representative's own date and precision.
- TC-K24. Given an operator who classifies an unknown marker, then later groups with that marker are classified without asking.
- TC-K25. Given the release group's first-release date, then it is stored as a separate attribute and is not used as the edition's date.

**Still to check:** a real remaster, to test the audio-affecting side of the marker rule and whether remaster text appears in track titles, and a Various Artists compilation.

## 44. Remaster and deluxe evidence from one release group of 81 releases (puts parts of section 43 under review)

Data: all 81 releases of one classic album's release group, fetched as four browse pages on October 12, 2026. It is one album, so these are a sample and not rules.

**Paging, verified.** The four pages returned 25, 22, 15, and 19 releases (offsets 0, 25, 47, and 62), a total of 81, which equals `release-count`. Pages shrink as releases with many tracks hit the 500-track cap. Stepping the offset by the `limit` would have skipped releases. **PG1. The client advances the offset by the number of releases actually returned, and stops when the offsets sum to `release-count`.** \[Decided\]

**Observed**

- **Track counts:** 62 releases had 11 tracks, and 19 had other counts (12, 24, 25, 30, 36, 40, 51, 58, and 75).
- **Normalized track lists:** 19 distinct lists across the 81 releases. The 62 eleven-track releases fall into three lists (59, 2, and 1). The 19 others fall into 16 lists.
- **Remaster wording appears only in the disambiguation text.** Three releases carry it: "2001 remaster" (on a release dated 2011), "remastered on 180g vinyl at 45 rpm", and "2011 SACD remaster" (36 tracks). No track title in any of the 81 releases mentions a remaster. Where a remaster has the same tracks as the original, the track list cannot tell them apart, and the disambiguation is the only signal. The two eleven-track remasters are probably inside the 59-release list. This has not been checked.
- **Edition wording seen:** "(deluxe)", "(super deluxe)", "deluxe edition", "expanded edition", "35th anniversary edition", "super deluxe edition", and "reissue" ("2004 reissue", "re-issue, marked ..."). A reissue is not necessarily audio-affecting.
- **Pressing notes with no audio meaning:** "made in Japan", "01 matrix", "textured cover", "textured sleeve", "made in West Germany", "reel-to-reel, 7½ IPS, Dolby B", and a Portuguese re-issue note. They contain non-ASCII hyphens, curly quotes, and a ½ sign, so marker text needs the same normalization as track titles.
- **Barcodes cannot be trusted to group.** One barcode appears on three 2001 releases with 12, 25, and 24 tracks. Another appears on a 58-track and a 75-track release. Another appears on three 40-track releases with the same list. This confirms G5 and TC-K19.
- **Incomplete records exist.** One release has no status, and one has no date, country, or barcode.
- **Dates:** 17 of the first page's 25 releases had year-only dates.
- **Unexplained:** five 58-track releases produced five different normalized lists, and five 40-track releases produced three. It is not yet known whether these are real differences or more title noise.
- **The big group spans many years.** The 59-release list covers 1977 vinyl originals through 1990 CD reissues in many countries. Under the ranking in section 43, a CD would be the representative.

**Proposals under review (not yet decided).** Section 43 recorded G3 and G6 as decided. This data puts them in question.

- **G3-revised.** The edition's date is the earliest member date, taking the most precise date within the earliest period. Reason: the ranking prefers CD, so the representative's own date would read 1984 or later for an album first released in 1977. The earliest-member rule gives 1977-02-04, which is a date a release actually has.
- **G6-revised.** Only wording that implies different audio (remaster and its variants, mono, stereo, remix, and similar) escalates. Pressing notes and other free text are ignored, because in this one album at least seven different pressing notes would otherwise each send a group to an operator. Edition-name wording (deluxe, expanded, anniversary) matters only through the track lists, which already separate those editions. "Reissue" is treated as packaging. The vocabulary is matched by stem ("remaster", "remastered", "SACD remaster") and lives in configuration.
- **G7.** Members with an audio-affecting marker are split out of a group and not allowed to make the whole group ambiguous. The unmarked members form a clear group. The marked members form their own candidate edition, which goes to an operator for confirmation, because a remaster is a separate edition by policy. In the data above, two remasters would no longer hold 57 plain releases hostage.

**Test cases (if the proposals are accepted)**

- TC-K26. Given 59 releases with the same normalized list, two of which carry a remaster marker, then the 57 others form a clear group and the two form a separate candidate edition for an operator.
- TC-K27. Given pressing notes such as "made in Japan" or "textured cover", then they do not affect classification.
- TC-K28. Given a release with no status, then Step 1 treats it as unknown and the proposal flags it. It is not silently dropped.
- TC-K29. Given the same barcode on releases with different normalized lists, then they are not grouped.
- TC-K30. Given members with dates 1977 and 1977-02-04 in the earliest period, then the edition's date is 1977-02-04.
- TC-K31. Given a marker with a non-ASCII hyphen or curly quote, then it is normalized before matching.
- TC-K32. Given paging offsets, then the client advances by the returned count.

**Still to check:** why the 58-track and 40-track releases differ (real differences or title noise), whether the two eleven-track remasters sit inside the 59-release list, and the Various Artists compilation.

## 45. Why the deluxe releases differ, and a correction to section 44

**Correction.** Section 44 said one barcode appears on three 40-track releases "with the same list". Of those three releases, two share a normalized list and the third (a US "expanded edition") has a different one. So even a shared barcode on releases with the same track count does not guarantee the same list.

**Observed** (same album, 81 releases, October 12, 2026)

- **40-track set (5 releases, 3 distinct lists).** The first release differs from each of the others in the same 14 consecutive positions, starting at the 13th track. Side by side, the first labels its live tracks with a suffix ("Dreams (live)", "Intro (live)"), and the others use the bare title ("Dreams", "Intro"). They are the same live recordings under different title conventions. Counting titles that exist in only one list gives 6 new and 14 missing, because some bare live titles coincide with studio titles already in the list.
- **58-track set (5 releases, 5 distinct lists).** Two releases hold exactly the same songs in a different order (34 positions differ, none new or missing). Two others each differ from the first by 13 titles, starting at the same position. A 2019 release differs more (7 titles not in the first, 18 missing). Whether the 13-title differences are also title conventions has not been checked.

**What this means**

- Comparing normalized track titles is sensitive to annotation conventions as well as typography. Stripping trailing parentheticals from track titles is unsafe, because "(live)", "(remix)", and "(radio edit)" can mark genuinely different recordings.
- Releases with the same songs in a different order are a real question: the same edition re-sequenced for a format, or a different edition.

**Proposals under test (not decided)**

- **RC1.** Compare the ordered list of recording IDs, which each track in a browse carries, as the primary signal of track content, with the normalized title list as the fallback when IDs are missing. Recording IDs ignore typography and annotation text. The open question is whether a remaster shares recording IDs with its original. If it does, the marker text stays the only way to tell them apart, as in section 44. If it does not, the lists differ and the remaster separates on its own. This is being checked against the fixtures.
- **RS1.** Releases that hold the same songs in a different order are classified as ambiguous and go to an operator, and are not merged or split automatically. \[Open\]

**Test cases (if the proposals are accepted)**

- TC-K33. Given two releases with the same recording IDs in the same order and different title annotations, then they are the same edition.
- TC-K34. Given two releases with the same recording IDs in a different order, then the group is ambiguous.
- TC-K35. Given a shared barcode and different recording lists, then the releases are not grouped.

Sections 43 and 44 remain the current statement of the rule. Revisions to G3, G6, and G7 are still awaiting a decision, and the recording-ID result will be folded into the same consolidation.

## 46. Result of the recording-ID test (withdraws RC1 from section 45)

Data: the same 81 releases, compared by their ordered lists of recording IDs, on October 12, 2026.

**Observed**

- **Recording IDs fragment more than titles do.** The 81 releases have 31 distinct recording lists against 19 normalized title lists. Among the 62 eleven-track releases there are 16 recording lists against 3 title lists. MusicBrainz keeps separate recording entries for different pressings and masterings, so releases of one album often point to different recording IDs.
- **Remasters have their own recording lists, and so do many ordinary releases.** A recording-list difference is therefore not specific to remasters. The two eleven-track remasters have recording lists that differ from an ordinary release's, and their normalized title list is identical to the ordinary release's. This confirms they sit inside the 59-release title-list group.
- **Recording IDs did help where titles failed.** In the 40-track set, four of the five releases share one recording list, including the one that labels its live tracks "(live)". Title lists had split it into three.
- One recording list is shared by two eleven-track releases, one of which is marked "2001 remaster". The other has not been identified. If it carries no marker, a shared recording list could reveal an unmarked remaster.

**Decisions and proposals**

- **RC1 is withdrawn.** Using recording IDs as the primary signal would split the 59-release group into many editions. \[Decided\]
- **RC2.** Two releases are the same edition when their normalized title lists are equal, or when their recording lists are equal. Each signal fails by splitting in a different case (titles on annotation differences, recordings on pressing and mastering differences), and neither merged anything wrongly in this sample. A group built this way in which some pair of members matches under neither signal is ambiguous, because a bridging release could chain two different editions together. \[Proposed, untested\]
- **MP1.** If any member of a recording-list group carries an audio-affecting marker, the whole recording-list group is treated as marked. This would catch a remaster whose disambiguation is empty. It rests on one example. \[Proposed, to verify\]
- RS1 (same songs in a different order go to an operator) is unchanged and open.

**Test cases (if accepted)**

- TC-K36. Given releases that share a title list but have different recording lists, then they are one edition.
- TC-K37. Given releases that share a recording list but have different title lists, then they are one edition.
- TC-K38. Given a group in which two members match under neither signal, then the group is ambiguous.
- TC-K39. Given an unmarked release whose recording list matches a remaster-marked release, then it is treated as marked.

**Where the exploration stands.** The fixtures now cover the artist browse, search, errors, joint and featured credits, a release group with 16 releases, and a release group with 81 releases including remasters and deluxe sets. Further checks are better done as tests in the code, run against trimmed fixtures. The open decisions are G3-revised, G6-revised, G7, RS1, and RC2. The Various Artists compilation fixture has not been recorded.

## 47. Addendum to section 46: an unmarked release shares a remaster's recordings

The release that shares a recording list with the "2001 remaster" release was identified. It is dated 2011-04-16, was released in the US, has no disambiguation text, and has its own barcode. The marked release is dated 2011, was released worldwide, and carries the text "2001 remaster". A separate US release of the same date carries the text "remastered on 180g vinyl at 45 rpm" and has a different recording list.

So in this one sample, an unmarked release has the same recordings as a marked remaster, and it is dated years after the remaster was made, which is consistent with a reissue of that remaster. This supports MP1, but it does not prove the unmarked release is a remaster. Confirming it would need MusicBrainz's own relationship data for the release, which has not been fetched.

**What this suggests about the two signals.** The normalized title list identifies the track content and sequence, which is what defines an edition. The recording list identifies the underlying audio. A difference in recording lists alone is common between pressings of one edition (16 recording lists among the 62 eleven-track releases) and is not a reason to escalate. A recording list shared with a marker-carrying release is evidence about the audio, and it is what MP1 uses.

**Effect on the group in section 44.** Of the 59 releases with the same title list, the marked ones (two) plus the unmarked release that shares a recording list with one of them would be split out as a candidate edition for an operator, and the remaining releases would form a clear group. Without MP1, the unmarked release would stay in the clear group and could auto-commit as the original edition. \[Proposed\]

MP1 remains unverified beyond this single example and stays \[Proposed, to verify\]. The open decisions are G3-revised, G6-revised, G7, RS1, RC2, and MP1.

## 48. Grouping and representative-release rule: CURRENT VERSION (supersedes sections 33 and 43 to 47)

This is the version to hand to a coding agent. It consolidates sections 33 and 43 to 47 and records the decisions made on October 12, 2026. It is built from fixtures of real MusicBrainz data: one artist's early catalog (346 releases), a 16-release album, and an 81-release album with remasters and deluxe sets. These are a few samples, so the vocabulary and defaults are starting points to be tested in code. Each MusicBrainz release arrives as one candidate (R37). Grouping is Transform's job.

**Step 1: filter.**

- Releases whose status is bootleg, pseudo-release, withdrawn, or cancelled are set aside, and the proposal says so. Status is compared without regard to case. \[Proposed\]
- Promotion releases stay but rank low. A release with no status stays in, is flagged in the proposal, and ranks below Official and Promotion. \[Proposed\]

**Step 2: group into editions.**

- Track titles and marker text are normalized per character before any comparison (TN1): Unicode compatibility normalization, typographic quotes, apostrophes, and dashes folded to plain characters, case folded, and whitespace collapsed. Raw text is never compared (TN4). Trailing parentheticals are never stripped from track titles, because "(live)", "(remix)", and "(radio edit)" can mark different recordings. \[Decided\]
- Track lists are flattened across media, so the number of discs or vinyl sides never splits an edition (G1). \[Decided\]
- Album titles are compared after removing trailing parenthetical text. The removed text is read as marker text (G2). Releases must have the same base album title to be grouped. \[Decided\]
- **Two releases are the same edition when their normalized title lists are equal, or when their ordered recording-ID lists are equal (RC2). \[Decided\]** Titles fail on annotation differences, and recording IDs fail on pressing and mastering differences, so either match is enough. Recording IDs are never the only primary signal (RC1 is withdrawn).
- A shared barcode or disc ID supports grouping and never decides it. A missing or different barcode never splits a group (G5). Retailer and distribution wording never decides anything (TN3). \[Decided\]
- Different normalized lists with different recording lists mean different editions (TN2).
- **Re-sequenced releases (RS1).** Releases that hold the same songs in a different order are not merged or split automatically. They are classified as ambiguous and go to an operator. \[Decided\]
- A group built this way in which some pair of members matches under neither signal is ambiguous, because a bridging release could chain two different editions together. \[Decided\]

**Step 3: classify each group.**

- **Marker vocabulary.** Marker text comes from the release's disambiguation and the removed title parentheticals. Only wording that implies different audio is audio-affecting: matched by stem, the starting set is remaster (also remastered), mono, stereo, and remix. It lives in configuration and grows with evidence. Wording about packaging or pressing (limited, exclusive, a retailer's name, "made in Japan", "textured cover", and so on), "reissue", and edition names (deluxe, expanded, anniversary) are ignored as markers. Edition names matter only through the track lists, which already separate those editions. Free text outside the vocabulary is ignored. \[Decided, G6-revised\]
- **Marker propagation (MP1).** If any member of a recording-list group carries an audio-affecting marker, every member of that recording-list group is treated as marked. This catches a remaster whose disambiguation is empty. It rests on one real example and is to be verified on more albums. \[Decided\]
- **Split (G7).** Marked members are split out of their group and do not make the rest ambiguous. The unmarked members form a clear group. The marked members form their own candidate edition, which goes to an operator, because a remaster is a separate edition by policy. \[Decided\]
- **Clear:** all members share a normalized title list or recording list, have the same base album title, no pair matches under neither signal, and none is marked. Clear groups can auto-commit (P4).
- **Ambiguous (goes to an operator):** the marked candidate edition from the split; a re-sequenced group (RS1); a group with a pair that matches under neither signal; and, on the spreadsheet path, a group that fits the operator's record poorly.
- Residual risk, accepted: an unmarked remaster with no marker and no shared recording list will look like an ordinary release. The operator review of marked groups and the "recently auto-committed" view (P5, P7) are the mitigations.

**Step 4: choose the representative,** deterministically, in this order: Official over Promotion over no status; complete data (a date, a label, a duration on every track); format (Digital Media, then CD, then others, with names matched tolerantly because formats include `12" Vinyl` and similar); region (worldwide, which is `XW`, then a configured list); earliest date, compared at the coarser of the two precisions; and the lowest release ID only to break an exact tie. \[Proposed\]

**What the edition takes from its releases**

- The representative supplies the title, the label (the first entry of the release's label list), and the track list.
- **The edition's date is the earliest member date, using the most precise date within the earliest period (G3-revised). \[Decided\]** The earliest year among the members is found first, and within that year the earliest full date is used if one exists, with its own precision. This replaces both the "representative's own date" and the "earliest member date" wording. Reason: the ranking prefers CD, so a representative's own date would read 1984 or later for an album first released in 1977. For that album the rule gives 1977-02-04.
- The release group's first-release date, the release group ID, and all member release IDs (with the representative flagged), barcodes, and catalog numbers are stored as attributes. \[Proposed\]
- The proposal records every candidate, which signal grouped them, the markers read and how each was classified, any marker propagation, the ranking reasons, and the reason for any ambiguity.

**Configuration, not code:** the audio-affecting vocabulary, the format preference, the region preference, and the status handling. Operators can ask for vocabulary additions. \[Proposed\]

**Tests.** The rule is tested against recorded MusicBrainz responses so tests use neither the live service nor its rate limit. All the earlier test cases still apply except these, which are superseded: TC-K3 (different lists are separate editions, not ambiguous), TC-K7, TC-K11, TC-K20, TC-K22, TC-K23, and TC-K24. They are replaced by TC-K26 to TC-K39 in sections 44 to 46. Two more:

- TC-K40. Given the same normalized title list with different recording lists, then the releases are one edition.
- TC-K41. Given a release with an audio-affecting marker and an unmarked release with the same recording list, then both are in the marked candidate edition.

**Fixtures.** Large recorded pages (hundreds of KB) stay in the scratch folder. Smaller trimmed versions are re-recorded with a lower `limit` for the repo. The Various Artists compilation has not been recorded.

**Progress log update (supersedes the "Next" lists in sections 20, 22, 23, 28, 29, and 32).** Decided this session after the commit contract and release dates: MusicBrainz API and rate-limit facts, low-frequency artist watching, verified response shapes, joint and featured credits and role inference, and this consolidated grouping rule. Next: choose and trim the fixtures for the repo; write the grouping rule as tests; the Phase 0 walking skeleton; resolve the trust boundary (C9) and check `GetUserId`, the authentication setup, the `Song` and join configuration, and the migrations in data-manager; the vetting dashboard; the policy numbers (freshness window, retry limits, priority, track limit); the Various Artists fixture; and the housekeeping items (commit the harvester code on a `feature/` branch, AGENTS.md after the design settles, and the NY DOL unemployment reopen, which remains time-sensitive and unconfirmed).

## 49. Handoff for the next session (written October 16, 2026)

The date stamps in earlier sections were approximate while the session ran across several days. Dates of recorded fixtures (October 12, 2026) are correct.

**Which sections are current**

- Requirements baseline and scope: sections 1 to 17, with later sections overriding them where they differ.
- Approval policy (auto-commit by default, limited to ID-verified matches, a per-request require-approval option) and artist resolution: section 29. Artist credits as ordered lists with roles: sections 30 and 40.
- Architecture: sections 24 to 26 (database per service, with SIM-1 as a labeled simulated requirement; stage inputs through the producer's API; dashboard data by API composition). Section 21 holds the entity sketch, and section 24 overrides its database decisions. Delivery phasing: section 28.
- Commit contract: section 31, with release dates and precision in section 32. data-manager findings: sections 26 and 27.
- Runs, requests, joining, and artist runs: section 23. Scheduled watching: section 37. MusicBrainz API, licensing, and rate-limit facts: sections 34, 35, and 36.
- **Grouping and representative-release rule: section 48 only.** Sections 33 and 43 to 47 are history.
- Verified response shapes and evidence: sections 38 to 42 and 44 to 47.

**Open decisions**

1. The trust boundary for who approved a commit (C9), and what `GetUserId` returns and how authentication is configured in data-manager.
2. Whether scheduled runs follow the auto-commit default (P6, proposed).
3. The watch list cap (SW9), the freshness window, retry limits, the on-demand versus bulk priority rule (N5), the maximum track count, and the description length limit.
4. Whether the resolver and review are one service, and whether the committer is its own service.
5. Cover art: whether the catalog stores image bytes, and the image variants (N11, N12).
6. The vetting dashboard: the grid component and which spreadsheet qualities users valued.
7. Whether the search engine for artist candidates stays a simple normalized name match.
8. The first-release marker vocabulary beyond remaster, mono, stereo, and remix, and verifying marker propagation (MP1) on more albums.

**Next steps, in order**

1. Choose and trim fixtures for the repo (small pages, with a lower `limit`). Record the Various Artists compilation. Check MusicBrainz's data license page before committing them.
2. Write the grouping rule (section 48) and the commit contract tests as code tests against the fixtures.
3. Build the Phase 0 walking skeleton against a stub catalog: the coordinator and one extractor with a real outbox, inbox, and relay loop.
4. Read data-manager's `GetUserId`, authentication setup, the `Song` and join configuration, and migrations, and settle C9.
5. Plan Phase 1 changes to data-manager (section 28) as small, additive, separately versioned releases.
6. Design the vetting dashboard once real candidate data has been seen in the review screens.

**Housekeeping.** The earlier harvester code goes on the `feature/initial-scaffolding` branch, pushed with a PR. The fixtures so far are in a scratch folder outside the repo. AGENTS.md is written only after the design settles and the solution has a real structure, and it points here. The NY DOL unemployment reopen was still unconfirmed at the start of the session.

**First task in the next session.** Collapse this document into a single current version that keeps only decisions, requirements, and open items, so a new reader does not have to follow supersession notes across 49 sections.
