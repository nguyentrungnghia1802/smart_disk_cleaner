# 10 — Initial Cleanup / Protection Rule Catalog

**Document status:** Normative baseline for V1 rule content  
**Important:** This catalog classifies candidates for review. It does not authorize deletion.

## 1. Responsibility

Define the initial built-in rule families, their intended category/risk/confidence, path context, and false-positive safeguards. This document prevents implementation from degenerating into unsafe generic rules such as "all `.tmp` files are junk" or "all files older than N days are junk".

## 2. Boundary

This catalog is intentionally conservative. V1 SHOULD ship fewer well-tested rules rather than many broad heuristics. A rule not listed here may be added only with positive/negative tests and an explicit rationale.

The catalog does not attempt antivirus detection, unused-program detection, registry cleanup, content duplicate detection, or direct Windows component cleanup.

## 3. Input / Output

### Inputs used by catalog rules

- normalized path and parent context,
- node type,
- file name/extension,
- logical/allocated size,
- modified timestamp as optional supporting evidence,
- file attributes,
- reparse status,
- resolved per-session Windows/user known paths.

### Outputs

Each matching rule emits:

```text
RuleId
Category
Risk
Confidence
PrimaryReasonCode
RecommendationCode
ProtectionStrength
Optional reclaim estimate policy
```

## 4. Domain Model

### Protection strength

- `HardProtected` — must not become a normal cleanup recommendation.
- `ReservedExcluded` — not analyzed because it is app-owned mutable data.
- `Normal` — eligible for other rules.

### Recommendation codes

Examples:

- `ReviewCacheCandidate`
- `ReviewTemporaryCandidate`
- `ReviewCrashArtifact`
- `ReviewRecycleBin`
- `ReviewLargePersonalFile`
- `ReviewDeveloperCache`
- `DoNotDeleteDirectly`
- `UseWindowsManagedCleanup`
- `UnknownNoRecommendation`

## 5. Data Structure

Built-in rules should use stable IDs, for example:

```text
PROTECT-WINDOWS-WINSXS-001
PROTECT-WINDOWS-INSTALLER-001
PROTECT-SYSTEM-FILE-001
EXCLUDE-APPDATA-OWNED-001
TEMP-USER-001
TEMP-WINDOWS-001
CACHE-CHROMIUM-001
CACHE-THUMBNAIL-001
CRASH-LOCALDUMP-001
RECYCLE-BIN-001
DEV-GRADLE-CACHE-001
DEV-NUGET-CACHE-001
DEV-NODE-MODULES-001
LARGE-FILE-INFO-001
GENERIC-TMP-REVIEW-001
```

Rule IDs MUST remain stable once released so historical snapshots remain explainable.

## 6. Algorithm

### 6.1 Resolve known locations once per scan session

Resolve paths such as:

```text
Windows directory
System directory
User profile
LocalAppData
RoamingAppData
TEMP/TMP
Application-owned data root
```

Do not assume the Windows directory is always literally `C:\Windows` or the user profile always follows one hard-coded language/name layout. Built-in exact/prefix rules should derive paths from the current system/session where practical.

### 6.2 Hard protection baseline

The following families are hard-protected against normal cleanup recommendation in V1:

| Rule family | Match intent | Category | Risk | Confidence | Recommendation |
|---|---|---|---|---|---|
| Windows component store | Windows `WinSxS` subtree | Protected | Protected | High | Use Windows-managed servicing/cleanup only |
| Windows Installer cache | Windows `Installer` subtree | Protected | Protected | High | Do not delete directly |
| Core system binaries | `System32` and critical Windows binary/config subtrees | Protected | Protected | High | Do not delete directly |
| Boot/recovery metadata | boot/EFI/recovery-critical locations visible in scan | Protected | Protected | High | Do not delete directly |
| System Volume Information | exact protected system metadata root | Protected | Protected | High | Do not delete directly |
| Paging/swap/hibernation files | recognized root system files such as page/swap/hibernation backing files | Protected | Protected | High | Manage through Windows feature/settings, not file deletion |
| App-owned data root | exact Smart Disk Cleaner data root | Reserved exclusion | Protected | High | Excluded from analysis |

