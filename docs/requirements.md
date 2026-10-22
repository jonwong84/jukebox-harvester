# Jukebox Harvester: Requirements (consolidated)

Consolidated October 16, 2026 from the 49-section working draft. This version keeps only decisions, requirements, and open items. It has no supersession notes, progress logs, or dated session history. No design, no code. Intended home in the repo: `docs/requirements.md`. AGENTS.md should point here, not duplicate it.

**Legend**

- `[Decided]` was explicitly agreed.
- `[Proposed]` was suggested and not explicitly confirmed. Do not treat it as settled.
- `[Proposed*]` was untagged in the working draft and defaulted to Proposed during consolidation. Promote it to Decided only when you confirm it.
- `[Open]` needs a decision.

**Terminology.** "Auto-commit" means committed without a human approval step. Earlier drafts also said "bypass" and "auto-approved". This document uses "auto-commit" and "auto-committed", and the commit contract's `approval.mode` value is `Auto`.

Blocks headed "Facts" record verified behavior of external systems and are evidence, not decisions, so they carry no tags.

---

## 1. Purpose, scope, and learning goals

The Harvester brings music metadata from external sources into the Jukebox catalog as clean, trustworthy records, with minimal manual entry. It is a redesign, as microservices, of a predecessor monolith used in a previous job. Predecessor details stay conceptual.

- **R1.** Turn external music data into canonical Jukebox records with little retyping. `[Decided]`
- **R2.** Nothing becomes canonical on a guess. Either the match is verified, or an operator decides. `[Decided]`
- **Expected operators:** about ten. `[Decided]`
- **Scale assumptions** (invented, not confirmed): typical album 20 to 200 tracks; a first bulk import of 1,000 to 10,000 lookups. `[Proposed*]`
- **Operators authenticate through the Entra ID work planned for Azure.** An assumption to challenge. `[Proposed*]`

**Non-goals for v1**

- No playback, recommendations, or syncing to any streaming service. `[Proposed*]`
- A Plex-like streaming service is only an idea. The catalog should be a good thing to build on later (stable identifiers, a clean read boundary, no assumption that the Harvester is its only consumer), but nothing is designed for it now. `[Proposed*]`
- No second data source is built. Adding or replacing one must be cheap (R10, R11). `[Proposed*]`
- No multi-album spreadsheets. One file is one album. `[Proposed*]`
- No separate roles (R5). `[Decided]`
- No scheduled retry of unmatched records. `[Decided]`
- No spreadsheet round trip (export for review, import afterward), because edits made outside the system bypass the audit trail and the concurrency rules. `[Proposed*]`
- Spotify is excluded (R9). `[Decided]`

**Learning goals** (counted as requirements)

- **L1.** Use RabbitMQ with a real decision about what lives in the database versus what travels as messages. `[Proposed*]`
- **L2.** Separate services with clear owners of status. Chosen deliberately, and the real reason for database-per-service (section 11). `[Decided]`
- **L3.** Practice idempotency, retries, and a human checkpoint. The checkpoint is now the exception path. `[Proposed*]`

---

## 2. Users, roles, and audit

- **R3.** The system supports more than one user. `[Decided]`
- **R4.** There is one shared canonical catalog, the single source of truth for all metadata. `[Decided]`
- **R5.** There is one privileged role. Everyone who operates the Harvester (an "operator") can request extraction, join runs, review, approve, and correct. `[Decided]`
- **R6.** Every request, approval, correction, and rejection records who did it and when. This keeps the audit trail and makes a later role split a permissions change, not a redesign. Operators must therefore be authenticated. `[Decided]`
- **R7.** The catalog enforces its own write rules at its boundary: only an authenticated operator may write. The UI is not the only guard. `[Decided]`
- Listeners in a future streaming product are a different kind of user with no catalog write access. Out of scope. `[Proposed*]`
- Per-user data (who requested what, who watches which source, later listening history) lives outside the catalog and references catalog records. `[Proposed]`

---

## 3. Sources, extraction, and the album candidate

**Sources and contracts**

- **R8.** v1 sources are MusicBrainz and operator-submitted spreadsheets. `[Decided]`
- **R9.** Spotify is excluded. Its developer access requires Premium and restricts endpoints and quotas. A Spotify extractor could be added later behind the same contract. `[Decided]`
- **R10.** Each source is implemented behind an extraction contract (`IExtractor`). Replacing or adding a source means writing a new extractor, with no changes to the pipeline, the catalog, or the review step. `[Decided]`
- **R11.** The reference lookup ("what edition is this?") sits behind its own interface, with MusicBrainz as its only v1 implementation. Losing MusicBrainz must not require touching Transform. `[Decided]`
- **R12.** The pipeline must be runnable end to end with a fake in-memory extractor and no real source. This is the test that coupling has not crept in. `[Proposed]`
- **R13.** Prefer documented APIs or files over scraping. If scraping is ever needed, it sits behind the same contract. `[Decided]`
- **R14.** Cover art is attached when available and never blocks a record. The Cover Art Archive is the likely source. `[Proposed]`
- Rationale for R10 to R12: the predecessor's source-neutral record shape worked, but its extraction was welded to one proprietary scraping tool that was later deprecated, so the logic could not be moved, tested, or replaced. Producers must be owned code, behind a contract, testable with a fake. `[Proposed*]`

**Triggers**

- Extraction from watched MusicBrainz artists runs on a schedule (section 10, SW1 to SW10). `[Decided]`
- Any extraction can also be triggered on demand by an operator. `[Decided]`
- A scheduled run and a manual run are the same pipeline started by different triggers. They must not diverge. `[Decided]`

**Spreadsheet flow**

1. An operator creates an Album record in the dashboard (Title, Artist[], Year, Label, Edition). These fields are a starting set, not final. `[Decided]`
2. The operator attaches a spreadsheet: one row per track, for that one album. `[Proposed*]`
3. The extractor populates the album's track list from the file. `[Proposed*]`
4. The record proceeds to Transform. `[Proposed*]`

Rules:

- The operator-entered album fields are human-set and win over any album text in the file. Disagreements are flagged. `[Proposed*]`
- The extractor maps columns to track fields. Disc and position come from columns or a documented row-order rule. `[Proposed*]`
- A bad row flags that track. The album proceeds through the pipeline but does not commit until an operator handles the flag. `[Proposed*]`
- Attaching a corrected file to the same record replaces the track list. It does not append. `[Proposed]`
- An operator may supply a MusicBrainz ID. It is verified, never trusted blindly. `[Proposed*]`
- A spreadsheet album is a run with one source and one requester. Its source key is the album record itself. `[Proposed]`
- A year-only release date is acceptable on the Album record and in a spreadsheet (RD4). `[Proposed]`

**The album candidate (output of Extract)**

- **R15.** Every extractor emits one source-neutral, versioned album candidate with its tracks inside. Transform consumes only that shape. `[Decided]`
- The candidate carries: the source's name, the source's own ID or row reference, the fields it could read, the raw payload, and any problems found. `[Proposed*]`
- All fields except the claim of what it is are optional. Missing is a valid state, not an error. `[Proposed*]`
- Values are claims. An identifier supplied by a source is "this source says," and Transform decides whether to trust it. `[Proposed*]`
- A track carries its disc and position. A track also carries `numberAsPrinted` (a string, because vinyl uses values like "A1"). `[Proposed]`
- Problems attach at track level and album level. One bad track never discards the album. `[Proposed*]`
- Source vocabulary must not appear in the shape ("release-group," "playlist," "sheet"). Any such word indicates leakage. `[Proposed*]`
- Extract translates (parsing, pagination, authentication, column mapping, rate limits). Transform interprets (normalizing names, matching editions, detecting duplicates and conflicts). Extractors do not clean up values. `[Proposed*]`
- A source that only provides tracks groups them into albums inside its own extractor. `[Proposed*]`
- Raw payloads are kept, since explaining why a match was chosen requires them. `[Decided]`
- Artist-credit join phrases are passed through unchanged by the extractor (RI3). `[Proposed]`

