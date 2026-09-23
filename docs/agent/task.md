# Implementation Task Plan — Smart Disk Cleaner Analyzer

This plan deliberately places only the **high-risk, logic-heavy, correctness-sensitive work** in Phase 1. Phase 2 contains straightforward integration, UI, persistence polish, and usability work that a weaker model can complete after the core contracts are stable.

---

# Phase 1 — Core Correctness / High-Reasoning Tasks

> Goal: prove the scanner and analysis model are safe and correct before spending tokens/time on UI polish.

## P1-01 — Establish core domain contracts and test harness

**Why Phase 1:** Every difficult subsystem depends on stable node/accounting/warning contracts. Wrong contracts cause expensive rewrites.

### Checklist

- [x] Create domain enums/types for node type, category, risk, confidence, accounting confidence, metadata flags, session status.
- [x] Create `RawScanNode`, finalized `ScanNode`, `ScanWarning`, `ScanSession`, `ScanSnapshot` contracts.
- [x] Represent unknown allocated/unique size as nullable/explicit unknown, never zero.
- [x] Define physical file identity as volume identity + file identity, not path.
- [x] Define interfaces for filesystem metadata, allocated-size provider, physical identity provider, volume info, scanner.
- [x] Create deterministic in-memory fixture builders for unit tests.
- [x] Add project references/dependency direction so Domain has no WPF/SQLite/native dependencies.
- [x] Add baseline build/test commands and make them pass.

### Acceptance

- [x] Core models can represent ordinary file, hard link, reparse point, inaccessible node, partial metadata, and directory aggregate without hacks.
- [x] Domain tests run without touching real disk.
- [x] No deletion/mutation service exists in core contracts.

**References:** `00_project_scope.md`, `01_architecture.md`, `03_storage_accounting.md`

---

## P1-02 — Implement safe read-only filesystem traversal

**Why Phase 1:** A scanner bug can recurse forever, cross scope, crash on normal Windows conditions, or accidentally violate safety guarantees.

### Checklist

- [x] Validate selected root and drive readiness.
- [x] Resolve and exclude the exact application-owned data root when it is under the selected drive; do not broaden the exclusion to unrelated AppData paths.
- [x] Implement iterative/queue-based directory traversal.
- [x] Enumerate hidden/system entries when accessible.
- [x] Detect reparse point before enqueueing a directory.
- [x] Emit reparse node but **never recurse through it**.
- [x] Support long-path-capable APIs/behavior.
- [x] Convert access-denied, vanished path, and per-item I/O failures into `ScanWarning` where recoverable.
- [x] Implement cancellation throughout traversal.
- [x] Ensure handles/resources are disposed.
- [x] Do not read file contents.
- [x] Add scanner integration fixture tests.
- [x] Add non-mutation verification for fixture tree.

### Acceptance

- [x] Reparse-loop fixture cannot cause recursion loop.
- [x] One inaccessible branch does not abort other branches.
- [x] Cancellation stops scan cleanly.
- [x] Scan does not create, modify, rename, or delete fixture files.
- [x] App-owned exclusion root is never traversed when it resides on the selected volume.
- [x] Partial metadata is explicit rather than fabricated.

**References:** `02_filesystem_scanner.md`, `08_nonfunctional_security.md`, `09_testing_acceptance.md`

---

## P1-03 — Implement NTFS-aware storage accounting and hard-link deduplication

**Why Phase 1:** This is one of the most error-prone parts. Incorrect hard-link/sparse/allocated accounting makes all later "largest" and reclaim estimates misleading.

### Checklist