Protection applies before extension/name candidate rules. For example, `.tmp` under a hard-protected subtree remains protected.

### 6.3 User temporary directory

**Rule:** `TEMP-USER-001`

Match only the resolved user/system temporary location prefixes intended for temporary files, not any arbitrary folder named `Temp`.

Recommended result:

```text
Category: HighConfidenceReclaimable
Risk: Low to Medium
Confidence: High
Reason: Located in a recognized temporary-data root
```

Safeguards:

- path-context required,
- reparse point itself is not followed,
- files may be active/in-use; classification is a candidate, never proof safe to delete at this instant,
- no generic basename-only match for `Temp`.

### 6.4 Windows temporary directory

**Rule:** `TEMP-WINDOWS-001`

Recognized Windows temp location may be marked as a strong temporary candidate, but risk should be at least as conservative as user temp because active servicing/app processes may own files.

```text
Category: HighConfidenceReclaimable
Risk: Medium
Confidence: High
Recommendation: ReviewTemporaryCandidate
```

V1 analyzer does not attempt to bypass locks or clean it.

### 6.5 Chromium-family browser caches

**Rule family:** `CACHE-CHROMIUM-001`

Match only recognized cache subdirectories under known browser profile structures, such as cache/code-cache/GPU-cache style directories for explicitly supported Chromium-family products.

```text
Category: HighConfidenceReclaimable
Risk: Low
Confidence: High when path context exactly matches supported product layout
```

Safeguards:

- never classify the entire browser profile as cache,
- cookies, login data, bookmarks, history databases, extensions, and user profile content are not covered by the cache rule,
- unsupported browser/profile layout falls back to Unknown/Review.

Browser-specific path definitions should be isolated in rule data so they can be updated without rewriting the engine.

### 6.6 Windows thumbnail cache

**Rule:** `CACHE-THUMBNAIL-001`

Recognize thumbnail-cache database patterns only inside the expected Windows Explorer cache context.

```text
Category: HighConfidenceReclaimable
Risk: Low
Confidence: High
Reason: Regeneratable thumbnail cache in recognized cache location
```

A matching filename outside the expected cache directory MUST NOT trigger this rule.

### 6.7 Crash dump/report artifacts

**Rule family:** `CRASH-LOCALDUMP-001`

Recognized crash dump/report locations may be candidates because they can be very large, but dumps can be valuable for debugging.

```text
Category: ReviewRecommended
Risk: Medium
Confidence: High that artifact is a crash dump; not high confidence that user no longer needs it
```

Size/age can raise review priority but must not convert it to automatic-safe semantics.

### 6.8 Recycle Bin

**Rule:** `RECYCLE-BIN-001`

Recognized recycle-bin content is already user-deleted data but permanent removal is irreversible.

```text
Category: ReviewRecommended
Risk: Medium
Confidence: High
Recommendation: ReviewRecycleBin
```

The analyzer may show its space impact. V1 does not empty the recycle bin.

### 6.9 Developer caches

Developer data requires nuance because many directories are regeneratable but expensive to rebuild.

#### Gradle caches

Recognized Gradle cache directories:

```text
Category: ReviewRecommended
Risk: Medium
Confidence: High that it is a cache
Recommendation: ReviewDeveloperCache
```

Do not classify project source/config as cache.

#### NuGet package caches

Recognized global package cache locations:

```text
Category: ReviewRecommended
Risk: Medium
Confidence: High that packages can generally be restored when sources remain available
```

Do not assume offline restore is possible.

#### `node_modules`

A directory named `node_modules` under a project is potentially regeneratable but can contain local/modified/generated package state and recreation requires package metadata/network/cache availability.

```text
Category: ReviewRecommended
Risk: Medium
Confidence: Medium/High that it is dependency material, not that deletion is harmless
```

It MUST NOT be classified as `HighConfidenceReclaimable` solely by directory name.

### 6.10 Downloads and personal large files

A Downloads folder, archive, ISO, VM disk, video, backup, or other large personal file is not junk by default.

Rules may surface:

```text
Category: LargeFileNotJunk or ReviewRecommended
Risk: High for deletion decision
Confidence: High that it is large; None/Low that it is junk
```

Examples of extensions useful for discovery, not junk verdicts:

```text
.iso .zip .7z .rar .tar .gz
.vhd .vhdx .vmdk
.mp4 .mkv .mov
.bak and backup-like names
```

### 6.11 Generic `.tmp`, `.log`, old-file rules

Generic extension or age patterns are weak signals.

- `.tmp` outside recognized temporary context -> `ReviewRecommended`, Low confidence.
- `.log` outside recognized disposable cache context -> `ReviewRecommended` or informational only.
- "older than N days" alone -> never high-confidence junk.
- large + old -> review priority only.

### 6.12 Reparse point classification

A reparse point is primarily a filesystem topology fact, not a junk category.

```text
Category: Unknown or context-derived
Evidence: ReparsePointNotFollowed
Risk: at least context-aware
```

Do not classify the target content through the link path because the target is not traversed in V1.

### 6.13 Rule precedence examples

Example A:

```text
C:\Windows\WinSxS\something.tmp
```

Matches `.tmp` weak signal and WinSxS protection. Result: `Protected`.

Example B:

```text
<resolved LocalAppData>\Temp\abc.tmp
```

Recognized temp path. Result: strong temporary candidate.

Example C:

```text
D:\Source\MyApp\Temp\important-model.bin
```

Folder name is `Temp` but not a recognized temp root. Result: Unknown/other matching informational rule, not automatic temp candidate.

## 7. State / Flow

```text
Resolve known roots
 -> validate built-in rules
 -> protection pass
 -> strong context-specific candidate pass
 -> developer/review candidate pass
 -> generic informational signals
 -> conflict resolution
 -> classification + evidence
```

Rule-set version must be persisted with snapshot.

## 8. Error Handling

- Known-folder resolution fails: disable only dependent rule family and record rule-set/session warning; do not substitute a guessed path.
- Browser layout not recognized: no strong cache rule match.
- Environment variable expands to invalid path: reject that resolved matcher for session.
- Rule data malformed: fail validation and use last-known-good embedded rule set if available.
- Unknown timestamp/size: size/age condition does not match unless explicitly designed for unknown.

## 9. Edge Cases

- Multiple Windows user profiles on same drive.
- Non-default Windows installation directory.
- AppData redirected.
- Temporary directory redirected to D:.
- Browser has multiple profiles.
- Cache directory is a junction/reparse point.
- `.tmp` file is valuable project data.
- Crash dump needed for active debugging.
- `node_modules` contains a locally linked/custom package.
- System file has misleading cache-like extension.
- User intentionally stores archival material in Downloads.

## 10. Performance Consideration

- Resolve known locations once per session.
- Index exact/prefix path rules.
- Use cheap protection checks before lower-priority generic rules.
- Keep initial catalog deliberately small.
- Avoid per-node filesystem calls during classification; classification uses captured facts.

## 11. Testing Requirement

For each rule family, create:

- at least one positive fixture,
- at least one near-miss negative fixture,
- protected-overlap fixture when relevant,
- case-variation fixture,
- missing-metadata fixture when relevant.

Mandatory catalog regression tests:

- `WinSxS\x.tmp` => Protected.
- recognized user temp child => HighConfidenceReclaimable candidate.
- arbitrary project `Temp` folder => not strong temp candidate.
- recognized browser Cache => cache candidate; neighboring profile database => not cache by same rule.
- crash dump => ReviewRecommended, not automatically safe.
- `node_modules` => ReviewRecommended, not high-confidence junk.
- 20 GB ISO => LargeFileNotJunk/Review, not junk-by-size.
- old `.log` alone => not high-confidence junk.

## 12. Acceptance Criteria

- [ ] V1 has explicit hard protection rules before cleanup candidate rules.
- [ ] App-owned mutable data root is an exact reserved exclusion.
- [ ] Strong temp/cache classification requires recognized path context.
- [ ] Browser cache rules do not cover whole user profiles.
- [ ] Crash dumps and developer caches remain review decisions.
- [ ] `node_modules` is not automatically treated as safe junk.
- [ ] Downloads and large personal files are not treated as junk by location/size alone.
- [ ] Generic extension/age heuristics have low confidence and cannot override protection.
- [ ] Every built-in rule family has positive and negative tests.