---

## 4. Identity and editions

- **R16.** Each edition is its own catalog entity. Original, remaster, and deluxe are separate albums, and the same applies to songs. `[Decided]`
- **R17.** An edition is one record. Pressings and regional variants of the same edition collapse into it. `[Decided]`
- **R18.** The catalog generates its own identifier for every edition. External identifiers are optional attributes. An edition may have zero, one, or several MusicBrainz release IDs, with one marked representative. `[Decided]`
- **R19.** The catalog rejects a second edition that claims a MusicBrainz release ID already mapped to an existing edition. Identifiers are stored as (source, external ID) pairs so other sources can be added. `[Open]` The working draft tagged this "Decided / Proposed" without saying which half is which. Review and split.
- An edition can enter the catalog with no MusicBrainz ID, so independent music MusicBrainz does not know is not excluded. `[Decided]`
- **A10.** ISRC is a track-level attribute, not an identity. `[Decided]`
- Tracks are identified by disc and position within the edition. A recording ID, if any, is an attribute and not part of identity. ISRC is an attribute and is not unique. `[Proposed]`
- The release-group ID is stored as a plain attribute, so a future grouped view needs no migration. A release-group ID may be shared by several editions. `[Proposed]`
- Records carry a match status (matched, unmatched, re-matched), separate from approval status. `[Proposed]`
- The catalog's `isCompilation` is derived from a release group's secondary type Compilation. `[Proposed]`

---

## 5. Runs, requests, and joining

- **R22.** Extraction belongs to the source. Requests belong to users. `[Decided]`
- **R29.** A run is the work of bringing in one source. A request is one operator's (or the scheduler's) ask for that work. Many requests can attach to one run. `[Decided]`
- **R23.** A request for a source that already has a run in progress joins that run. Everyone sees the same progress and result. A scheduled trigger follows the same rule. `[Decided]`
- **R30.** Two requests join only if they have the same source key: (extractor, canonical subject). For MusicBrainz in v1 the key is (MusicBrainz, artist ID), with one scope. A scope is added to the key only if a second scope appears. `[Open]` The working draft tagged this "Decided / Proposed". Review and split.
- **R31.** A search is a lookup, not a run. The operator searches by name and chooses an artist, and the run is created for the chosen ID. Request text is never matched fuzzily for joining. `[Proposed]`
- **R32.** There is at most one active run per source key, enforced by a unique index in the database, not by workflow behavior. When two requests race, one insert fails and that request attaches to the winner. `[Proposed]`
- **R24.** Cancelling withdraws only that user's request (it sets the request's withdrawn time). The run stops when no active requests remain. Approved items stay. `[Decided]`
- A watched artist is a standing request for the same source key as a manual request, so scheduled and manual requests share one run. `[Decided]`
- A run records its source key, status, and start and finish times. A request records the run, the operator or scheduler, the trigger (manual or scheduled), the approval setting, when it was made, and when it was withdrawn if it was. `[Proposed*]`
- **R38.** Strictness applies from the moment a request joins. If a request requiring approval joins a run that started with auto-commit, albums already committed stay in the catalog, marked auto-committed so they can be reviewed later. Every later album waits for review. The joiner is told how many were already committed. `[Decided]`
- A request arriving right after a run finished returns that result instead of re-extracting. `[Proposed*]`
- Previously rejected items are not resurfaced as new. The catalog records "seen and declined." `[Proposed]`
- No duplicates must hold between different sources that share an album, not only between runs of the same source. `[Proposed*]`
- MusicBrainz's limit is shared by all runs combined, including scheduled and manual ones. On-demand requests take priority over bulk work. `[Proposed*]` The exact rule is open (N5).

---

## 6. Artist requests and credits

**Artist requests**

- **R33.** An operator can request an artist from MusicBrainz, meaning all of that artist's releases. `[Decided]`
- **R34.** An artist run covers only releases credited to that artist at the album level. Guest appearances and compilations where the artist appears only on some tracks are out of scope for artist runs in v1. They can be added through the spreadsheet path. This is a stated limitation, not a defect. `[Decided]`
- **R35.** Placeholder credits such as "Various Artists" cannot be requested or watched. The extractor refuses them with a reason. `[Proposed]`
- **R36.** Album-level and track-level artist credits are separate fields everywhere, including the spreadsheet and the candidate shape. For compilations, scoring and the fallback duplicate check reduce the weight of the artist signal and rely more on the track list. `[Proposed]`
- A joint album credited to two or more artists at the album level appears in each artist's run. The catalog's uniqueness rule and the duplicate check keep it to one edition. `[Proposed*]`
- **R37.** The MusicBrainz extractor emits one candidate per MusicBrainz release. Transform groups releases into editions (section 9). The extractor does not group. `[Decided]`
- Each release is stored as an immutable raw extraction tied to the run. Its staging-album reference is nullable until grouping assigns it. `[Proposed]`
- An artist-run album does not exist until grouping, so it starts at Resolving. During extraction the run's own status shows progress. `[Proposed]`
- Grouping must be safe to repeat. A release arriving in a later run attaches to the existing staging or committed edition and never creates another. `[Proposed]`
- A doubtful grouping (members disagree, for example on track count) goes to review even when auto-commit is the default. `[Proposed]`
- Artist runs can produce dozens of review items, so the dashboard offers a batch action such as approving all clear winners together. `[Proposed]`

**Artist credits**

- **CR1.** Every album and every track carries an ordered list of artists. Each entry has a position and a role (Primary or Featured), plus the credit as printed kept as a display value. `[Decided]`
- **CR2.** Collaboration entities are deferred. When they come, a collaboration is an entity only when it has an identity of its own (a duo, a band, a long-running project), and everything else stays a list. An operator decides, and Transform never infers it. `[Decided]`
- **CR3.** Other roles (remixer, producer, composer) and group-member relationships are non-goals for v1. A group is just an artist. `[Proposed]`
- **CR4.** Transform never splits a credit string on its own, because "&" and "," are ambiguous. Either the operator enters the artists as a list, or the whole string goes through artist resolution as one candidate with the split offered as an alternative. `[Proposed]`
- **CR5.** A "(feat. X)" inside a track title is left as given. Moving the guest into the artist list goes through review. `[Proposed]`
- **CR6.** Artist sets are compared order-insensitively for scoring and duplicate checks, and the artist signal is weighted less when there are many artists. `[Proposed]`
- **CR7.** Auto-commit still requires every artist in the list to be resolved by an ID (P4). A collaboration with one unresolved guest goes to review. `[Decided]`
- **RI1.** MusicBrainz states no roles, so roles are derived from the join phrases by position. Entries are Primary until a featuring phrase appears (`feat.`, `featuring`, and similar), and every later entry is Featured, including entries joined by `&` after it. `[Proposed]`
- **RI2.** The phrase vocabulary is configuration. A join phrase outside the known set (for example "with", "vs.", "x", a slash) leaves roles unresolved and sends the record to review. Only `&` and `feat.` have been seen. `[Proposed]`
- **RI3.** Inference happens in Transform. The extractor passes join phrases through unchanged. `[Proposed]`
- **RI4.** A record whose roles were inferred and not confirmed by an operator can still auto-commit only if every phrase is in the known set. `[Proposed]`
- The credit as printed is the concatenation of each credit entry's name and join phrase. `[Proposed*]`

