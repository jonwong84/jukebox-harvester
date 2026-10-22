# AGENTS.md

Instructions for coding agents (Cursor, Antigravity, GitHub Copilot, Codex, and others) working in jukebox-harvester. This file holds conventions and guardrails only. Requirements live in `docs/requirements.md`. Do not copy them here.

## What this repo is

jukebox-harvester is the metadata ingestion service for the Jukebox catalog. It pulls album and track metadata from MusicBrainz and operator spreadsheets, groups releases into editions, and commits approved records to jukebox-data-manager. Read `docs/requirements.md` before changing behavior. It is the single source of truth.

## Status tags are binding

Every item in `docs/requirements.md` is tagged `[Decided]`, `[Proposed]`, `[Proposed*]`, or `[Open]`.

- Implement and assert `[Decided]` items as settled.
- `[Proposed]` and `[Proposed*]` items are suggestions, not agreements. Do not build on them as if they were settled. If a task needs one, say so and ask. A task brief that names a Proposed rule explicitly authorizes testing it, with the trait below.
- `[Open]` items are undecided. Never pick an answer on your own.
- A test of a non-settled rule carries `[Trait("rule-status", "proposed")]`. A test that records behavior we know is a gap carries `[Trait("rule-status", "known-gap")]`. Tests of Decided rules carry no trait.

## Stack and layout

- .NET 10, solution file `Jukebox.Harvester.slnx` at the repo root.
- Tests use xUnit v3 (`xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`). Test projects are executables (`OutputType` `Exe`). A root `global.json` selects the Microsoft Testing Platform runner for `dotnet test`. Do not remove or change it.
- `src/Jukebox.Harvester.Transform/`: class library, pure logic (grouping), no queue, database, or HTTP. Contains the provisional grouping input types and the `EditionGrouper` stub, which currently returns an empty result.
- `tests/Jukebox.Harvester.Transform.Tests/`: xUnit v3 tests for the library, with the fixture loader and the `Candidates` helper.
- `tests/fixtures/musicbrainz/`: recorded and synthetic fixtures. See the README there.
- `src/Harvester.Service/`, `src/Harvester.Data/`, and the sibling repo `jukebox-harvester-data-access` predate this layout. Their names and boundaries are undecided (open item 25). Do not rename, move, or restructure them.
- More projects (`.Extract`, `.Load`, and so on) are added only when first needed. Do not create empty projects.

Build and test, from the repo root:

```
dotnet build
dotnet test
```

If `dotnet test` fails to run, the fallback is `dotnet run --project tests/Jukebox.Harvester.Transform.Tests`. Report the failure and do not change `global.json` or project settings to work around it.

The build prints vulnerability warnings (NU1902, NU1903) for `System.Security.Cryptography.Pkcs` from projects other than the ones above. They are known. Do not change package versions to clear them unless a task says so.

## Git

- Never commit, push, or open pull requests. The maintainer does that.
- Never run `git add .` or `git add -A`. If you stage anything, stage files by name.
- The maintainer checks changes with `git --no-pager diff`, so keep changes small and limited to the files the task names.

## Do not

- Edit `docs/requirements.md` or this file.
- Edit recorded fixtures by hand. The maintainer re-records and re-trims them. Fully synthetic fixtures are named `synthetic-*` and say so in their `_fixture` block. A partly synthetic fixture says so in its `_fixture` block.
- Add a NuGet package or change a framework version without asking. The xUnit v3 packages above are already in place.
- Decide a design question. If the task depends on one, stop and ask.
- Derive a test's expected value by running the code under test. Expected values come from the task brief or from `docs/requirements.md`.
- Commit secrets, tokens, or personal data. Check `.gitignore` first.

## Fixtures

- Recorded MusicBrainz responses are real data. Keep core entity fields only. Do not add tags, genres, ratings, or annotations (they carry a different license).
- Recorded fixtures must not contain the maintainer's contact email or any User-Agent string.
- Untrimmed recordings live in `scratch/musicbrainz/`, which is git-ignored. Never move them into `tests/` or stage them.

## When unsure

Ask. A short question costs less than a wrong guess in a catalog whose first rule is that nothing becomes canonical on a guess.