- [x] Implement logical-size capture.
- [x] Implement allocated-size provider with clean unsupported/failure behavior.
- [x] Implement physical file identity provider on supported Windows filesystem.
- [x] Capture hard-link/link-count metadata where available.
- [x] Build session-level physical identity map.
- [x] Count unique allocated bytes only once per known physical identity for whole-scan accounting.
- [x] Preserve path-associated allocated bytes separately from unique allocated bytes.
- [x] Propagate unknown allocated/identity metadata as partial confidence.
- [x] Implement O(N) bottom-up directory aggregation.
- [x] Add overflow-safe byte aggregation.
- [x] Add deterministic fallback when native accounting is unavailable.
- [x] Test normal files, repeated hard-link identities, unknown identity, unknown allocated size, nested aggregates, and overflow guard.
- [x] Add Windows integration test for real hard links where environment permits.
- [x] Add sparse-file integration test where environment permits; otherwise keep deterministic provider-level test.

### Acceptance

- [x] Known hard-linked physical allocation is not double-counted in unique total.
- [x] Logical and allocated values remain distinct.
- [x] Unknown never masquerades as exact zero.
- [x] Directory aggregates are deterministic and linear-time after discovery.
- [x] Accounting confidence is visible when data is incomplete.

**References:** `03_storage_accounting.md`

---

## P1-04 — Implement deterministic protection + cleanup classification engine

**Why Phase 1:** A bad rule engine can falsely label user/system data as junk. Safety precedence and explainability must be correct before UI exposes recommendations.

### Checklist

- [x] Implement immutable `RuleDefinition` and validated rule-set loading.
- [x] Implement normalized Windows path matching.
- [x] Implement condition types required by V1: exact/prefix path, name/glob, extension, type, size, age, attributes, reparse status, parent context.
- [x] Implement hard protection rules first.
- [x] Implement risk/confidence/category as separate outputs.
- [x] Implement conflict resolution with `Protected` sticky precedence.
- [x] Implement conservative built-in known temp/cache candidate rules.
- [x] Implement large-file informational rule that does **not** classify by size alone as high-confidence junk.
- [x] Ensure missing metadata causes non-match/conservative behavior, not false positive.
- [x] Store matched rule IDs + primary reason + rule-set version.
- [x] Add positive, negative/near-miss, precedence, case-insensitive, missing-metadata tests for every built-in rule family.
- [x] Add invariant test: protected cannot be downgraded.
- [x] Add invariant test: same facts + ruleset => same result.

### Acceptance

- [x] Every non-Unknown cleanup classification is explainable by rule evidence.
- [x] A large ISO/video/archive is never called high-confidence junk solely because of size.
- [x] Generic `.tmp`/cache patterns cannot override protected path rules.
- [x] Rule engine requires no WPF/live filesystem dependency.

**References:** `04_rule_engine.md`, `10_initial_rule_catalog.md`

---

## P1-05 — Implement and prove adaptive 10/40 tree policy

**Why Phase 1:** The algorithm is product-defining and must not accidentally become a scan-pruning rule.

### Checklist

- [x] Implement `AdaptiveTreePolicy` as pure domain/application logic.
- [x] Count threshold using immediate files + immediate directories.
- [x] If total children `<= 10`, allow automatic recursion to all immediate child directories.
- [x] If total children `> 10`, calculate `K = ceil(immediateDirectoryCount * 0.40)`.
- [x] Rank directories by unique allocated recursive bytes when complete.
- [x] Fallback to allocated recursive, then logical recursive bytes.
- [x] Implement deterministic tie breaker.
- [x] Return auto-expand IDs without deleting/pruning snapshot nodes.
- [x] Add exact table-driven tests from `05_adaptive_tree.md`.
- [x] Test manual access to non-top-40 branch at projection level.

### Acceptance

- [x] `10 files + 6 dirs` => 3 dirs auto-expanded.
- [x] `11 files + 0 dirs` => 0 dirs auto-expanded.
- [x] `0 files + 11 dirs` => 5 dirs auto-expanded.
- [x] `0 files + 25 dirs` => 10 dirs auto-expanded.
- [x] Full scan/snapshot content is unchanged by presentation policy.

**References:** `05_adaptive_tree.md`

---

