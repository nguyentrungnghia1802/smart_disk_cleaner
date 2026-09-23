# 08 — Non-Functional, Safety & Security Requirements

**Document status:** Normative

## 1. Responsibility

Define non-functional requirements for safety, correctness, privacy, responsiveness, resource use, logging, reliability, and maintainability.

## 2. Boundary

These requirements apply across scanner, rule engine, persistence, and UI. The application is a local desktop analyzer and should not require cloud connectivity for core functionality.

## 3. Input / Output

Inputs include local filesystem metadata, configuration, rule files, and user commands. Outputs include local snapshots, logs, UI results, and explicit exports.

No telemetry/network upload is required for V1. If future telemetry is introduced, it requires a separate privacy/spec change and MUST NOT be added silently.

## 4. Domain Model

Cross-cutting concepts:

```text
SafetyInvariant
PerformanceBudget
ResourceBudget
PrivacyPolicy
AuditLogEvent
ConfigurationVersion
```

Key safety invariants:

1. No automatic deletion.
2. No write into analyzed namespace from scan/classification; the exact app-owned data root is a reserved exclusion when it lies on the selected drive.
3. Reparse points not followed by default.
4. Unknown metadata never converted into false certainty.
5. Protected rules dominate generic cleanup rules.
6. Cancellation never leaves app-owned persistent snapshot falsely marked complete.

## 5. Data Structure

Local logs should be structured enough to diagnose failures:

```text
TimestampUtc
Level
EventId
SessionId?
Operation
Path? (local-only)
ExceptionType?
Message
DurationMs?
Counters?
```

Logs are app-owned data. They should rotate/retain within reasonable limits.

Configuration should include schema/version and validated settings; invalid config falls back to documented defaults.

## 6. Algorithm

### 6.1 Safety enforcement

Safety should be enforced structurally, not only by UI:

- scanner interfaces expose read metadata operations, not delete methods,
- core domain has no delete use case,
- infrastructure project for V1 contains no cleanup/delete service required by normal workflow,
- app-data writer validates target path belongs to allowed app-data/export destination context,
- scanner excludes the exact app-owned data root when it is located on the selected volume,
- protection policy is evaluated before candidate classification.

### 6.2 Performance budgets

Because hardware varies widely, requirements are responsiveness-oriented rather than absolute scan-time promises.

Targets:

- UI should remain interactive during scan.
- Cancellation acknowledgement should normally be visible within about 1 second; underlying blocked OS calls may delay full stop.
- Progress UI updates should be throttled to a human-useful rate (for example several updates per second, not per node).
- Memory should scale approximately O(N) only where snapshot data genuinely requires it; UI-object count must scale with visible/materialized nodes, not all nodes.
- No file-content hashing in V1.

### 6.3 Privacy

- Core app works offline.
- No scanned path/file metadata is transmitted externally in V1.
- Export is explicit.
- Logs remain local.
- Do not log file content.

## 7. State / Flow

Safety lifecycle:

```text
User selects drive
 -> scanner reads metadata
 -> analysis runs locally
 -> snapshot stored locally
 -> user reviews
 -> optional explicit export
```

There is no implicit upload or cleanup stage.

## 8. Error Handling

- Unexpected exception must be caught at process/UI boundary, logged locally, and presented safely.
- Do not continue after a detected invariant corruption that could produce misleading totals; mark scan failed/partial.
- App-owned log/persistence failure must not trigger fallback writes into scanned locations.
- Invalid export destination is rejected without changing scan data.

## 9. Edge Cases

- App is run from a directory inside the drive being scanned.
- App-data database itself is discovered by scan.
- Very low free disk space prevents snapshot persistence.
- User kills process mid-save.
- Antivirus temporarily locks app database or scanned metadata handles.
- Local machine uses non-English Windows paths.
- Device is HDD vs SSD.
- NTFS vs another filesystem on D:.

## 10. Performance Consideration

- Correctness before aggressive parallelism.
- Profile before introducing complexity.
- Native handle counts and memory usage should be observable in development diagnostics.
- Use bounded concurrency.
- Avoid keeping file handles open longer than necessary.
- SQLite writes should be batched.
- Large trees require lazy presentation.

## 11. Testing Requirement

- Static/code review confirms no delete/move/rename path in V1 core workflow.
- Mutation guard integration tests.
- Offline test: core scan works without network.
- Cancellation stress tests.
- Large synthetic snapshot memory/UI tests.
- Persistence failure injection.
- Invalid config fallback.
- Rule safety invariant tests.
- Reparse loop fixture test.

## 12. Acceptance Criteria

- [ ] Core functionality works offline.
- [ ] No scanned metadata is uploaded by V1.
- [ ] UI remains responsive during long scans.
- [ ] Bounded concurrency is used if parallelism is introduced.
- [ ] Logs/config/database stay in app-owned data area.
- [ ] No fallback writes occur inside scan tree on errors.
- [ ] Safety invariants have automated tests.
- [ ] Unknown/partial results are visibly distinguishable from exact results.