---

## 7. Approval and auto-commit

- **P1.** Auto-commit is the default. A record that meets every auto-commit criterion is committed without a human. Anything else goes to review. `[Decided]`
- **P2.** Auto-commit is limited to ID-verified matches. Name-based matches, even with a high score, never auto-commit. This is the strict starting point and can be loosened later with evidence. `[Decided]`
- **P3.** A request can ask for approval. When it does, every item waits for an operator, including ones that would have auto-committed. The strictest request wins when requests join a run, from the moment of joining (R38). `[Decided]`
- **P4.** Auto-commit criteria (all must hold): every artist is resolved by an exact external ID or an existing catalog ID, never by name alone; the edition is verified by ID, or built from a single release; there are no duplicate warnings, conflicts, or invalid tracks; if several releases were grouped into one edition, the group is clear under section 9; and the request did not ask for approval. A spreadsheet album qualifies only if its supplied ID is verified and its artists resolve by ID. `[Proposed]`
- **P5.** An auto-committed record is marked as such, with the requester recorded, and `approvedBy` is empty, so after-the-fact spot checks are possible. `[Proposed]`
- **P6.** Scheduled runs follow the same default. A watched source may be set to require approval. `[Proposed]` Needs confirmation (open item 2).
- **P7.** Before auto-commit is enabled on scheduled runs, there must be a "recently auto-committed" view (P5) and a basic way to merge or remove a wrong edition (F5, F7). Field-level provenance (C8) can stay deferred. `[Proposed]`
- Auto-commit skips the approval step only. It never skips validation, duplicate checks, ambiguity, or the catalog's uniqueness rule. `[Decided]`
- Ambiguity always goes to an operator, even when auto-commit is the default. `[Proposed*]`
- An auto-commit run finishes by reporting how many items still need attention. `[Proposed*]`

**Artist resolution**

- **P8.** Each artist in a commit request carries a resolved identity in one of three forms: `Existing` (a confirmed catalog artist ID), `External` (the catalog finds or creates by source and ID), or `CreateNew` with a name (explicitly chosen by an operator). The catalog never matches by name on its own and never guesses. `[Decided]`
- **P9.** Artist identity is the catalog's own `ArtistId`. No unique SortName is required. Names need not be unique. `[Decided]`
- **P10.** The review screen shows the top few catalog artists with their scores, pre-selects a clear winner, and lets the operator pick another or choose "new artist." A weak top score or a close runner-up means ambiguous, with nothing pre-selected. A short disambiguation text is kept as a display field. `[Proposed]`
- The artist-candidate rule needs both a minimum top score and a gap to the runner-up (the search score alone is not enough: two different artists scored 100 and 99 for one query). `[Proposed*]`
- Matching by name uses a normalized name column and a contains or prefix match to produce candidates. A real search engine is deferred. `[Open]`

---

## 8. Duplicates, bad data, failures, refresh, and provenance

**Duplicates and bad data**

- An exact match on an existing identifier is never an insert. It is either skip or enrich. `[Proposed*]`
- A probable match with no identifier match goes to an operator, even on auto-commit. Remasters are not duplicates. The probable cases are the same edition spelled differently, and two MusicBrainz IDs for one real release. `[Proposed*]`
- Existing duplicates need a merge tool: keep both records' identifiers and redirect anything that referenced the loser (F7). `[Proposed]`
- Invalid items (missing required fields, unparseable identifier, track with no album) never reach the catalog, with or without auto-commit. They go to a needs-attention list with a reason. `[Proposed*]`
- Doubtful items (low-confidence match, contradiction with existing data) go to operator review. `[Proposed*]`
- Human-set fields are never overwritten. A significant disagreement is shown as a proposed change, not applied silently. `[Proposed*]`
- Unmatched editions get a fallback duplicate check on normalized artist, title, edition, and year. It warns the operator and does not silently reject. Track lists from the spreadsheet strengthen it. `[Proposed*]`
- **A11.** After an operator's pick, the duplicate check runs again before approval is accepted, so a duplicate error appears at decision time and not at commit. The commit-time uniqueness rule stays as the final guard. `[Decided]`
  - An exact conflict (the chosen MusicBrainz ID is already mapped to an existing edition) rejects the pick with an error. The record stays AwaitingReview. `[Decided]`
  - A probable duplicate shows a warning. The operator confirms it is a distinct edition, or chooses to enrich or merge. `[Proposed]`

**Failures and scale**

- **R25.** One bad item never stops the batch. `[Decided]`
- **R26.** Temporary outages are retried automatically, a limited number of times. Persistent failures surface with a reason. `[Decided]` The limits are open (open item 3).
- **R27.** Re-running a failed or interrupted run is safe: no duplicates, no lost approvals. `[Proposed*]`
- Losing one source degrades a run and does not stop it. `[Proposed*]`

**Refresh**

- **R28.** An operator can request a refresh of existing records. It updates in place, never creates a duplicate, and never overwrites human-set fields. `[Decided]`
- Unmatched editions are re-checked only on manual refresh, for one record or in batch. `[Decided]`
- A refresh that finds a likely match proposes the ID. An operator confirms it, even on auto-commit. `[Proposed*]`
- Operators get an unmatched filter showing how long each record has been unmatched and when it was last checked. `[Proposed*]`

**Provenance and audit**

- Each field remembers its supplier: a source, or a person (who and when). Field-level provenance in the catalog is Phase 2 (C8). `[Proposed*]`
- An operator's attestation on an unmatched edition is recorded as human-set. `[Proposed*]`
- The first decision on a review item wins and is recorded. A later change is an explicit new edit. `[Proposed*]`
- The catalog must outlive its sources. Everything it holds is stored as its own data with provenance, so losing MusicBrainz does not damage existing records. `[Proposed*]`

---

## 9. Matching, grouping, and the representative release

