# 07 — WPF UI / UX Specification

**Document status:** Normative for V1 presentation

## 1. Responsibility

Define user-visible workflows, tree behavior, state presentation, warning language, node detail view, filters, progress, and safety affordances. The UI must make uncertainty and protection visible rather than implying that every candidate is safe to delete.

## 2. Boundary

V1 UI provides analysis/review only. It MAY provide `Open in Explorer` and `Copy Path`. It MUST NOT expose a delete button, automatic cleanup command, or bulk-delete action.

## 3. Input / Output

### Input

- application state,
- scan progress,
- persisted/current snapshot,
- presentation-tree policy decisions,
- search/filter terms,
- selected node.

### Output

- start/cancel scan commands,
- selected drive,
- tree navigation state,
- search/filter requests,
- copy/open commands,
- explicit export request in Phase 2.

## 4. Domain Model

Primary view models:

```text
MainWindowViewModel
DriveSelectorViewModel
ScanProgressViewModel
SummaryViewModel
TreeNodeViewModel
NodeDetailsViewModel
FilterBarViewModel
WarningsViewModel
```

UI category semantics:

- High-confidence reclaimable
- Review recommended
- Large / not junk
- Protected
- Unknown
- Warning/incomplete

Do not rely on color alone; pair color/icon with text labels for accessibility.

## 5. Data Structure

`TreeNodeViewModel` should be lazy:

```text
NodeId
DisplayName
DisplayPath
PrimarySize
SecondarySize?
Category
Risk
Confidence
HasChildren
IsExpanded
IsLoadingChildren
Children (materialized only when needed)
AutoExpansionReason?
```

Node detail projection:

```text
Path
Type
Logical size
Allocated/local size
Unique allocated estimate if meaningful
Created/modified/access timestamps
Attributes
Reparse/link information
Category
Risk
Confidence
Primary reason
Matched rules
Warnings
```

## 6. Algorithm

### 6.1 Main workflow

```text
Launch
 -> choose C: or D:
 -> Start Scan
 -> live progress + Cancel
 -> scan completes
 -> summary + adaptive tree
 -> user browses/searches/filters
 -> optional Open in Explorer / Copy Path
 -> optional explicit export (Phase 2)
```

### 6.2 Tree behavior

- Root appears after stable snapshot is ready in V1; progressive tree during scan is optional.
- Children materialize lazily.
- Adaptive 10/40 policy controls automatic recursive expansion.
- Manually expanding a collapsed node must always be allowed if snapshot has children.
- Protected nodes remain navigable but visually distinct.
- Reparse nodes indicate "not followed".
- Inaccessible nodes indicate coverage limitation.

### 6.3 Summary

Display at least:

```text
Selected drive
Scan duration
Discovered files/directories
Known/partial accounting status
Candidate bytes by category
Protected bytes (informational, not reclaimable)
Warnings/inaccessible count
```

Avoid one misleading "You can free X GB" number when estimates include medium/high-risk or partial accounting. Prefer separate totals.

### 6.4 Search/filter Phase 2

Useful filters:

- category,
- risk,
- minimum size,
- extension,
- path/name text,
- protected only,
- warnings only,
- reparse points.

Search results should preserve path context or allow jump-to-tree-node.

## 7. State / Flow

UI states:

```text
Idle
Scanning
Cancelling
ProcessingAccounting
Classifying
Saving
Ready
Error
```

The UI MUST not show 100% complete while aggregation/classification is still running unless progress label makes the stage distinction explicit.

## 8. Error Handling

- Root unavailable: blocking error with clear retry/change-drive action.
- Access denied in branch: non-blocking warning count + branch marker.
- Snapshot save failed: show analysis result if available, with "not saved" status.
- Open in Explorer target vanished: non-fatal notification.
- Copy path failure: non-fatal notification.
- Fatal unexpected error: log detailed app error locally and show concise user-safe message.

## 9. Edge Cases

- D: not present.
- Scan canceled before any file found.
- Scan completed with thousands of warnings.
- Tree node has millions of immediate files; UI must virtualize or page/lazy-load enough to remain responsive.
- Very long path/name display.
- High DPI/scaling.
- User resizes columns/window.
- Selected node disappears from disk after scan; snapshot remains historical and should be labeled as such where needed.

## 10. Performance Consideration

- Enable UI virtualization where compatible with tree implementation.
- Do not create one WPF object per scan node upfront.
- Batch progress notifications.
- Avoid recalculating aggregates in bindings.
- Use background application operations; UI thread only handles presentation updates.
- For huge child lists, consider incremental loading/paging in Phase 2 if virtualization is insufficient.

## 11. Testing Requirement

- View-model tests for command enablement by state.
- UI automation/smoke tests for Start/Cancel/Ready flow.
- Tree policy integration tests verify auto-expanded count.
- Protected/reparse/inaccessible labels visible.
- No Delete command exists in V1.
- Large synthetic tree responsiveness smoke test.
- Accessibility check: classification not communicated by color only.

## 12. Acceptance Criteria

- [ ] User can choose C: or D: when available.
- [ ] Start and Cancel states behave correctly.
- [ ] Tree is hierarchical, lazy, and respects adaptive policy.
- [ ] User can manually access collapsed branches.
- [ ] Node detail explains classification and uncertainty.
- [ ] Protected and inaccessible nodes are clearly distinguished.
- [ ] UI exposes no automatic or direct deletion capability in V1.
- [ ] Large-file nodes are not visually presented as equivalent to junk candidates.
