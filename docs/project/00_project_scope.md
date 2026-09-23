# 00 — Project Scope & Product Contract

**Document status:** Normative / Source of truth  
**Project:** Smart Disk Cleaner Analyzer  
**Target:** Windows desktop application  
**Primary stack:** C# / .NET 10 / WPF / MVVM  
**Product mode:** Read-only disk analysis and user-assisted review; no automatic deletion

## Document Set / Reading Order

1. `00_project_scope.md` — product contract, scope, invariants, shared vocabulary.
2. `01_architecture.md` — modular-monolith boundaries and execution pipeline.
3. `02_filesystem_scanner.md` — read-only traversal and Windows filesystem hazards.
4. `03_storage_accounting.md` — logical/allocated/unique bytes, hard links, aggregation.
5. `04_rule_engine.md` — deterministic classification, protection precedence, explainability.
6. `05_adaptive_tree.md` — exact `>10 -> top 40% child directories` presentation policy.
7. `06_data_persistence.md` — SQLite snapshot model and app-owned write policy.
8. `07_ui_ux.md` — WPF/MVVM workflows and safe presentation.
9. `08_nonfunctional_security.md` — safety, privacy, performance, reliability.
10. `09_testing_acceptance.md` — verification strategy and system release gates.
11. `10_initial_rule_catalog.md` — initial protected/candidate rule families and safeguards.
12. `../agent/agent.md` — rules for coding agents.
13. `../agent/task.md` — two-phase implementation checklist.

When documents conflict, this product contract and the more specific normative document for the affected subsystem must be reconciled before implementation; do not silently implement contradictory behavior.

## 1. Responsibility

This document defines the product contract: what the application is, what it is not, supported use cases, safety invariants, terminology, top-level behavior, and acceptance boundaries. All implementation documents and tasks MUST conform to this document.

The application SHALL help a single local user analyze a selected Windows drive (initially C: and D:), identify files/folders that may be reclaimable or suspiciously wasteful, explain why they were classified, and present results in a hierarchical tree. The application SHALL NOT automatically delete, move, rename, truncate, rewrite, quarantine, or otherwise mutate scanned filesystem content.

## 2. Boundary

### 2.1 In scope

- Select one supported fixed drive per scan: C: or D: in V1.
- Enumerate files/directories and read filesystem metadata.
- Calculate logical size and, where technically available, allocated size.
- Aggregate directory sizes from descendants.
- Detect and safely handle hard links, reparse points, inaccessible entries, disappearing files, long paths, and common race conditions.
- Classify entries using deterministic rules into categories such as high-confidence reclaimable, review recommended, large/not-junk, protected, unknown, and scan warning.
- Build a full internal scan result and a bounded presentation tree.
- Apply the adaptive presentation rule: when a directory has more than 10 immediate children, recursively expand only the top 40% largest immediate child directories by allocated size; all items remain discoverable through manual expansion/search.
- Display reasons, matched rules, risk, confidence, sizes, path, timestamps, and warnings.
- Persist scan snapshots and app cache only inside the application's own data directory.
- Treat the application-owned data root as a reserved exclusion from filesystem analysis when it is physically located on the selected drive; this prevents the scanner from observing files that the application itself must mutate.
- Export only after explicit user action.

### 2.2 Out of scope for V1

- Automatic deletion or one-click cleanup.
- Registry cleaners.
- Memory/RAM cleaners.
- Driver cleanup.
- Windows component servicing or direct WinSxS manipulation.
- Uninstalling applications.
- Antivirus/malware classification.
- Content-based duplicate detection.
- Cloud account operations.
- Network drive crawling.
- Cross-platform support.
- Background system service.
- Kernel driver.

### 2.3 Non-negotiable write policy

The application MUST only write SQLite databases, caches, logs, configuration, and temporary app artifacts inside its own application-data area. Export is allowed only when the user explicitly selects a destination. If the app-data root resides on the selected drive, that root MUST be excluded from the analysis scope before traversal. With this explicit reserved exclusion, the application MUST NEVER create, modify, or delete files inside the analyzed directory namespace as part of scanning, classification, or analysis.

## 3. Input / Output

### Inputs

- Selected drive root, initially `C:\` or `D:\`.
- Scan options from application configuration.
- Rule definitions shipped with the application.
- Optional user filter/search criteria after a scan.

### Outputs

- Immutable scan snapshot containing nodes, aggregate metrics, warnings, and rule matches.
- Presentation tree derived from the snapshot.
- Per-node classification and explanation.
- Overall summary: total scanned logical bytes, total unique allocated bytes where available, candidate bytes by category, inaccessible counts, warning counts, scan duration.
- Optional explicit export in a later Phase 2 task.

## 4. Domain Model

Core concepts:

- **ScanSession** — one execution against one selected volume.
- **ScanNode** — file or directory discovered during scan.
- **PhysicalFileIdentity** — identity used to deduplicate hard-linked file content on supported filesystems.
- **DirectoryAggregate** — calculated descendant counts and byte totals.
- **RuleDefinition** — deterministic classification rule.
- **RuleMatch** — evidence that a rule applied to a node.
- **Classification** — category, risk, confidence, recommendation, and explanation.
- **ScanWarning** — recoverable issue associated with a path or session.
- **PresentationNode** — UI-oriented projection of a ScanNode with expansion state.

### Classification vocabulary

- `HighConfidenceReclaimable` — recognized disposable/cache/temp data with strong evidence; still not auto-deleted.
- `ReviewRecommended` — plausible cleanup candidate but requires user judgment.
- `LargeFileNotJunk` — space-heavy object without evidence of being junk.
- `Protected` — object/path that the application must not present as a normal deletion candidate.
- `Unknown` — insufficient evidence.
- `Warning` — node with scan/metadata limitations.

### Risk vocabulary

- `Low` — typically regeneratable or disposable based on known rule.
- `Medium` — may affect application state or user workflow.
- `High` — could break software/system behavior or destroy user data.
- `Protected` — not eligible for normal cleanup recommendation.

### Confidence vocabulary

- `High`, `Medium`, `Low`, `None`.

Risk and confidence are independent. A rule may be high-confidence that a file is an installer package but still assign high deletion risk.

## 5. Data Structure

Minimum top-level structures:

```text
ScanSession
- Id
- VolumeRoot
- VolumeSerial
- StartedAtUtc
- CompletedAtUtc?
- Status
- OptionsVersion
- RuleSetVersion
- Nodes[]
- Warnings[]
- Metrics

