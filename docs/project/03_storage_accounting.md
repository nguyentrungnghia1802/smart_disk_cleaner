# 03 — Storage Accounting & Filesystem Correctness

**Document status:** Normative / Phase 1 critical

## 1. Responsibility

Define how file and directory sizes are represented and aggregated accurately enough for cleanup analysis, especially around logical size, allocated size, hard links, sparse/compressed files, cloud placeholders, and unsupported metadata.

## 2. Boundary

This component calculates facts about storage. It does not decide junk status and does not delete data.

It MUST distinguish:

- logical byte length,
- allocated bytes when known,
- unique allocated bytes for deduplicated hard-linked physical content,
- directory aggregate values,
- unknown/incomplete metrics.

## 3. Input / Output

### Input

A complete or finalized set of raw scan nodes for a session, including optional physical identities and allocated-size metadata.

### Output

For each file:

```text
LogicalBytes
AllocatedBytes?
UniqueAllocatedBytes?
AccountingConfidence
```

For each directory:

```text
DirectFileCount
DirectDirectoryCount
DescendantFileCount
DescendantDirectoryCount
LogicalBytesRecursive
AllocatedBytesRecursive?
UniqueAllocatedBytesRecursive?
IncompleteDescendantCount
```

## 4. Domain Model

### PhysicalFileIdentity

On filesystems/providers where stable file identity is available:

```text
PhysicalFileIdentity
- VolumeIdentity
- FileIdentity
```

Do not use normalized path as physical identity: hard links intentionally have different paths for the same physical file.

### AccountingConfidence

- `ExactWithinCapturedMetadata`
- `Partial`
- `Unsupported`

No fake zero: unknown allocated size MUST be `null`/unknown, not zero.

## 5. Data Structure

Use a session-level map:

```text
Dictionary<PhysicalFileIdentity, PhysicalFileAggregate>
```

Where:

```text
PhysicalFileAggregate
- FirstSeenNodeId
- LogicalBytes
- AllocatedBytes?
- SeenPathCount
- LinkCountReported?
```

Directory aggregation can use node IDs and parent IDs. A bottom-up ordering can be obtained by depth descending or explicit post-order over the parent graph.

## 6. Algorithm

### 6.1 File accounting

For each regular file:

1. Preserve reported logical length.
2. Preserve allocated size if provider reports it.
3. If physical identity is known:
   - first occurrence gets `UniqueAllocatedBytes = AllocatedBytes` if known,
   - subsequent occurrences get `UniqueAllocatedBytes = 0` for whole-volume unique accounting,
   - all path nodes still retain their own `AllocatedBytes` for path-level display.
4. If physical identity is unknown:
   - cannot safely deduplicate,
   - set unique metric according to explicit fallback policy and mark it partial; do not claim exactness.

### 6.2 Directory aggregation

Aggregate bottom-up:

```text
for node in nodes ordered by depth descending:
  parent.Logical += node.LogicalRecursiveOrSelf
  parent.Allocated += node.AllocatedRecursiveOrSelf when known policy permits
  parent.UniqueAllocated += node.UniqueAllocatedRecursiveOrSelf
  parent counts += child counts
```

Allocated recursive totals require a clear unknown policy. Recommended:

- store `KnownAllocatedBytes` plus `UnknownAllocatedItemCount`,
- UI may display `>= X GB` or `partial` when unknown descendants exist,
- do not silently treat unknown as zero in claims of exact size.

### 6.3 Ranking metric for 40% rule

Use the best available reclaim-relevant metric in this order:

1. `UniqueAllocatedBytesRecursive` when sufficiently complete,
2. `AllocatedBytesRecursive` when complete,
3. `LogicalBytesRecursive` as fallback,
4. stable path/name tie-breakers.

The snapshot MUST record which metric was used for ranking when fallback occurs.

### 6.4 Hard-link semantics

Two views are useful:

- **Path size view:** what size is associated with each path entry.
- **Unique volume usage view:** count physical allocation only once.

Session summary MUST prefer unique allocated bytes where exact/available. A directory path display may show path-associated allocated bytes but should expose hard-link information to prevent misleading reclaim estimates.

### 6.5 Cloud placeholders

When allocated size indicates little/no local allocation despite large logical size, reclaim estimate must use local allocated bytes, not logical size. Classification MAY identify cloud placeholder status if metadata adapter supports it, but storage accounting remains factual.

## 7. State / Flow

```text
RawNodesReady
 -> PhysicalIdentityIndexBuilt
 -> FileAccountingResolved
 -> DirectoryAggregationResolved
 -> AccountingMetricsFinalized
```

Classification MUST run only after accounting is sufficiently finalized for size-dependent rules.

## 8. Error Handling

- Allocated-size query fails: preserve logical size, mark allocated unknown.
- File identity query fails: preserve path node, mark dedupe unavailable for node.
- Conflicting physical identity metadata: log invariant warning and use conservative partial accounting.
- Integer overflow risk: use 64-bit checked arithmetic or safe overflow detection; never wrap silently.
- Directory graph corruption: fail aggregation stage rather than emit invalid totals.

## 9. Edge Cases

- 0-byte file with nonzero allocation metadata.
- Sparse file with huge logical length and small allocation.
- Compressed file.
- Multiple hard links in same directory.
- Hard links in different branches.
- Hard-link count larger than number of paths visible in scanned scope.
- File modified between logical-size and allocated-size queries.
- Unknown allocated sizes mixed with known descendants.
- Directory itself consumes metadata allocation not represented by child file sizes; V1 may omit directory-entry/MFT overhead but must state that file allocation totals are estimates of reclaimable data, not a byte-perfect raw volume accounting of filesystem metadata.

## 10. Performance Consideration

- Physical identity map lookup should be O(1) average.
- Aggregation should be O(N).
- Avoid resorting entire node set repeatedly; compute depth once or use parent child counts/post-order.
- Allocate compact structs/records for hot accounting data where profiling indicates benefit.
- Avoid big-integer math; 64-bit signed bytes are adequate for supported consumer volumes, but protect against malformed/overflow inputs.

## 11. Testing Requirement

Required numeric tests:

- ordinary files: logical = expected sum,
- hard links: path allocated may appear multiple times, unique allocated counts once,
- unknown physical identity: exact flag becomes partial,
- sparse file where integration environment supports creation,
- directory aggregate across multiple depths,
- unknown allocated descendant propagation,
- deterministic ranking fallback,
- checked overflow path.

Golden fixture example:

```text
root/
  A.bin 100 allocated 128 physical X
  hardlink-to-A.bin 100 allocated 128 physical X
  B.bin 50 allocated 64 physical Y

Path-associated allocated = 320
Unique allocated = 192
Logical path sum = 250
```

The exact fixture allocation units may vary in integration environments; unit tests should inject deterministic metadata.

## 12. Acceptance Criteria

- [ ] Logical and allocated size are separate fields.
- [ ] Unknown allocated size is never represented as a truthful zero.
- [ ] Hard-linked physical data can be deduplicated when identity metadata is available.
- [ ] Directory aggregation is deterministic and O(N) after node discovery.
- [ ] Session summary does not knowingly double-count hard-linked physical allocation.
- [ ] Ranking for adaptive tree uses allocated/reclaim-relevant size when available.
- [ ] Partial accounting is clearly marked and propagated.
- [ ] Numeric overflow cannot silently corrupt totals.
