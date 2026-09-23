# 01 — Architecture Specification

**Document status:** Normative  
**Architecture style:** Modular monolith  
**UI pattern:** WPF + MVVM

## 1. Responsibility

Define the component boundaries, dependency direction, execution model, module contracts, concurrency rules, cancellation strategy, and deployment assumptions for Smart Disk Cleaner Analyzer.

## 2. Boundary

The V1 application is a single Windows desktop process. It MUST NOT require a Windows service, kernel driver, browser, cloud backend, or microservices. Native Windows APIs MAY be wrapped behind infrastructure interfaces when .NET abstractions do not expose enough metadata.

Recommended solution layout:

```text
src/
  SmartDiskCleaner.App/              # WPF composition root, views, resources
  SmartDiskCleaner.Application/      # use cases, orchestration, DTOs
  SmartDiskCleaner.Domain/           # pure domain models/policies
  SmartDiskCleaner.Infrastructure/   # filesystem/native API/SQLite implementations
  SmartDiskCleaner.Rules/            # built-in rule definitions + parsing

tests/
  SmartDiskCleaner.Domain.Tests/
  SmartDiskCleaner.Application.Tests/
  SmartDiskCleaner.Infrastructure.Tests/
  SmartDiskCleaner.IntegrationTests/
```

For a personal project, these MAY be consolidated into fewer projects if build complexity becomes excessive, but logical dependency boundaries MUST remain.

## 3. Input / Output

### Architecture inputs

- Scan request from UI.
- Cancellation token.
- Scan options.
- Rule set.
- Filesystem metadata adapter.
- Snapshot repository.

### Architecture outputs

- Progress events/read models.
- Completed immutable ScanSnapshot.
- Domain warnings.
- UI-ready queries/projections.

## 4. Domain Model

Primary application services:

- `StartScanUseCase`
- `CancelScanUseCase`
- `GetScanSnapshotQuery`
- `GetPresentationChildrenQuery`
- `SearchScanNodesQuery`
- `ExportScanResultUseCase` (Phase 2)

Infrastructure interfaces:

```csharp
IFileSystemScanner
IFileMetadataReader
IPhysicalFileIdentityProvider
IAllocatedSizeProvider
IVolumeInfoProvider
IScanSnapshotRepository
IRuleSetProvider
IAppPathProvider
IClock
```

Domain services:

```text
StorageAccountingService
DirectoryAggregationService
ClassificationService
AdaptiveTreePolicy
ProtectionPolicy
```

## 5. Data Structure

Application layer uses immutable or effectively immutable records after each pipeline stage. Mutable builder structures MAY be used internally during high-volume scanning for performance, but MUST NOT leak into UI or persistence contracts.

Recommended aggregate:

```text
ScanSnapshot
- Session
- IReadOnlyList<ScanNodeRecord>
- IReadOnlyList<ScanWarning>
- Metrics
- Indexes
  - NodeById
  - ChildrenByParentId
  - Optional normalized-path index
```

UI projections MUST NOT be the source of truth for accounting.

## 6. Algorithm

### 6.1 Orchestration pipeline

```text
StartScanUseCase
  1. validate request
  2. create session id
  3. capture volume metadata
  4. run scanner
  5. finalize physical identity accounting
  6. aggregate directories bottom-up
  7. classify nodes
  8. derive session metrics
  9. persist snapshot transactionally
 10. expose presentation root
```

### 6.2 Dependency rule

```text
App -> Application -> Domain
App -> Infrastructure (composition only)
Application -> Domain
Infrastructure -> Domain/Application contracts
Domain -> nothing infrastructure-specific
```

Domain MUST NOT reference WPF, SQLite, P/Invoke, registry APIs, or concrete filesystem classes.

### 6.3 Concurrency

- One scan session at a time in V1 is sufficient.
- Filesystem enumeration MAY use bounded parallelism only after correctness is proven.
- Do not parallelize blindly across directories; HDD random seeks and NTFS metadata contention can make performance worse.
- All parallel operations MUST honor cancellation.
- Progress aggregation MUST be thread-safe.
- UI updates MUST be throttled/batched.

Correctness takes priority over maximum scan throughput.

## 7. State / Flow

Application-level state:

```text
Idle
 -> Starting
 -> Scanning
 -> Aggregating
 -> Classifying
 -> Saving
 -> Ready

Any active state -> Cancelling -> Idle/CancelledResult
Any active state -> Error -> Idle
```

UI command enablement MUST derive from state:

- Start enabled only in Idle/Ready.
- Cancel enabled during active scan stages where cancellation can still be honored.
- Drive selection locked during active scan.
- Export enabled only when a stable snapshot exists.

## 8. Error Handling

Use typed failures rather than generic strings where practical:

```text
ScanStartFailure
VolumeUnavailableFailure
RootAccessFailure
NativeMetadataFailure
SnapshotPersistenceFailure
ConfigurationFailure
RuleSetLoadFailure
```

Path-level errors remain `ScanWarning` and should not promote to session failure unless root/systemic.

UI catches exceptions only at presentation boundary; business logic should return/throw meaningful application exceptions with safe messages. Detailed exception data belongs in local app logs.

## 9. Edge Cases

- UI closes during scan: trigger cancellation and dispose resources.
- Multiple Start clicks: serialize/disable; do not spawn duplicate scan sessions.
- Snapshot database corrupted: fail repository initialization clearly and allow rebuild/reset of app-owned data only.
- Rule set malformed: reject bad rule set and use last-known-good/built-in rules if available.
- Native API unavailable on non-NTFS volume: fallback to portable metadata path and mark unsupported metrics unknown.
- Volume becomes unavailable while querying metadata.

## 10. Performance Consideration

- Keep native handles short-lived unless caching is demonstrably safe.
- Use object pooling only after profiling.
- Avoid storing duplicated full paths if memory becomes an issue; parent/name composition MAY be introduced later, but simplicity wins initially.
- Use prepared SQLite statements and batched inserts.
- Prefer asynchronous orchestration for responsiveness, not fake async around CPU-only loops.
- Never block WPF UI thread on scan completion.

## 11. Testing Requirement

- Unit-test domain services without filesystem.
- Contract-test infrastructure interfaces.
- Integration-test real temp NTFS fixtures where CI environment permits.
- Test application pipeline with fake scanner/repository to force each stage failure.
- Verify cancellation from Scanning, Aggregating, Classifying, and Saving if saving cancellation is supported.
- Architecture test: Domain project cannot reference App/Infrastructure assemblies.

## 12. Acceptance Criteria

- [ ] Architecture is a single deployable desktop app for V1.
- [ ] Domain logic is independently unit-testable.
- [ ] Native/SQLite/WPF dependencies are isolated from core classification and tree policies.
- [ ] One authoritative pipeline transforms scan data into snapshot data.
- [ ] UI never performs direct filesystem traversal.
- [ ] Cancellation propagates through long-running operations.
- [ ] Path-level failures remain recoverable warnings.
- [ ] No component adds a deletion capability without an explicit future scope change.