ScanNode
- Id
- ParentId?
- Name
- FullPath
- NodeType
- LogicalBytes
- AllocatedBytes?
- UniqueAllocatedBytes?
- CreatedUtc?
- ModifiedUtc?
- LastAccessUtc?
- Attributes
- IsReparsePoint
- ReparseTag?
- IsAccessible
- PhysicalIdentity?
- LinkCount?
- Classification
- MatchedRuleIds[]
- Aggregate
```

The implementation MAY normalize data across persistence tables, but must preserve the semantics above.

## 6. Algorithm

Top-level scan algorithm:

```text
Validate selected drive
Open ScanSession
Enumerate filesystem read-only
  -> capture metadata
  -> identify reparse points
  -> identify physical file identity when supported
  -> record warnings without aborting whole scan
Compute unique file accounting
Aggregate directories bottom-up
Evaluate classification rules
Build presentation projection
Persist immutable snapshot
Publish completed summary
```

The adaptive 10/40 rule MUST be applied only after enough size information exists to rank child directories. It is a presentation-depth rule, not permission to skip filesystem accounting.

## 7. State / Flow

Allowed ScanSession state transitions:

```text
Created -> Validating -> Scanning -> Aggregating -> Classifying -> Persisting -> Completed
                                \-> Cancelling -> Cancelled
Any active state ----------------> Failed
```

A recoverable path error MUST create a warning and continue. A session-level failure is reserved for conditions that make the scan result unusable, such as inability to inspect the selected root at all or unrecoverable persistence failure after analysis.

## 8. Error Handling

- Access denied: record warning, mark node/branch inaccessible, continue.
- File/directory disappears: record race-condition warning and continue.
- Metadata retrieval fails: preserve node with unknown fields where possible.
- Reparse target unavailable: do not follow; record metadata/warning if useful.
- Unsupported filesystem feature: degrade gracefully; never fabricate precise values.
- Drive removed/unmounted: cancel/fail session with explicit reason.
- Database write failure: do not mutate scanned tree; report snapshot persistence failure.
- Cancellation: stop accepting new work, drain safely, persist only if snapshot consistency policy permits; otherwise discard partial persistent snapshot while allowing transient UI diagnostics.

## 9. Edge Cases

- Empty drive or empty directory.
- Directory with millions of entries.
- Directory with >10 files but no child directories.
- Directory with >10 children and exactly one child directory.
- Hard-linked files appearing in different branches.
- Sparse/compressed files where logical and allocated bytes differ substantially.
- Reparse loops.
- Case-insensitive paths with unusual Unicode names.
- Paths longer than legacy MAX_PATH.
- Files modified while scan is running.
- User scans system drive without administrator privileges.
- Online-only/cloud placeholder whose logical size is larger than local allocation.
- A child directory becomes inaccessible after parent enumeration.

## 10. Performance Consideration

- Enumeration MUST be streaming; do not load entire directory listings unnecessarily before processing.
- Avoid reading file contents in V1.
- Avoid per-node synchronous UI dispatch.
- Batch progress updates.
- Directory aggregation should be O(N) over discovered nodes after enumeration.
- Rule evaluation should use precompiled/indexed conditions where practical, avoiding repeated expensive regex/path normalization.
- Persistence should use transactions/batching rather than one transaction per node.
- Memory usage must remain bounded enough for large consumer volumes; the exact budget is specified in the NFR document.

## 11. Testing Requirement

At minimum, end-to-end fixtures MUST cover:

- normal nested tree,
- access denied branch,
- hard links,
- symbolic link/junction/reparse point,
- sparse file if environment supports it,
- long path,
- files disappearing mid-scan,
- >10 child adaptive tree case,
- protected path classification,
- safe temp/cache rule classification,
- cancellation.

Tests MUST verify the scanner did not mutate any test input file by comparing file count, paths, content hashes for fixture files, and relevant timestamps where stable enough for assertion.

## 12. Acceptance Criteria

- [ ] A user can select C: or D: and start a read-only scan.
- [ ] No scan operation writes into the scanned directory tree.
- [ ] The application never auto-deletes or auto-moves user/system files.
- [ ] Full accounting and presentation-tree depth are separate concepts.
- [ ] Hard-link/reparse behavior is deterministic and documented.
- [ ] Recoverable filesystem errors do not crash the whole scan.
- [ ] Every cleanup-oriented classification includes an explainable reason and rule source.
- [ ] Large files are not automatically labeled as junk.
- [ ] Protected areas are represented distinctly from ordinary candidates.
- [ ] The 10/40 adaptive presentation rule is deterministic and testable.
- [ ] The product can operate without administrator rights, with reduced coverage reported explicitly.

## 13. Traceability

Detailed requirements are refined by:

- `01_architecture.md`
- `02_filesystem_scanner.md`
- `03_storage_accounting.md`
- `04_rule_engine.md`
- `05_adaptive_tree.md`
- `06_data_persistence.md`
- `07_ui_ux.md`
- `08_nonfunctional_security.md`
- `09_testing_acceptance.md`