## P1-06 — Integrate one authoritative scan pipeline + correctness gate

**Why Phase 1:** Individually correct pieces can still become wrong when ordered incorrectly. This task proves the final sequence and safety invariants.

### Checklist

- [x] Implement pipeline: validate -> scan -> accounting -> aggregate -> classify -> snapshot.
- [x] Ensure size-dependent rules run only after required accounting/aggregates exist.
- [x] Ensure presentation policy runs on finalized snapshot facts.
- [x] Implement typed session states and progress stages.
- [x] Propagate cancellation through the pipeline.
- [x] Preserve recoverable warnings.
- [x] Derive session summary using exact/partial accounting semantics.
- [x] Add end-to-end in-memory/fake-provider golden tests.
- [x] Add real temp-filesystem integration smoke test.
- [x] Run full Phase 1 test suite.
- [x] Review code specifically for any accidental delete/move/write-to-scan-tree path.

### Acceptance / Phase 1 gate

- [x] `dotnet build` passes.
- [x] `dotnet test` passes.
- [x] Reparse, hard-link, partial accounting, rule precedence, 10/40, cancellation, and non-mutation tests pass.
- [x] A completed in-memory snapshot contains all facts needed by Phase 2 without rescanning for UI purposes.
- [x] No unresolved correctness TODO remains in Phase 1 core path.

---

# Phase 2 — Integration, UI, Persistence, Usability & Polish

> Goal: build the usable desktop application on top of the already-proven core. Tasks are intentionally ordered from foundational/easier work toward moderate integration work.

## P2-01 — Create WPF shell and MVVM application state

- [ ] Create WPF app project/composition root.
- [ ] Create MainWindow and base layout.
- [ ] Add drive selector for available C:/D: targets.
- [ ] Implement Start/Cancel commands.
- [ ] Bind typed app states: Idle/Scanning/Processing/Saving/Ready/Error.
- [ ] Disable conflicting controls during scan.
- [ ] Keep filesystem traversal out of ViewModels.
- [ ] Add basic ViewModel command/state tests.

**Acceptance:** app launches, drive can be selected, pipeline can be started/cancelled through application service without UI freeze.

---

## P2-02 — Implement scan progress and summary UI

- [ ] Show current stage, elapsed time, discovered counts, warnings count.
- [ ] Throttle/batch progress events.
- [ ] Show completed summary separated by candidate category/risk.
- [ ] Mark accounting as exact/partial where required.
- [ ] Avoid a misleading single reclaimable total when risky/partial items are included.
- [ ] Add user-safe error/warning messages.

**Acceptance:** long scan keeps UI responsive and user can understand current stage and result quality.

---

## P2-03 — Implement lazy hierarchical tree UI

- [ ] Create lazy `TreeNodeViewModel`.
- [ ] Materialize children on demand.
- [ ] Integrate `AdaptiveTreePolicy` for automatic expansion.
- [ ] Allow manual expansion outside top 40%.
- [ ] Show size, category, risk/protection, warning/reparse state.
- [ ] Ensure classification is not conveyed by color alone.
- [ ] Add large synthetic tree smoke test.

**Acceptance:** tree remains usable with large snapshots and exactly follows Phase 1 policy.

---

## P2-04 — Implement node details panel

- [ ] Show full path/name/type.
- [ ] Show logical, allocated, and unique/local accounting where meaningful.
- [ ] Show timestamps/attributes.
- [ ] Show hard-link/reparse/partial metadata facts.
- [ ] Show category, risk, confidence, reason, matched rules.
- [ ] Add `Copy Path`.
- [ ] Add `Open in Explorer` with vanished-target handling.
- [ ] Do not add Delete action.

**Acceptance:** user can understand why a node is shown and what uncertainty exists.

---

## P2-05 — Implement SQLite snapshot repository

