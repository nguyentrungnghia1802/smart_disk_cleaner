# 06 — Domain Data & Persistence Specification

**Document status:** Normative

## 1. Responsibility

Define persisted entities, snapshot consistency rules, application-data location policy, schema evolution, indexing, and storage behavior. Persistence exists to support reopening scan results, querying large trees efficiently, and preserving evidence used by classifications.

## 2. Boundary

V1 persistence uses SQLite in the application's private data directory. If that directory physically resides on the selected drive, the scanner MUST exclude the exact application-owned data root from the analysis namespace. This reserved exclusion is the mechanism that reconciles persistent app writes with the read-only scan contract: the application may mutate only its own excluded data root, never an analyzed path.

The repository does not own filesystem cleanup actions.

## 3. Input / Output

### Input

- finalized ScanSession metadata,
- finalized ScanNode records,
- rule matches,
- warnings,
- aggregate metrics,
- rule-set/options versions.

### Output

- immutable/reopenable ScanSnapshot,
- indexed child queries,
- node detail queries,
- search/filter queries,
- snapshot list/delete operations limited to app-owned data.

## 4. Domain Model

Recommended persisted entities:

```text
ScanSession
ScanNode
ScanWarning
RuleMatch
SessionMetric
SchemaInfo
```

Optional Phase 2 entities:

```text
UiPreference
RecentDrive
ExportHistory
```

Deleting old snapshots from the application database is allowed because that mutates only app-owned data, not scanned filesystem content.

## 5. Data Structure

Suggested SQLite schema (conceptual):

```text
scan_sessions(
  id TEXT PK,
  volume_root TEXT NOT NULL,
  volume_identity TEXT,
  started_utc TEXT NOT NULL,
  completed_utc TEXT,
  status INTEGER NOT NULL,
  options_version TEXT NOT NULL,
  ruleset_version TEXT NOT NULL,
  accounting_confidence INTEGER NOT NULL,
  node_count INTEGER NOT NULL,
  warning_count INTEGER NOT NULL
)

scan_nodes(
  id INTEGER PK,
  session_id TEXT NOT NULL,
  parent_id INTEGER NULL,
  depth INTEGER NOT NULL,
  name TEXT NOT NULL,
  full_path TEXT NOT NULL,
  node_type INTEGER NOT NULL,
  logical_bytes INTEGER NOT NULL,
  allocated_bytes INTEGER NULL,
  unique_allocated_bytes INTEGER NULL,
  recursive_logical_bytes INTEGER NOT NULL,
  recursive_allocated_bytes INTEGER NULL,
  recursive_unique_allocated_bytes INTEGER NULL,
  child_file_count INTEGER NOT NULL,
  child_directory_count INTEGER NOT NULL,
  descendant_file_count INTEGER NOT NULL,
  descendant_directory_count INTEGER NOT NULL,
  created_utc TEXT NULL,
  modified_utc TEXT NULL,
  last_access_utc TEXT NULL,
  attributes INTEGER NOT NULL,
  is_reparse_point INTEGER NOT NULL,
  reparse_tag INTEGER NULL,
  is_accessible INTEGER NOT NULL,
  physical_identity TEXT NULL,
  link_count INTEGER NULL,
  category INTEGER NOT NULL,
  risk INTEGER NOT NULL,
  confidence INTEGER NOT NULL,
  primary_reason_code TEXT NULL,
  metadata_flags INTEGER NOT NULL,
  FOREIGN KEY(session_id) REFERENCES scan_sessions(id)
)

rule_matches(
  session_id TEXT NOT NULL,
  node_id INTEGER NOT NULL,
  rule_id TEXT NOT NULL,
  is_primary INTEGER NOT NULL,
  evidence_json TEXT NULL
)

scan_warnings(
  id INTEGER PK,
  session_id TEXT NOT NULL,
  node_id INTEGER NULL,
  path TEXT NULL,
  operation TEXT NOT NULL,
  code TEXT NOT NULL,
  severity INTEGER NOT NULL,
  message_safe TEXT NOT NULL
)
```

Recommended indexes:

```text
scan_nodes(session_id, parent_id)
scan_nodes(session_id, full_path)
scan_nodes(session_id, category)
scan_nodes(session_id, risk)
scan_nodes(session_id, recursive_unique_allocated_bytes DESC)
scan_warnings(session_id)
rule_matches(session_id, node_id)
```

## 6. Algorithm

### 6.1 Snapshot transaction

1. Open repository transaction.
2. Insert session in `Persisting` state.
3. Bulk/batch insert nodes.
4. Bulk insert rule matches and warnings.
5. Insert metrics.
6. Update session to `Completed` with final counts.
7. Commit transaction.

If any mandatory insert fails, rollback entire snapshot transaction. Do not leave a session that appears Completed with missing nodes.

### 6.2 Snapshot immutability

Completed snapshot rows are immutable in V1. Reclassification against a newer ruleset should create a new derived snapshot/version in a future feature; do not silently rewrite historical classification evidence.

### 6.3 App-owned data on the selected drive

The live SQLite/log/config files may change while scanning C:. Therefore:

- resolve the exact app-owned data root before traversal,
- if it is under the selected volume, exclude that entire root from analysis,
- do not create ScanNodes for descendants of that reserved root,
- optionally expose one informational summary that an app-owned subtree was intentionally excluded,
- never broaden the exclusion to a parent such as the whole user AppData directory.

This prevents self-observation, recursive scan growth, and violation of the write policy while keeping the exclusion minimal and deterministic.

### 6.4 Retention

Phase 2 may keep a configurable number of completed snapshots. Default can be simple (e.g. latest few), but retention deletion MUST operate only on app-owned SQLite data.

## 7. State / Flow

```text
NoSnapshot
 -> BuildingInMemory
 -> PersistingTransaction
 -> CompletedSnapshot

PersistingTransaction -> RollbackOnFailure -> NoCompletedSnapshot
```

Schema migration flow:

```text
Open DB -> Read schema version -> Migrate app-owned DB transactionally -> Ready
```

## 8. Error Handling

- Database locked: bounded retry for app-owned contention only; fail clearly after limit.
- Corruption: do not attempt risky magic repair. Offer app-data reset/recreate workflow in Phase 2.
- Disk full while persisting: rollback, report that analysis completed but snapshot could not be saved if in-memory result remains available.
- Schema migration fails: rollback migration and prevent writes with incompatible schema.
- Invalid foreign-key relation: treat as implementation invariant violation.

## 9. Edge Cases

- Millions of nodes.
- Extremely long full paths.
- Duplicate path observations caused by race/reparse bugs; schema should not assume global full-path uniqueness across sessions.
- Null allocated metrics.
- Snapshot interrupted during process termination.
- App database lies on same drive being scanned; exact app-owned root is excluded.
- Unicode normalization differences.
- Old snapshot uses older rule-set version.

## 10. Performance Consideration

- Enable appropriate SQLite WAL mode only after validating lifecycle and backup semantics for a desktop app.
- Use one transaction for large batch or chunked transactions with an atomic finalization strategy; avoid commit per node.
- Use prepared statements.
- Child lookup by `(session_id,parent_id)` must be indexed.
- Do not load all nodes into WPF view models when reopening large snapshots.
- Search should use indexed fields first; full-text index is optional and probably unnecessary for V1.

## 11. Testing Requirement

- Persist/reload snapshot equality for representative data.
- Transaction rollback on injected failure.
- Millions-node synthetic insertion benchmark at reduced CI scale plus local large benchmark.
- Null accounting fields survive round trip.
- Unicode/long path round trip.
- Old schema migration test once first migration exists.
- Completed snapshot cannot be partially visible after failed save.
- Repository operations never write to a fixture scanned directory.

## 12. Acceptance Criteria

- [ ] SQLite is stored only under application-owned data location.
- [ ] Completed snapshots are internally consistent and transactionally saved.
- [ ] Child queries are indexed by session + parent.
- [ ] Rule-set and options versions are persisted.
- [ ] Unknown numeric values remain null/unknown after round trip.
- [ ] Historical scan evidence is not silently rewritten.
- [ ] Repository failure never causes mutation of scanned files.
