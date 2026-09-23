# 02 — Read-Only Filesystem Scanner Specification

**Document status:** Normative / Phase 1 critical

## 1. Responsibility

Specify exact behavior of the read-only scanner responsible for discovering filesystem entries and collecting metadata without mutating the scanned tree.

## 2. Boundary

Scanner responsibilities:

- validate and enumerate selected drive,
- emit a node for discoverable files/directories,
- collect metadata needed by later accounting/classification,
- identify reparse points before recursion,
- attempt physical file identity/allocated-size retrieval through adapters,
- isolate recoverable I/O errors,
- honor cancellation.

Scanner MUST NOT:

- classify junk,
- decide whether user should delete an item,
- compute final directory aggregates,
- write into scanned paths,
- follow reparse targets by default,
- read file content in V1.

## 3. Input / Output

### Input

```text
ScanRequest
- VolumeRoot
- FollowReparsePoints = false (V1 fixed)
- IncludeHidden = true
- IncludeSystem = true where access allows
- IgnoreInaccessible = true
- MetadataLevel = Standard+NativeAccounting
- CancellationToken
```

### Output

Streaming/batched `RawScanNode` records plus warnings and scan counters.

```text
RawScanNode
- TemporaryId
- ParentTemporaryId?
- FullPath
- Name
- Type
- Attributes
- LogicalBytes
- AllocatedBytes?
- PhysicalIdentity?
- LinkCount?
- ReparseTag?
- CreatedUtc?
- ModifiedUtc?
- LastAccessUtc?
- MetadataFlags
```

## 4. Domain Model

### MetadataFlags examples

- `AllocatedSizeKnown`
- `PhysicalIdentityKnown`
- `ReparsePointDetected`
- `CloudPlaceholderSuspected`
- `MetadataPartial`
- `AccessRestricted`

### ScanWarning fields

```text
Id
SessionId
Path
Operation
Code
MessageSafe
ExceptionType?
OccurredAtUtc
Severity
Recoverable
```

Warning messages presented to user must avoid dumping irrelevant stack traces.

## 5. Data Structure

Scanner implementation should use an explicit work queue/stack rather than recursive C# method calls to avoid stack depth hazards.

Example:

```text
PendingDirectory
- NodeId
- Path
- Depth
```

Maintain visited physical directory identity only if directory identity is reliably available. Because V1 does not follow reparse points, reparse cycles are already prevented by policy. The scanner still MUST guard against accidental recursion into a detected reparse directory.

## 6. Algorithm

### 6.1 Root validation

1. Normalize requested root.
2. Confirm it refers to an allowed drive root for V1.
3. Confirm drive exists and is ready.
4. Capture filesystem/volume information.
5. Resolve the application-owned data root and register it as an exact reserved exclusion if it is located under the selected volume.
6. Create root node.
7. Begin iterative enumeration.

### 6.2 Directory enumeration

For each real directory:

```text
check cancellation
try enumerate immediate children
for each child:
  check cancellation periodically
  if child is the reserved application-owned data root:
     emit an informational exclusion marker/warning if desired
     do not traverse or analyze that subtree
     continue
  read base metadata
  if file:
     capture logical size
     capture allocated size if provider supports it
     capture physical identity/link count if provider supports it
     emit node
  if directory:
     detect reparse attribute/tag first
     emit node
     if reparse point:
        DO NOT enqueue target traversal
     else:
        enqueue child directory
catch recoverable exception:
  emit warning
  continue
```

### 6.3 Reparse policy

`FollowReparsePoints` MUST be false in V1. A reparse directory itself appears in the result, but its target is not recursively scanned through that path. This prevents loops, cross-volume traversal, duplicated accounting, and unexpected scope expansion.

### 6.4 Race conditions

The filesystem is live. A file may disappear or change between enumeration and metadata retrieval. The scanner MUST accept this as normal race behavior:

- do not retry indefinitely,
- record a warning only when useful,
- continue scan,
- tag uncertain metrics as partial rather than inventing values.

### 6.5 Path handling

Use APIs compatible with long Windows paths. Do not implement logic that truncates or rejects a path solely because it exceeds legacy 260-character limits.

### 6.6 Privileges

V1 runs with normal user privileges by default. Missing access must be represented as incomplete coverage. The application MUST NOT silently relaunch elevated unless a future requirement explicitly adds that behavior.

## 7. State / Flow

Per-directory state:

```text
Discovered -> MetadataRead ->
  [ReparsePoint -> NotTraversed]
  [NormalDirectory -> Queued -> Enumerating -> Enumerated]
  [AccessError -> Inaccessible]
```

Per-file state:

```text
Discovered -> MetadataRead -> Emitted
                       \-> PartialMetadata
                       \-> Vanished
```

## 8. Error Handling

Recoverable examples:

- UnauthorizedAccessException
- SecurityException
- DirectoryNotFoundException
- FileNotFoundException
- IOException for individual path
- metadata/native call fails for one item

Potential session-fatal examples:

- selected root not available,
- root cannot be enumerated at all and no meaningful result can be produced,
- invariant corruption in scanner bookkeeping.

The scanner MUST NOT use an empty catch that hides failure semantics.

## 9. Edge Cases

- Directory has no children.
- File has zero bytes.
- File name contains trailing/odd Unicode representation supported by filesystem.
- Deep directory nesting.
- Reparse point points back to ancestor.
- Mount point points to another drive.
- File is locked for content access but metadata is readable.
- File cannot be opened for physical identity but can be enumerated.
- Directory deleted after being queued.
- Drive eject/removal during scan.
- Millions of small files.
- Junction located under user profile.
- System-protected directory visible but not enumerable.

## 10. Performance Consideration

- Prefer enumeration APIs that stream entries.
- Minimize opening a file handle multiple times; if native metadata calls can share one handle safely, encapsulate that optimization.
- Do not hash contents.
- Do not calculate duplicate content.
- Progress updates should be emitted every batch/time interval, not every file.
- Use bounded buffers if producer/consumer architecture is adopted.
- First implementation MAY be mostly sequential to establish correctness baselines.

## 11. Testing Requirement

Unit tests with abstraction/fakes:

- does not enqueue reparse directory,
- continues after access denied,
- cancellation stops traversal,
- disappearing path does not fail whole scan,
- partial native metadata remains representable.

Integration fixtures on Windows:

- real nested folders/files,
- real junction/symbolic link if CI privileges permit,
- long path,
- hard link,
- read-restricted directory if environment permits,
- concurrent delete during scan.

Mutation test:

1. Create fixture tree.
2. Hash file contents and record path set before scan.
3. Run scanner.
4. Verify file content hashes and path set unchanged.

## 12. Acceptance Criteria

- [ ] Scanner performs no application-originated write inside scanned tree.
- [ ] Scanner does not follow reparse points in V1.
- [ ] Scanner can return useful partial results when some branches are inaccessible.
- [ ] Long path handling is supported by implementation choices.
- [ ] Cancellation is responsive and leaves no leaked handles.
- [ ] File content is never required for V1 scanning.
- [ ] Each node clearly indicates when native accounting metadata is unknown.
- [ ] Live filesystem race conditions are handled without unbounded retries.