This section is the only current grouping rule. It is built from fixtures of real MusicBrainz data (one artist's early catalog, a 16-release album, and an 81-release album with remasters and deluxe sets). These are a few samples, so the vocabulary and defaults are starting points to be tested in code. Each MusicBrainz release arrives as one candidate (R37). Grouping is Transform's job.

**Matching (before grouping)**

1. If an ID was supplied, fetch that release and verify it against the record. `[Proposed*]`
2. Otherwise search MusicBrainz `release` by artist and title, with query special characters escaped. `[Proposed*]`
3. Fetch details for the top few candidates and cache responses. `[Proposed*]`
4. Score each candidate against the record and its tracks. Signal strength, strongest first: track count, titles, and durations; title and artist (order-insensitive artists); year; label; edition text. Edition text is a hint or tiebreaker, never a filter. `[Proposed*]`
5. Group survivors into editions and pick a representative release (steps 1 to 4 below). `[Proposed*]`
6. Emit a proposal listing the candidates considered, their scores, and the winner if any. Outcomes: clear winner (pre-checked for approval), ambiguous (side-by-side choice by an operator), no match. `[Proposed*]`

**Step 1: filter**

- Releases whose status is bootleg, pseudo-release, withdrawn, or cancelled are set aside, and the proposal says so. Status is compared without regard to case. `[Proposed]`
- Promotion releases stay but rank low. A release with no status stays in, is flagged in the proposal, and ranks below Official and Promotion. `[Proposed]`

**Step 2: group into editions**

- Track titles and marker text are normalized per character before any comparison (TN1): Unicode compatibility normalization, typographic quotes, apostrophes, and dashes folded to plain characters, case folded, and whitespace collapsed. Raw text is never compared (TN4). Trailing parentheticals are never stripped from track titles, because "(live)", "(remix)", and "(radio edit)" can mark different recordings. `[Decided]`
- Track lists are flattened across media, so the number of discs or vinyl sides never splits an edition (G1). `[Decided]`
- Album titles are compared after removing trailing parenthetical text. The removed text is read as marker text (G2). Releases must have the same base album title to be grouped. `[Decided]`
- **Two releases are the same edition when their normalized title lists are equal, or when their ordered recording-ID lists are equal (RC2).** Titles fail on annotation differences, and recording IDs fail on pressing and mastering differences, so either match is enough. Recording IDs are never the only primary signal. `[Decided]`
- A shared barcode or disc ID supports grouping and never decides it. A missing or different barcode never splits a group (G5). Retailer and distribution wording never decides anything (TN3). `[Decided]`
- Different normalized title lists with different recording lists mean different editions (TN2). `[Proposed*]`
- **Re-sequenced releases (RS1).** Releases that hold the same songs in a different order are never merged. A re-sequenced group is classified as ambiguous and goes to an operator, and the group it is related to is not made ambiguous by it. `[Decided]`
- When several groups hold the same songs in different orders, the largest group is the reference and keeps its own classification. Every other group in the relation is ambiguous. If no group is strictly largest, all of them are ambiguous. `[Proposed]`
- A group built this way in which some pair of members matches under neither signal is ambiguous, because a bridging release could chain two different editions together. `[Decided]`

**Step 3: classify each group**

- **Marker vocabulary (G6).** Marker text comes from the release's disambiguation and the removed album-title parentheticals. Only wording that implies different audio is audio-affecting, matched by stem: the starting set is remaster (also remastered), mono, stereo, and remix. It lives in configuration and grows with evidence. Packaging or pressing wording (limited, exclusive, a retailer's name, "made in Japan", "textured cover"), "reissue", and edition names (deluxe, expanded, anniversary) are ignored as markers. Edition names matter only through the track lists, which already separate those editions. Free text outside the vocabulary is ignored. `[Decided]`
- **Marker propagation (MP1).** If any member of a recording-list group carries an audio-affecting marker, every member of that recording-list group is treated as marked. This catches a remaster whose disambiguation is empty. `[Decided]` It rests on one real example and is to be verified on more albums (open item 8).
- **Split (G7).** Marked members are split out of their group and do not make the rest ambiguous. The unmarked members form a clear group. The marked members form their own candidate edition, which goes to an operator, because a remaster is a separate edition by policy. `[Decided]`
- **Clear:** all members share a normalized title list or recording list, have the same base album title, no pair matches under neither signal, and none is marked. Clear groups can auto-commit (P4). `[Proposed*]`
- **Ambiguous (goes to an operator):** the marked candidate edition from the split; a re-sequenced group that is not the reference (RS1); a group with a pair that matches under neither signal; and, on the spreadsheet path, a group that fits the operator's record poorly. `[Proposed*]`
- Residual risk, accepted: an unmarked remaster with no marker and no shared recording list will look like an ordinary release. Mitigations are operator review of marked groups and the "recently auto-committed" view (P5, P7). `[Proposed*]`

**Step 4: choose the representative,** deterministically, in this order: Official over Promotion over no status; complete data (a date, a label, a duration on every track); format (Digital Media, then CD, then others, with names matched tolerantly because formats include `12" Vinyl` and similar); region (worldwide, which is `XW`, then a configured list); earliest date, compared at the coarser of the two precisions; and the lowest release ID only to break an exact tie. `[Proposed]`

**What the edition takes from its releases**