- [ ] Create schema from `06_data_persistence.md`.
- [ ] Store DB only in app data directory.
- [ ] Implement transactional snapshot save.
- [ ] Implement indexed child/node/session queries.
- [ ] Implement batch insert/prepared statements.
- [ ] Persist ruleset/options version and warnings.
- [ ] Preserve nullable unknown metrics.
- [ ] Add rollback/failure-injection tests.
- [ ] Add save/reload equality tests.

**Acceptance:** no completed snapshot can be observed partially saved; large child queries are indexed.

---

## P2-06 — Reopen recent snapshots

- [ ] List recent scan sessions.
- [ ] Open a completed snapshot without rescanning.
- [ ] Show that snapshot data is historical and disk may have changed.
- [ ] Allow deleting old snapshots **only from app-owned DB/data**.
- [ ] Handle missing/corrupt snapshot gracefully.

**Acceptance:** persisted analysis can be reviewed later without any scan-tree mutation.

---

## P2-07 — Search, sort and filters

- [ ] Text search by name/path.
- [ ] Filter by category.
- [ ] Filter by risk/protected/warning/reparse.
- [ ] Minimum size filter.
- [ ] Sort results by useful size metrics/name/path.
- [ ] Jump from search result to details/tree context where practical.
- [ ] Keep operations responsive on large snapshots.

**Acceptance:** user can locate large or suspicious candidates without manually expanding the entire tree.

---

## P2-08 — Explicit export

- [ ] Add export command only when stable snapshot exists.
- [ ] Let user explicitly choose destination.
- [ ] Support at least CSV or JSON; optional both.
- [ ] Export path, type, sizes, category, risk, confidence, reason, warnings as appropriate.
- [ ] Reject invalid destination safely.
- [ ] Never auto-export.
- [ ] Add tests for escaping/Unicode/nullable values.

**Acceptance:** export occurs only after explicit user action and does not mutate scanned content.

---

## P2-09 — Local configuration and logging

- [ ] Store config/logs under app data directory.
- [ ] Add config schema/version.
- [ ] Validate settings and fall back to documented defaults.
- [ ] Add rolling/size-bounded local logs.
- [ ] Do not log file contents or secrets.
- [ ] Log session/stage/errors sufficiently for debugging.

**Acceptance:** diagnostics are useful without creating unbounded logs or writing to scan tree.

---

## P2-10 — UI polish and accessibility

- [ ] Add clear icons/text for High-confidence / Review / Large-not-junk / Protected / Warning.
- [ ] Handle long paths with tooltip/copy support.
- [ ] Check high-DPI behavior.
- [ ] Improve empty/error/cancel states.
- [ ] Add keyboard navigation where practical.
- [ ] Ensure category differences are understandable without color.

**Acceptance:** application is understandable and safe for normal personal use.

---

## P2-11 — Packaging and final release verification

- [ ] Configure release build/publish for Windows target.
- [ ] Ensure no secrets/private machine paths are embedded.
- [ ] Confirm app works offline.
- [ ] Run full test suite.
- [ ] Perform manual C: scan without elevation.
- [ ] Perform D: scan when available.
- [ ] Verify warnings on inaccessible system locations rather than crash.
- [ ] Verify reparse nodes are not followed.
- [ ] Verify no Delete command exists.
- [ ] Verify app writes only to app-data + explicit export destination.
- [ ] Review all `docs/project/` against implemented behavior.
- [ ] Commit and push final verified state to `main`.

**Acceptance:** all system acceptance items in `09_testing_acceptance.md` pass or any environment-specific limitation is documented factually.

---

# Completion Rules

A checkbox means **implemented and verified**, not merely started.

For any task:

- [ ] Implementation matches relevant `docs/project/` contracts.
- [ ] Focused tests pass.
- [ ] Full suite passes when task touches Phase 1/core behavior.
- [ ] Docs are updated if behavior changed.
- [ ] `git diff` contains no accidental unrelated edits before commit.

Do not move difficult unfinished correctness work from Phase 1 to Phase 2 simply to make progress appear complete.
