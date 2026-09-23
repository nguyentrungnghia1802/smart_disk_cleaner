# 09 — Verification, Test Strategy & System Acceptance

**Document status:** Normative

## 1. Responsibility

Define how the project proves filesystem correctness, classification safety, adaptive-tree behavior, persistence consistency, and end-to-end product acceptance.

## 2. Boundary

Testing covers unit, integration, property/invariant, system, and performance-smoke tests. It does not require production-scale benchmark infrastructure for a personal project, but correctness-critical filesystem behaviors MUST have deterministic automated coverage where feasible.

## 3. Input / Output

### Test inputs

- pure in-memory node graphs,
- fake metadata providers,
- temporary filesystem fixtures,
- optional NTFS-specific fixtures,
- malformed rule sets,
- synthetic large snapshots,
- injected failures/cancellation.

### Test outputs

- pass/fail evidence,
- expected vs actual accounting values,
- mutation-guard evidence,
- warning/event assertions,
- performance-smoke measurements where relevant.

## 4. Domain Model

Test categories:

```text
UNIT-ACC      Storage accounting
UNIT-RULE     Rule engine
UNIT-TREE     Adaptive tree
UNIT-APP      Orchestration/state
INT-FS        Real filesystem integration
INT-DB        SQLite persistence
INT-PIPE      End-to-end scan pipeline
UI-SMOKE      WPF critical flow
PERF-SMOKE    Large synthetic/real fixture
SAFETY        Non-mutation/protection invariants
```

Every critical bug should receive a regression test before/with the fix.

## 5. Data Structure

Canonical deterministic fixture representation:

```text
TestNodeSpec
- Path
- Type
- LogicalBytes
- AllocatedBytes?
- PhysicalIdentity?
- Attributes
- ReparsePoint
- Accessible
```

Expected result representation:

```text
ExpectedAccounting
ExpectedClassification
ExpectedAutoExpansion
ExpectedWarnings
```

## 6. Algorithm

### 6.1 Test pyramid

Most tests should be fast pure unit tests. Native filesystem behavior gets focused integration tests. Full-drive scans are manual verification, not normal CI.

### 6.2 Required Phase 1 correctness suite

#### A. Scanner

- normal traversal,
- inaccessible child continues,
- disappearing file continues,
- reparse directory is emitted but not traversed,
- cancellation stops work,
- long path supported where environment permits.

#### B. Accounting

- exact logical sum,
- allocated vs logical distinction,
- hard-link unique accounting,
- unknown allocated propagation,
- deterministic directory aggregation,
- overflow guard.

#### C. Rule engine

- protected precedence,
- known temp/cache positive/negative cases,
- large file not labeled high-confidence junk,
- case-insensitive path matching,
- missing metadata conservative behavior,
- deterministic results by rule-set version.

#### D. Adaptive tree

Use the exact threshold table from `05_adaptive_tree.md`, plus ties and fallback ranking.

#### E. Safety

- fixture contents unchanged,
- no file created/deleted/renamed in fixture,
- no reparse traversal,
- no deletion API reachable from V1 use cases.

### 6.3 Property/invariant tests

Useful invariants:

```text
parent recursive logical >= each child's recursive logical
unique allocated <= path-associated allocated when all values known
protected classification cannot be downgraded by generic rule
same input + same ruleset => same classification
0 <= autoExpandedDirs <= immediateChildDirs
if total children <= 10 => autoExpandedDirs == immediateChildDirs
if total children > 10 => autoExpandedDirs == ceil(D*0.4)
```

### 6.4 Manual system acceptance

Before V1 release, manually scan:

- a normal non-system test directory/drive,
- C: without elevation,
- D: if available,
- a machine/profile containing cloud placeholders if available.

Compare broad totals against Windows/another trusted disk analyzer for sanity, while accounting for different hard-link/filesystem-overhead semantics.

## 7. State / Flow

Defect workflow for personal project:

```text
Find bug
 -> reproduce
 -> add failing regression test where practical
 -> fix smallest root cause
 -> run targeted tests
 -> run full test suite
 -> update docs if behavior/spec changed
 -> commit to main
```

No heavyweight QA branch is required.

## 8. Error Handling

Tests must deliberately inject:

- access denied,
- native metadata failure,
- repository exception,
- malformed rules,
- cancellation,
- missing path,
- inconsistent node graph.

A test that cannot create an OS-specific fixture due to environment privileges should be explicitly skipped with reason, not reported as passed.

## 9. Edge Cases

- exactly 10 vs 11 children,
- all child items are files,
- only one child directory,
- hard link points to file outside visible branch but within volume,
- hard-link identity unavailable,
- root contains reparse mount point,
- enormous sparse logical size,
- null timestamps,
- odd Unicode/case path,
- SQLite persistence interrupted,
- scan canceled during each major pipeline stage.

## 10. Performance Consideration

Performance tests are guardrails, not absolute hardware promises. Record environment when comparing results.

Suggested smoke scenarios:

- 100k synthetic nodes for pure aggregation/rules/tree projection,
- large fan-out directory for top-K tree behavior,
- batched SQLite insert/query benchmark,
- WPF tree with large snapshot but limited materialized nodes.

A regression that causes UI freezes or accidental O(N²) processing should block release.

## 11. Testing Requirement

Definition of done for any task that changes behavior:

- tests added/updated,
- all related tests pass,
- no new compiler warnings without justification,
- no unexplained skipped correctness tests,
- docs updated when contract changes.

Minimum release gates:

```text
dotnet build => pass
dotnet test  => pass
manual V1 smoke => pass
mutation safety check => pass
critical spec checklist => pass
```

## 12. Acceptance Criteria

V1 is acceptable only if all are true:

- [ ] Scan C:/D: selection works where drive exists.
- [ ] No automatic delete/move/rename capability exists.
- [ ] Reparse points are not followed.
- [ ] Recoverable inaccessible paths do not crash whole scan.
- [ ] Logical/allocated size distinction is preserved.
- [ ] Hard-link unique accounting is correct when identity is available.
- [ ] Partial accounting is labeled, not fabricated.
- [ ] Protected precedence tests pass.
- [ ] Large-file-only rules do not classify as high-confidence junk.
- [ ] Adaptive 10/40 table tests pass exactly.
- [ ] Snapshot save/reload is consistent.
- [ ] Cancellation works without corrupting completed snapshot state.
- [ ] UI remains usable during a realistic long scan.
- [ ] Mutation guard proves fixture tree unchanged.