- The representative supplies the title, the label (the first entry of the release's label list), and the track list. `[Proposed*]`
- **The edition's date is the earliest member date, using the most precise date within the earliest period (G3).** The earliest year among the members is found first, and within that year the earliest full date is used if one exists, with its own precision. Reason: the ranking prefers CD, so a representative's own date would read 1984 or later for an album first released in 1977, where this rule gives 1977-02-04. `[Decided]`
- The release group's first-release date, the release group ID, and all member release IDs (with the representative flagged), barcodes, and catalog numbers are stored as attributes. `[Proposed]`
- The proposal records every candidate, which signal grouped them, the markers read and how each was classified, any marker propagation, the ranking reasons, and the reason for any ambiguity. `[Proposed*]`
- **Configuration, not code:** the audio-affecting vocabulary, the format preference, the region preference, and the status handling. Operators can ask for vocabulary additions. `[Proposed]`

**Not specified in this rule** (open item 14): how candidates are scored against the operator's record on the spreadsheet path, beyond the rule that a poor fit is ambiguous.

---

## 10. MusicBrainz constraints and extraction behavior

**Facts** (verified against MusicBrainz documentation and recorded responses; recheck the rate-limit page before relying on it, since it says its rules are "as of 2012-01-08" and may change)

- JSON via `fmt=json` or an `Accept` header. No API key. A meaningful User-Agent is required. Non-commercial use is free, and commercial use needs a MetaBrainz plan.
- The rate limit is about one request per second per source IP, not per application. Exceeding it is a cliff: all requests from the IP get `503 Service Unavailable` until the rate drops. No `Retry-After` is documented. A blank or default User-Agent counts as anonymous and is throttled. A .NET `HttpClient` sends none unless set.
- MusicBrainz asks applications not to run at fixed times such as the top of an hour, and not to poll for changes.
- Three request types: lookup (`/ws/2/<entity>/<MBID>?inc=`), browse (`?<linked-entity>=<MBID>&limit=&offset=&inc=`), and search (`?query=&limit=&offset=`). Only search works without an MBID.
- Browse paging: default `limit` 25, maximum 100. A release browse page holds at most about 500 tracks in total, so a page may hold fewer releases than `limit`. The next offset must be the previous offset plus the number of releases actually returned. Lookups cap linked entities at 25.
- A release browse can include track lists (`inc` of `artist-credits`, `labels`, `recordings`, `release-groups`, `media`, `discids`, `isrcs`), so one request returns many releases with tracks. A browse with `limit=1` returns the full `release-count`. `release?artist=` returns releases credited to the artist at the release level. `track_artist` returns guest and compilation appearances, which R34 excludes.
- Release statuses: official, promotion, bootleg, pseudo-release, withdrawn, cancelled. The response capitalizes them (`Bootleg`).
- An invalid ID returns 400 `{"error":"Invalid mbid."}`. A well-formed ID that does not exist returns 404 `{"error":"Not Found"}`. A search with an unbalanced quote returned 200 with loosely matched results and no error.
- Artist search items have `id`, `name`, `score`, `disambiguation`, and an optional `type`.
- Release fields seen: `artist-credit`, `asin`, `barcode`, `country`, `cover-art-archive`, `date`, `disambiguation` (empty string when absent), `id`, `label-info`, `media`, `packaging`, `quality`, `release-events`, `release-group`, `status`, `text-representation`, `title`. Dates may be a full date, year and month, or year only. Country can be `XW` (worldwide). Format names contain quote characters (`12" Vinyl`).
- Artist credit is an ordered array. Each entry has an `artist` object, a credited `name`, and a `joinphrase`. There is no role field.
- Release group: `id`, `title`, `primary-type`, `secondary-types`, `first-release-date`, `disambiguation`, `artist-credit`.
- Track: `number` (a string), `position` (an integer), `length` in milliseconds (may be missing), `title`, `artist-credit`, and a `recording` object with its own `id`. ISRCs appear only when requested on the lookup. Disc IDs live under each medium.
- Core data (artists, releases, release groups, recordings, labels, mediums, and so on) is CC0. Supplementary data (annotations, tags, ratings, derived statistics, search indexes, edit history) is CC BY-NC-SA 3.0.
- Remaster wording appeared only in release disambiguation text in the samples, never in track titles.

**Decisions**

- **PG1.** The client advances the browse offset by the number of releases actually returned, and stops when the offsets sum to `release-count`. `[Decided]`
- **RL1.** The shared limiter paces calls below the limit with a margin (for example one call per 1.2 seconds), counted across every worker and every trigger that shares an egress IP. `[Proposed]`
- **RL2.** A `503` is treated as Unavailable (transient). The first `503` trips a circuit breaker: the shared limiter pauses all workers for a back-off period (growing with repeated failures) and slows down afterward. Retrying immediately is never done. `[Proposed]`
- **RL3.** Scheduled MusicBrainz work never runs at a fixed clock time or at the top of an hour. Start times are jittered and calls are spread out. `[Proposed]`
- **RL4.** The client always sends a User-Agent of the form `JukeboxHarvester/<version> ( <contact email> )`. A missing User-Agent is a startup error. `[Proposed]`
- **RL5.** Fixture recording and any manual testing follow the same pacing and send the same User-Agent. `[Decided]`
- **LI1.** Fixtures use core entity fields only. They never request tags, genres, ratings, or annotations. Search fixtures are kept minimal and search scores are never stored as data. `[Proposed]`
- **LI2.** In v1 the catalog stores only core MusicBrainz fields. Tags, genre associations, ratings, and annotations are not stored. `[Proposed]`
- **LI3.** MusicBrainz is credited in the application and documentation as a courtesy. `[Proposed]`
- The artist-run extractor browses `release?artist=` with the `inc` values above and pages with PG1. `[Proposed]`
- Withdrawn and cancelled releases are set aside in Transform (section 9, Step 1), not in the extractor. `[Proposed]`
- The extractor keys on status codes and uses error messages only for logs. An invalid ID is our bug and is never retried. A 404 is a data outcome. `[Proposed*]`
- Use the release's `cover-art-archive` data to decide whether to fetch artwork at all. `[Proposed]`
- Raising MusicBrainz throughput would require hosting a local copy of the published data (a mirror or the Live Data Feed). This is deferred. R11 lets it replace the lookup implementation without touching the pipeline. `[Proposed*]`

**Scheduled watching of artists** (low-frequency, low-cost watching) `[Decided]`

- **SW1.** The coordinator owns a watch list of artists, each with its MusicBrainz ID, the last known release count, and the time of the last check. `[Proposed]`
- **SW2.** Each watched artist is checked at most about once a week, at jittered times, through the shared limiter at a lower priority than on-demand work (N5). `[Proposed]`
- **SW3.** The check is one small browse request with `limit=1` using the same filters as the artist run. It compares `release-count` with the stored count. `[Proposed]`
- **SW4.** If the count rose, a run starts with the Scheduled trigger for the same source key as a manual request (R30), or joins an active run. It then follows the normal pipeline and approval policy (P1 to P6). `[Proposed]`
- **SW5.** When an artist is added to the watch list, the first import is a normal request. The stored count is set when that run finishes. `[Proposed]`
- **SW6.** After a run completes, the stored count is updated to the count the run saw. `[Proposed]`
- **SW7.** A count that fell is not acted on automatically. It is logged and flagged for an operator. `[Proposed]`
- **SW8.** The count detects new releases, not edits to existing ones. A net-zero change is missed. A manual run covers this. `[Proposed]`
- **SW9.** The watch list is kept small. The maximum size is `[Open]` (open item 3).
- **SW10.** A 503 during a check trips the circuit breaker (RL2) and the check is rescheduled later, never retried immediately. `[Proposed]`

---

## 11. Architecture

- **A1.** In-flight records live in the Harvester's own stores. Each Harvester service owns its own database, and none of them is the catalog. jukebox-data-manager receives only approved records, through its API, at the final commit step. `[Decided]`
- Reasons for database-per-service, stated honestly: **L2 is the real reason.** On engineering merit alone, at about ten operators and one developer, a shared staging database would be simpler. The accepted costs are sagas, an outbox in every service, no cross-service joins, and an eventually consistent dashboard. **SIM-1 (a simulated requirement, labeled as such):** extraction, review and audit, and cover art are owned by different teams that deploy and change schemas independently. It exists to give the patterns a constraint and is not a real constraint. Three reasons are real: cover art needs blob storage (N12), extractors must be replaceable, and raw payloads (bulky, shorter retention) differ from the audit trail (kept longer). Operator count is not an argument. `[Decided]`
- **A3.** No cross-database access, between Harvester services or between the Harvester and the catalog. Services reach each other's data only through APIs or messages. No foreign key crosses a boundary. Each service has its own credentials and migrations. `[Proposed]`
- **A4.** One owner per piece of state, and one writer per status field. A coordinating service is the only writer of lifecycle status. Stage services report outcomes and the coordinator applies transitions, so the auto-commit rule lives in one place. `[Proposed]`
- **A5.** The database holds the truth. Messages are signals that carry IDs, not data. Review is a database state that the dashboard queries. `[Proposed]`
- **A6.** Reliability techniques: transactional outbox, idempotent consumers, and a reconciliation sweep for records stuck too long. `[Proposed]`
- **A7.** Evidence and workflow are separate. A raw extraction is immutable (source, source reference, shape version, time, payload). A staging album is mutable. A refresh adds a new raw extraction and never overwrites an old one. `[Proposed]`
- **A8.** Status is split into separate fields: lifecycle, match status, and approval mode. Cover art status belongs to the cover art stage. `[Proposed]`
- **A9.** Staging tables use optimistic concurrency (a row version) within each service's own tables. `[Proposed]`
- **A13.** Stage services write their own output data and report an outcome. The coordinator writes only lifecycle status and handles small outcome messages, so it never touches bulk data. `[Decided]`
- **A14.** Each service writes its output and its outbox row in one local transaction. A relay publishes outbox rows to RabbitMQ with at-least-once delivery. Consumers are idempotent, checking both the record's current state and whether the message ID was already processed. `[Proposed]`
- **A15.** The coordinator is an orchestrator (a saga). It issues commands, receives outcomes, validates each transition against the current state, and compensates instead of rolling back. Transition rules live in one state-machine class that is unit-tested without RabbitMQ. `[Proposed]`
- **A16.** RabbitMQ settings: durable queues, persistent messages, publisher confirms, manual acknowledgement after the database commit, prefetch limits, dead-letter queues, and retries with increasing delay up to a limit. A dead-lettered message moves the record to NeedsAttention with a reason. `[Proposed]` The retry limit is open (open item 3).
- **A17.** A periodic sweep re-sends commands for records stuck in active states (Extracting, Resolving) too long. It excludes AwaitingReview and NeedsAttention. `[Proposed]`
- **A18.** Message order is not relied on. Handlers check state, and a stale write loses the race harmlessly. `[Proposed]`
- **A19.** The outbox, relay, and inbox logic is built once as a shared, tested library. `[Proposed]`
- **A20.** A correlation ID is set when a request begins and appears on every message and log line. `[Proposed]`
- **A21.** Build a walking skeleton first: the coordinator and one extractor with a real message loop (outbox, inbox, relay). `[Proposed]`
- **A22.** A stage fetches its input from the producing service's API. Messages carry only IDs, plus small outcome details. Large payloads are never placed in messages. `[Decided]`
- **A23.** Each service exposes a read API for the data it owns, including a batch get. Consumers tolerate the producer being briefly down by retrying with limits and never by reading the producer's database. `[Proposed]`
- **A24.** The dashboard shows data held in several services by API composition. A read model is deferred and added only if composition proves painful. `[Decided]`
- **A25.** The coordinator owns the list of records and their state, so the list comes from it, with paging. The dashboard backend fetches details for only the records on the current page, using batch gets. If a service is down, the screen shows what it has and marks the missing part. `[Proposed]`
- **A26.** Whether the coordinator keeps a few copied display fields (title, primary artist), updated from events, to support filtering and sorting on data it does not own. `[Open]`
- **Lifecycle states:** Created, Extracting, Resolving, AwaitingReview, Approved, Committed, NeedsAttention, Rejected, Cancelled. AwaitingReview (a choice between valid options) and NeedsAttention (something broken) are separate queues. `[Proposed*]`

**Data ownership (starting map)** `[Proposed*]`

| Service | Owns |
| --- | --- |
| Coordinator | Runs, requests, lifecycle status, match status, approval mode, its outbox |
| Extractor(s) | Raw extractions, parsed tracks |
| Resolver | Grouping, proposals, scores |
| Review | Operator decisions, audit of who decided |
| Cover art | Image records, image storage, cover art status |
| Catalog (jukebox-data-manager) | Canonical editions; holds no workflow status |

The target is four or five services, starting coarse. Whether the resolver and review are one service, and whether the committer is its own service, are open (open item 4).

---

## 12. Commit contract v1 and data-manager changes

**Decisions on the hand-off**

- **C1.** Commit is a stage like any other. The coordinator issues a commit command, a thin committer assembles the payload from the services that own the data (A22), calls data-manager, and reports an outcome. The coordinator owns the move to Committed. The committer decides nothing about readiness. `[Proposed]`
- **C2.** One atomic endpoint in data-manager. A single request carries the edition, its artists (with external IDs), and all its tracks. The catalog does everything in one database transaction and returns either the finished edition or a clear conflict. `[Decided]`
- **C3.** The staging album's ID is the idempotency key. A retry returns the edition already created. A unique constraint on the key handles two simultaneous requests. `[Proposed]`
- **C4.** Outcome mapping: created is success. Already exists with the same key is success. Already exists with a different identity is a conflict (NeedsAttention). Invalid goes to NeedsAttention with the reason. Timeout or server error is transient: retry with limits, then NeedsAttention. The catalog's duplicate check is the final authority, and the check at pick time (A11) is advisory. `[Proposed]`
- **C5.** Create and update are different operations. An update carries the version it read, so a concurrent change is rejected. An update never overwrites a field a person set. Phase 2. `[Proposed]`
- **C6.** Cover art is not part of commit and never blocks it. `[Decided]`
- **C7.** Albums commit one at a time, as each is approved. The committer respects backpressure from the catalog. `[Proposed]`
- **C8.** The catalog stores provenance per field (source or person, who, when). It requires changes in data-manager. Phase 2. `[Proposed]`
- **C9.** Trust boundary. The catalog authenticates the committer as a service, and the payload names the approving operator, or marks the album auto-committed with its requester. The catalog trusts that account. The alternative, carrying the operator's identity token, runs into token expiry while commands wait in queues and into scheduled work with no operator present. Whether to accept the weaker model is a security decision. `[Open]`
- **C10.** A commit request is small (tens of tracks, hundreds for unusual box sets). The catalog sets a documented maximum number of tracks and rejects larger requests as invalid. The committer uses an explicit timeout and treats a timeout as transient. The catalog's writes are batched in one transaction. `[Proposed]` The exact limits are open (open item 3).

**The contract** (draft as a whole, so `[Proposed]`, except where an item below carries a prior decision). It carries data and no decisions: whether a record may auto-commit is decided by the coordinator before the request is sent, never by the catalog.

**Operation.** `POST /api/Albums/commit`. The existing `POST /api/Albums` is unchanged. The endpoint requires authentication (`[Authorize]`). Which policy or role the committer must hold depends on C9.

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

The track shape also gains `numberAsPrinted` (section 3). `[Proposed]`

- `commitId` is the staging album's ID and the idempotency key. `correlationId` is for tracing and is logged. Neither affects the content hash, nor does `approvedAt`. `[Proposed]`
- `approval.mode` is `Operator` (requires `approvedBy`) or `Auto` (`approvedBy` empty, `requestedBy` required). `[Proposed]`
- An artist `identity` takes exactly one form (P8): `Existing` (an `artistId` the catalog must already hold), `External` (`source` and `id`; the catalog finds the artist by that pair or creates it using `name`), or `CreateNew` (an explicit operator choice; `name` required). A missing or unrecognized form is invalid. The catalog never matches by name. `[Decided]`
- A reused external artist is not renamed. Human-set fields are never overwritten. `[Proposed]`
- `role` is `Primary` or `Featured`. Positions are 1-based and unique within a list. The same artist cannot appear twice in one list. `[Proposed]`
- A `release` external ID may belong to only one edition. A `release-group` ID may be shared. Zero external IDs is valid (an unmatched edition). `[Proposed]`

**Release dates**

- **RD1.** The contract carries `releaseDate` as text in one of three forms: `YYYY`, `YYYY-MM`, or `YYYY-MM-DD`. Anything else is invalid with the code `InvalidReleaseDate`. A release date is optional. `[Decided]`
- **RD2.** The catalog stores a date and a precision (Year, Month, or Day) in two columns. For a partial date the date holds the first day of the period and the precision says how much is real. Responses and display show only what the precision supports. `[Proposed]`
- **RD3.** Matching and duplicate checks compare dates at the lower of the two precisions. A year-only date matches any date in that year. `[Proposed]`
- **RD4.** A spreadsheet, or the operator who creates the album record, may enter a year only. `[Proposed]`
- The database is empty, so the date columns are added as new and nothing needs migrating. `[Decided]`

**Success response.** `201 Created` with `outcome: "Created"`, or `200 OK` with `outcome: "AlreadyCommitted"` for a replay with identical content. Body: `albumId`, `trackIds` (each with disc, position, and `songId`), and `resolvedArtists` (each with the identity as sent, its `artistId`, and whether this commit created it). `[Proposed]`

**Error response.** A standard problem-details body plus `kind`, a stable machine-readable `code`, a safe `message`, `errors` (a list of `path`, `code`, `message`), and where relevant `conflictingAlbumId` and `retryAfterSeconds`. The committer branches on `kind` and `code` and never on message text. Full exceptions go only to the catalog's logs. `[Proposed]`

| kind | HTTP | codes (initial set) | Committer outcome |
| --- | --- | --- | --- |
| Invalid | 400 | MissingTitle, TitleTooLong, ArtistNameTooLong, NoTracks, TooManyTracks, InvalidTrackPosition, DuplicateTrackPosition, ArtistIdentityMissing, ArtistNotFound, DuplicateArtistInList, InvalidExternalId, InvalidApproval, InvalidReleaseDate, UnsupportedContractVersion | NeedsAttention with the reason |
| Conflict | 409 | ExternalIdAlreadyMapped (with `conflictingAlbumId`), IdempotencyKeyReused (same key, different content) | NeedsAttention |
| InProgress | 409 + Retry-After | CommitInProgress | Retry |
| Unavailable | 503 or 429 + Retry-After | ServiceUnavailable | Retry with limits |
| Unexpected | 500 | Unexpected | Retry up to the limit, then NeedsAttention |
| (no kind) | 401 or 403 | | NeedsAttention and alert |

The same `kind` field is added to the result types of the existing endpoints (F1), keeping the `Success` flag. `[Proposed]`

**Idempotency** `[Proposed]`

1. The catalog stores each `commitId` with a hash of the canonical content and the resulting album ID, in the same transaction as the album. Records are kept permanently.
2. A replay with the same ID and hash returns the original result (200, `AlreadyCommitted`).
3. The same ID with a different hash is `IdempotencyKeyReused` and is never re-applied.
4. While a first request holds the key, a second gets `CommitInProgress`. On a unique-constraint violation the loser re-reads: committed means replay, otherwise in progress.
5. Two commits racing to create the same external artist are protected by a unique index on (source, external ID). The loser re-reads and reuses the artist.

**Atomicity.** One transaction creates or reuses the artists, the album, its artist links, description, external IDs, the songs and their artist links, and the idempotency record. Nothing is written unless all of it succeeds. `[Decided]` (C2)

**Validation limits.** Title and artist name at most 200 characters. At least one track. Disc and position at least 1, unique as a pair. Every track has a title. Over-limit requests are invalid, not truncated. `[Proposed]` A maximum track count, a description length limit, and a duration rule are `[Open]` (open items 3 and 12).

**Supporting reads** (needed before any commit) `[Proposed]`

- `GET /api/Albums/by-external-id?source=&kind=&id=`
- `GET /api/Artists/by-external-id?source=&id=`
- `GET /api/Artists/candidates?name=` (top few artists with a score and a short disambiguation text; starts as a normalized name and contains or prefix match)
- `GET /api/Albums/possible-duplicates` (fallback check; advisory only)
- `GET /api/Commits/{commitId}` (the stored outcome or 404, so the committer can check what happened after a timeout)

**Versioning.** `contractVersion` is an integer. The catalog rejects an unsupported version as invalid. Adding optional fields or codes does not bump it. `[Proposed]`

**Mapping to data-manager (starting point)** `[Proposed]`

- `Album` becomes the edition: add label, edition text, credit as printed, approval mode, approved by, requested by, approved at, and the commit ID.
- An album external-ID table with a unique (source, kind, ID) pair for kind `release` only.
- Album artists gain position and role. Songs gain disc, ISRC, credit as printed, and a unique (album, disc, position). A song artist table with position and role replaces the single artist ID.
- An artist external-ID table with a unique (source, ID) pair.
- A commit record table with a unique commit ID, a content hash, and the album ID.

**The stub catalog (Phase 0).** An in-memory implementation of the same contract, including idempotency, conflicts, and artist reuse by external ID, with a test-only way to force each outcome (invalid, conflict, in progress, unavailable, unexpected). `[Proposed]`

**Contract tests.** One shared suite run against both the stub and the real endpoint. `[Proposed]` Whether the update-related tests belong in the Phase 1 suite is open (open item 13).

**data-manager: facts from code review** (not yet seen: `GetUserId` and the authentication setup, the `Song` and album-artist join configuration, migrations, the PUT and DELETE paths, the Artists and Songs controllers; anything about those is an assumption)

- The API has no external identifiers on albums or artists, no disc number, label, edition text, or ISRC, no idempotency key, no update version, no atomic create of an album with its songs, and no field provenance or merge operation. A song has one artist ID. Artists are referenced by integer ID only.
- An unknown artist ID in a create returns an error naming the missing IDs. `AddAsync` saves the album, artist links, and description in one `SaveChangesAsync`.
- The manager overwrites the request body's `UserId` with the authenticated user's ID. Whether that ID is trustworthy depends on `GetUserId`. `[Authorize]` is on the controller class, which rejects unauthenticated callers but admits any authenticated identity.
- Every failure, including unexpected database errors, returns `400 BadRequest` with the error text. Success returns `201 Created`. The data access layer catches every exception, including cancellation, and returns the raw exception text.
- No unique indexes, concurrency tokens, or soft-delete filters exist on `Album` and `Artist`. Two artists can share a name, and a retried create makes a second album.
- Deleting an album sets its songs' `AlbumId` to null. Deleting an artist that has songs is blocked.
- Schema limits: album title 200 characters, artist name 200, artist bio 2000.

**Required changes in data-manager**

- **F1.** Add an error kind (invalid, conflict, unexpected) to the access result and the manager response, keeping `Success`. The controller maps them to 400, 409, and 5xx. `[Proposed]`
- **F2.** The catalog sets `CreatedBy` from the authenticated caller (the committer's service identity). The approving operator, or auto-committed plus the requester, travels as separate provenance data stored apart from `CreatedBy` and not erased by the overwrite behavior. `[Proposed]`
- **F3.** Schema additions: a unique external ID on artists; an album external-ID mapping with a unique (source, external ID) pair; an idempotency table with a unique key; a row version on albums and artists; label, edition text, disc number, ISRC, a list of artists per song, and ordered album artists. `[Proposed]`
- **F4.** Validation checks the length limits (200, 200, 2000) and reports invalid before writing. `[Proposed]`
- **F5.** What deleting an edition does to its songs. With each edition owning its own tracks, removal should remove its songs, or a merge should reassign them. `[Open]`
- **F6.** Callers get a safe error message and the full exception goes to the logs. Cancellation propagates and is not logged as an error. Unexpected failures are logged as errors. `[Proposed]`
- **F7.** A merge operation for duplicates already in the catalog. `[Proposed]`

**Delivery phasing** `[Decided]`

- **Phase 0, no catalog changes.** The Harvester's walking skeleton (A21): the coordinator and one extractor, a real outbox, inbox, and relay loop, the shared library (A19), and the correlation ID (A20), against the stub catalog.
- **Phase 1, the first real commit.** F1 and F6 (error kinds, safe messages), external IDs with uniqueness, the idempotency key and the atomic endpoint (C2, C3), the new fields (F3), length validation (F4), and a field for the approver stored apart from `CreatedBy` (F2).
- **Phase 2, deferred until a feature needs it.** Row versions and the update path (refresh, R28), field-level provenance (C8), the merge operation (F7), and the edition-delete behavior (F5).
- Working rules for the data-manager repo: changes are additive (a new endpoint, new columns, an added error-kind field), and each phase is its own small versioned release through the existing changelog and jw-cicd-tools flow. `[Decided]`
- Define the commit contract before either side is built. The stub implements the same contract, so replacing it with the real endpoint is a configuration change. `[Proposed]`

---

## 13. Scaling and cover art

Scope: about ten operators using the Harvester. Scaling for a future listening audience is out of scope.

- **N1.** No service assumes a single instance. Services are stateless and consumers are idempotent. `[Proposed]`
- **N2.** The MusicBrainz limiter is shared across all instances, runs, and triggers combined. `[Proposed]`
- **N3.** More operators raise correctness risks (races), not throughput needs. Races are handled by joining, the catalog's uniqueness rule, duplicate checks, and first-decision-wins review. `[Proposed]`
- **N4.** Throughput is fixed by MusicBrainz's limit, so the design saves calls: joining concurrent requests, caching responses, skipping unchanged sources, and preferring ID-verified matches. `[Proposed]`
- **N5.** The on-demand-over-bulk priority rule must still serve small requests promptly when several operators run bulk work at once. `[Open]`
- **N6.** Each pipeline stage scales independently. Queues between stages are the mechanism. `[Proposed]`
- **N7.** A slow or failing external dependency must not stall unrelated stages or runs (bounded retries, backpressure, isolation). `[Proposed]`
- **N8.** Limits belong to the resource, not to the worker. `[Proposed]`
- **N9.** Fan-out inside a stage (such as downloading several images) happens as messages for separate consumers, not as threads inside one process. `[Proposed]`
- **N10.** Cover art is its own stage. It runs after the record is safe, has its own concurrency limit and failure handling, and a missing or failed image never blocks a record. A later retry can fill it in. `[Proposed]`
- **N11.** Cover art is stored with bounded size and a fixed set of variants. The exact variants are `[Open]`.
- **N12.** Storing the image bytes as the catalog's own data satisfies "the catalog outlives its sources," and storing only a source URL does not. Whether the catalog holds the image files or only a reference is `[Open]`.
- The Cover Art Archive's current terms and limits are not yet verified (R14). `[Open]`

Stage limits, for reference: spreadsheet parsing and scoring scale out (local CPU and database); MusicBrainz lookups do not (shared external limit, so save calls); cover art download is limited by Cover Art Archive limits (unverified); catalog writes scale within reason.

---

## 14. Vetting dashboard

The dashboard is the composition layer from A24 and expands the Angular frontend. The predecessor's desktop client had a grid, but users preferred Excel and Google Sheets.

- It needs: separate queues for AwaitingReview and NeedsAttention; candidates and scores for artists and editions, with a winner pre-selected only when it is clear; editing album fields and tracks; picking or overriding matches; duplicate warnings at decision time (A11); the raw source data; approve, reject, and correct, singly or in batches; first-decision-wins with a clear message for the second operator; and the "recently auto-committed" view (P5, P7). `[Proposed*]`
- A spreadsheet-like grid with inline editing and clipboard paste is the target. `[Proposed*]`
- Which spreadsheet qualities users valued, and the grid component choice (with its current licensing and features checked first), are `[Open]` (open item 6).
- With auto-commit as the default, the batch approval action matters less. `[Proposed*]`
- Design the dashboard only once real candidate data has been seen in the review screens. `[Proposed*]`

---

## 15. Open items

Numbered 1 to 8 as in the handoff, then additions. Each is `[Open]` unless noted.

1. **C9 trust boundary,** and what `GetUserId` returns and how authentication is configured in data-manager.
2. Whether scheduled runs follow the auto-commit default (P6, currently Proposed).
3. Policy numbers: the watch list cap (SW9), the freshness window, retry limits (R26, A16), the on-demand versus bulk priority rule (N5), the maximum track count (C10), and the description length limit.
4. Whether the resolver and review are one service, and whether the committer is its own service.
5. Cover art: whether the catalog stores image bytes, and the image variants (N11, N12).
6. The vetting dashboard: the grid component and which spreadsheet qualities users valued.
7. Whether the artist-candidate search stays a simple normalized name match.
8. The marker vocabulary beyond remaster, mono, stereo, and remix, and verifying marker propagation (MP1) on more albums. A release in the 81-release set has the disambiguation "Atmos mix", which the starting vocabulary does not match ("remix" is a stem, "mix" is not), so it would be treated as an ordinary member of its group.

Additions found during consolidation:

9. The R38 gap: a request requiring approval that arrives within the freshness window after a finished auto-committed run returns the finished result, and what happens to its unreviewed auto-committed albums is undecided.
10. R19 and R30: split their Decided and Proposed halves.
11. Album record fields beyond Title, Artist[], Year, Label, Edition, and whether operators can enter a MusicBrainz ID at creation.
12. The duration rule for commit validation.
13. Whether TC-H7 and TC-H8 (stale update version, refresh preserving human-set fields) belong in the Phase 1 shared contract suite. The contract section lists them, but the update path is Phase 2.
14. Spreadsheet-path matching: how candidates are scored against the operator's record is not specified in section 9.
15. F5 (edition-delete behavior) and A26 (copied display fields).
16. Whether list-valued staging columns (Artists) are JSON text or a child table.
17. Per-field source priority if a second reference source is ever added.
18. Whether one operator's correction can replace another's.
19. The Cover Art Archive's terms and limits (R14).
20. Search-query escaping behavior for colons, parentheses, hyphens, and the words AND and OR (a malformed query can fail silently).
21. Recheck MusicBrainz's rate-limit and API pages before building (their rules are dated and may change).
22. data-manager code not yet reviewed: the `Song` and join configuration, migrations, PUT and DELETE paths, and the Artists and Songs controllers.
23. Many Proposed items in section 9 (Steps 1 and 4, the label rule, the proposal contents) are untested against code. The test-writing step will exercise them.
24. The candidate shape must carry what the grouping rule reads (release status, disambiguation text, barcode, and a per-track recording reference) without source vocabulary. The shape in section 3 does not name these yet.
25. Project naming and boundaries. The intended names are `Jukebox.Harvester.Extract`, `.Transform`, `.Load`, and matching `.Tests` projects. How they map to the services in section 11 (coordinator, resolver, review, cover art, committer) is not decided. In particular the coordinator and cover art have no ETL name.
26. Label handling. The edition takes the first entry of a release's label list, and Step 4 prefers releases with a label. A release can have a label entry with a null label (a catalog number only), no label list at all, or MusicBrainz's placeholder label `[no label]`. In the trimmed fixtures, 5 of 43 releases have no usable label and one real release carries the placeholder. The rule does not say how these count.

---

## 16. Next steps and housekeeping

Next steps, in order:

1. Choose and trim fixtures for the repo (small pages, with a lower `limit`). Record the Various Artists compilation. Read MusicBrainz's data license page before committing them.
2. Write the grouping rule (section 9) and the commit contract tests as code tests against the fixtures.
3. Build the Phase 0 walking skeleton against a stub catalog: the coordinator and one extractor with a real outbox, inbox, and relay loop.
4. Read data-manager's `GetUserId`, authentication setup, the `Song` and join configuration, and migrations, and settle C9.
5. Plan Phase 1 changes to data-manager as small, additive, separately versioned releases.
6. Design the vetting dashboard once real candidate data has been seen.

Test strategy `[Proposed*]`

- The grouping rule is tested against recorded MusicBrainz responses, so tests use neither the live service nor its rate limit. Large recorded pages (hundreds of KB) stay in a scratch folder. Smaller trimmed versions are re-recorded with a lower `limit` for the repo.
- Use cases and test cases live in code, not in this document. A working table maps the old test-case IDs to their dispositions.
- Grouping tests take candidates built by a small test helper from the recorded JSON. The helper is replaced by the real MusicBrainz extractor mapping once that exists. `[Decided]`
- The pipeline must run end to end with a fake extractor (R12). One shared contract suite runs against both the stub catalog and the real endpoint.

Housekeeping

- The earlier harvester code goes on the `feature/initial-scaffolding` branch, pushed with a PR. Check `.gitignore` and secrets first.
- The untrimmed fixtures live in `scratch/musicbrainz/`, a git-ignored folder at the repo root. `[Proposed]`
- A short root AGENTS.md holds conventions and guardrails for coding agents and points here. It never holds requirements. `[Proposed]`
