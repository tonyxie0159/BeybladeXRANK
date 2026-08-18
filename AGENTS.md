# Repository guidance

## Development workflow

- Treat GitHub issues and pull requests as the source of truth for planned work and review.
- Never develop directly on `main`; create a focused branch named `codex/<short-description>`.
- Keep each pull request limited to one coherent change and open it as a draft until verification is complete.
- Do not commit secrets, access tokens, API keys, local databases, generated build output, or user-specific settings.
- Before handing work off, run `dotnet test BeybladeXRANK-main/BeybladeRecordSystem.slnx` and report the result.

## Code review rules

- Treat battle scoring, round revision history, and lineup ordering as compatibility-sensitive behavior; flag changes that can alter recorded results without an explicit migration or test.
- Preserve user ownership boundaries for accounts, Beyblades, battles, and statistics queries.
- Require focused regression tests for changes to domain rules, authentication, persistence, or migrations.
- Leave deterministic build and test checks to GitHub Actions; review should focus on correctness, security, and data integrity.
