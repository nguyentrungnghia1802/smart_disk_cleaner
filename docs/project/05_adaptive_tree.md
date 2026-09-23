# 05 — Adaptive Tree Presentation Algorithm

**Document status:** Normative / Core behavior

## 1. Responsibility

Define the deterministic hierarchical presentation rule requested for large directories while preserving full scan data and on-demand discoverability.

Core product rule:

> If a directory has more than 10 immediate children (files + directories), automatic recursive expansion continues only into the top 40% largest immediate child directories. The ranking is based on reclaim-relevant recursive size. The other children remain represented/collapsed and can be opened manually.

## 2. Boundary

This policy controls default recursive presentation/expansion. It MUST NOT:

- alter scan coverage,
- remove nodes from snapshot,
- modify directory accounting,
- classify junk,
- prevent manual browsing/search of non-selected branches.

## 3. Input / Output

### Input

For one directory:

```text
ParentNode
ImmediateChildren[]
Each child has:
- type
- recursive size metrics
- classification summary
- stable NodeId/path
```

### Output

```text
PresentationDecision
- VisibleChildren[]
- AutoExpandDirectoryIds[]
- CollapsedDirectoryIds[]
- RankingMetric
- ThresholdTriggered
```

All immediate children remain visible when the parent is opened; only recursive auto-expansion is restricted.

## 4. Domain Model

Constants/configuration:

```text
ChildThreshold = 10
LargestFolderRatio = 0.40
Rounding = Ceiling
MinimumAutoExpandedFoldersWhenTriggered = 0 if no child directories; otherwise at least 1
```

V1 defaults are product requirements. Future settings MAY make them configurable, but persisted scan/presentation configuration must record values used.

## 5. Data Structure

```text
AdaptiveTreeOptions
- ChildThreshold: int = 10
- LargestFolderRatio: decimal = 0.40
- RankingPolicyVersion

RankedChildDirectory
- NodeId
- RankingBytes
- MetricKind
- StableTieBreaker
```

Presentation state (expanded/collapsed manually) belongs to UI/session state, not core snapshot accounting.

## 6. Algorithm

### 6.1 Trigger

Let:

```text
C = count(immediate files) + count(immediate directories)
D = count(immediate directories)
```

If `C <= 10`, all immediate child directories are eligible for automatic recursion.

If `C > 10`:

```text
K = ceil(D * 0.40)
```

If `D = 0`, `K = 0`.
If `D > 0`, `K` naturally becomes at least 1 with ceiling.

Rank the D child directories descending by the storage accounting ranking metric:

1. complete unique allocated recursive bytes,
2. else complete allocated recursive bytes,
3. else logical recursive bytes,
4. stable tie-break by normalized full path ordinal-ignore-case, then NodeId.

Auto-expand/recurse into the first K directories.

### 6.2 Important example

```text
Parent has 10 files + 6 directories = 16 immediate children
16 > 10
D = 6
K = ceil(6 * 0.40) = 3
```

All 16 immediate children are visible when parent is opened, but only top 3 directories are automatically expanded recursively.

### 6.3 Example: many files, one folder

```text
100 files + 1 directory = 101 children
K = ceil(1 * 0.40) = 1
```

The only directory continues recursive auto-expansion.

### 6.4 Example: 11 files, zero folders

```text
C = 11, D = 0, K = 0
```

No child directory exists to recurse into. Files remain visible.

### 6.5 Manual expansion

If user manually expands a collapsed directory, its immediate children are loaded from snapshot and shown. Within that manually opened branch, the same adaptive rule determines which descendant directories are auto-expanded further unless UI design deliberately treats manual expansion as one-level only. V1 recommended behavior: manual action opens requested node one level; subsequent automatic recursion applies the same policy below it.

### 6.6 Lazy UI projection

The UI SHOULD materialize presentation children lazily from snapshot indexes. Do not instantiate WPF view models for every node at startup if scan contains millions of entries.

## 7. State / Flow

```text
SnapshotReady
 -> RootMaterialized
 -> EvaluateChildren(parent)
 -> [<=10 => AutoExpand all child dirs]
 -> [>10 => Rank dirs => AutoExpand top K]
 -> User may manually expand any collapsed dir
```

Tree expansion state must not be written back as storage-accounting truth.

## 8. Error Handling

- Missing size metric: apply documented fallback ranking.
- Equal sizes: use deterministic tie breaker.
- Missing child node referenced by index: treat as snapshot integrity failure, log error, avoid crashing UI if possible.
- Invalid ratio/threshold config: reject configuration and use safe defaults; do not divide by invalid values.
- Integer calculation: use decimal/double carefully; recommended `K = (int)Math.Ceiling(D * ratio)` with validated ratio in `(0,1]`.

## 9. Edge Cases

- Exactly 10 children: no threshold trigger.
- 11 children, all files: zero recursive expansions.
- 11 children, 10 files + 1 dir: one dir expanded.
- 11 child directories: `ceil(4.4)=5` expanded.
- 25 child directories: 10 expanded.
- Allocated size unknown for some directories.
- Multiple child directories have equal sizes.
- Very large number of immediate children.
- Manually expanded branch was not in original top 40%.

## 10. Performance Consideration

For a parent with D child directories, full sort is O(D log D). This is acceptable initially. If profiling shows very large fan-out, a top-K selection algorithm O(D log K) MAY replace full sort as long as deterministic ordering/tie behavior is preserved.

UI virtualization/lazy view-model creation is more important than micro-optimizing normal directory sorts.

## 11. Testing Requirement

Table-driven unit tests MUST include:

| Immediate files | Immediate dirs | Total | Expected auto-expanded dirs |
|---:|---:|---:|---:|
| 5 | 5 | 10 | 5 |
| 6 | 5 | 11 | 2 |
| 10 | 1 | 11 | 1 |
| 11 | 0 | 11 | 0 |
| 10 | 6 | 16 | 3 |
| 0 | 11 | 11 | 5 |
| 0 | 25 | 25 | 10 |

Also test fallback metric selection and deterministic ties.

## 12. Acceptance Criteria

- [ ] The 10-child threshold uses files + directories.
- [ ] The 40% calculation uses only child directories as recurse candidates.
- [ ] Rounding uses ceiling.
- [ ] Full snapshot data is never pruned by this rule.
- [ ] All immediate children remain accessible.
- [ ] Ranking prefers reclaim-relevant allocated metrics.
- [ ] Equal-size ordering is deterministic.
- [ ] Manual expansion can access a branch outside the top 40%.
