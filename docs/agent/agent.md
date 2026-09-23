# AI Agent Rules — Smart Disk Cleaner Analyzer

This project is a **single-developer personal project**. Keep the workflow simple, but do not compromise filesystem safety or correctness.

## 1. Source of truth

Before changing behavior, read the relevant files in `docs/project/` and this file. The project specifications are authoritative. If code and docs conflict, do not silently choose one: determine the intended behavior, update code and/or docs in the same change so they agree.

## 2. Core safety rules

The following are non-negotiable:

- The V1 application is a **read-only analyzer**, not an automatic cleaner.
- Never add automatic delete, move, rename, truncate, quarantine, or content rewrite behavior unless the project scope is explicitly changed.
- Scanning/classification MUST NOT write inside the analyzed namespace.
- App database/cache/log/config files belong only in the application's own data directory. If that root is on the selected drive, exclude exactly that root from analysis before traversal.
- Export occurs only after explicit user action.
- Do not follow filesystem reparse points/junctions/symlinks during V1 scan.
- Do not treat a large file as junk merely because it is large.
- Protected rules always take precedence over generic cleanup rules.
- Unknown metadata must remain unknown/partial; never turn missing values into fake zeros or false certainty.
- Preserve distinction among logical bytes, allocated bytes, and unique allocated bytes.

## 3. Engineering rules

- Prefer the simplest implementation that satisfies the spec.
- Correctness before performance optimization.
- Keep domain logic testable without WPF, SQLite, or live filesystem access.
- Do not place filesystem traversal logic in ViewModels.
- Use typed models/enums instead of stringly-typed core state when practical.
- All long-running work must support cancellation.
- Recoverable per-path I/O failures become warnings; they should not crash the entire scan.
- Avoid reading file contents in V1.
- Avoid unnecessary dependencies.
- Do not refactor unrelated code while implementing a task.
- If an implementation decision changes an established contract, update the relevant project documentation.

## 4. Testing rules

For each behavior change:

- Add or update focused automated tests.
- Add a regression test for any correctness bug when practical.
- Run the relevant tests before committing.
- Before marking a Phase 1 task complete, run the full test suite.
- Never mark a task complete if its acceptance criteria are not actually met.

Filesystem-critical changes must explicitly test safety invariants where relevant: reparse behavior, hard-link accounting, partial metadata, cancellation, and non-mutation.

## 5. Task execution

Use `docs/agent/task.md` as the implementation checklist.

- Work Phase 1 before Phase 2 unless a small prerequisite is required to enable Phase 1.
- Within a task, complete prerequisites and tests before checking the task off.
- Do not check future tasks simply because scaffolding exists.
- If a task is blocked, leave it unchecked and add a short factual note describing the blocker.

## 6. Git workflow

This is a one-person project. Use only the `main` branch unless the developer explicitly asks otherwise.

Typical flow:

```bash
git status
git add <relevant-files>
git commit -m "<clear message>"
git push origin main
```

Rules:

- Do not create feature branches by default.
- Do not force-push `main`.
- Do not rewrite published history unless explicitly requested.
- Do not commit secrets, credentials, machine-specific private data, build outputs, or large generated artifacts that should be ignored.
- Keep commits logically scoped; one huge mixed commit is worse than a few clear commits.
- Before commit, inspect `git diff` and ensure unrelated user changes are preserved.

## 7. Completion report

When finishing a task, report concisely:

- what changed,
- important design decisions,
- tests run and result,
- files/docs changed,
- any remaining limitation or blocker.
