# 04 — Risk & Cleanup Candidate Rule Engine

**Document status:** Normative / Phase 1 critical

## 1. Responsibility

Define a deterministic, explainable rule engine that classifies scan nodes without pretending that "large" means "junk". The engine provides evidence and recommendation context; final deletion judgment remains with the user.

## 2. Boundary

The rule engine:

- evaluates metadata/path/aggregate facts,
- applies protection rules,
- detects known cleanup candidate patterns,
- assigns category/risk/confidence,
- records matched rules and user-readable reasons.

The rule engine MUST NOT:

- delete or modify files,
- infer malware,
- read arbitrary file content in V1,
- rely on opaque machine-learning scoring,
- downgrade a protected path because a lower-priority cleanup rule matches,
- label a large personal file as junk solely based on size.

## 3. Input / Output

### Input

`ClassifiableNode` containing normalized path, type, size/accounting, timestamps, attributes, reparse status, and directory aggregates.

### Output

```text
ClassificationResult
- Category
- RiskLevel
- Confidence
- Recommendation
- PrimaryReason
- MatchedRules[]
- IsProtected
- ReclaimableBytesEstimate?
- EvidenceFlags[]
```

## 4. Domain Model

### RuleDefinition

```text
RuleId
Version
Name
Enabled
Priority
Scope: File | Directory | Both
Conditions
Action
ExplanationTemplate
SourceType: BuiltIn | User (future)
```

### Conditions supported in V1

- exact normalized path,
- path prefix,
- path segment pattern,
- filename/glob,
- extension,
- node type,
- minimum/maximum logical size,
- minimum/maximum allocated size,
- minimum age by modified timestamp,
- file attributes,
- reparse status,
- parent path context.

Regex SHOULD be avoided unless a glob/prefix cannot express the rule; if allowed, regex must be compiled/cached and guarded against pathological patterns.

### Rule action

```text
Category
RiskLevel
Confidence
RecommendationCode
ProtectionStrength
ReclaimabilityPolicy
```

## 5. Data Structure

Rules should be immutable after loading. Pre-index rules by cheap predicates where possible:

```text
ProtectionRules
ExactPathRules
PrefixRules
ExtensionRules
NamePatternRules
GenericRules
```

Matched rule IDs are stored with snapshot so historical scan explanations remain reproducible against the recorded rule-set version.

## 6. Algorithm

### 6.1 Normalization

Before evaluation:

- normalize case according to Windows path comparison semantics,
- normalize separators,
- preserve original path for display,
- do not resolve reparse target,
- resolve environment-based built-in paths at scan/session start, not ad hoc per node.

### 6.2 Precedence

Recommended precedence:

```text
1. Hard protection rule
2. Safety/risk escalation rule
3. Known high-confidence cleanup rule
4. Review-recommended rule
5. Large-file informational rule
6. Unknown fallback
```

A lower priority rule may add evidence, but MUST NOT reduce protection/risk established by a stronger rule.

### 6.3 Combination policy

For multiple matches:

- `IsProtected` is sticky OR.
- Risk takes the most conservative/highest applicable level unless rule explicitly refines a same-domain case.
- Confidence describes confidence in the chosen classification, not safety of deletion.
- Primary reason comes from the winning precedence rule.
- All relevant rule IDs remain inspectable.

### 6.4 Initial built-in categories

Safe/high-confidence candidates should be conservative and path-context aware. Candidate classes may include:

- user/application temp directories,
- known application caches,
- browser caches,
- crash dump/report caches,
- thumbnail/cache artifacts,
- recycle-bin representation if implemented safely,
- known developer build caches as review recommended rather than universally safe.

Protected examples should include rules for critical Windows/system servicing locations and critical system files. Protection rules describe app behavior; they do not claim that every byte in that location is permanently necessary.

### 6.5 Large-file rule

A size threshold rule may classify as `LargeFileNotJunk` or `ReviewRecommended` depending on context. It MUST NOT produce `HighConfidenceReclaimable` from size alone.

### 6.6 Timestamp policy

`LastAccessTime` MUST NOT be the sole basis for "unused" because update behavior can be disabled/system-managed. Modified age may be used as weak evidence and must be contextual.

### 6.7 Reclaimable estimate

Use allocated/unique allocated data when sufficiently known. If not known, estimate must be marked partial or omitted. Protected items get no normal reclaim recommendation.

## 7. State / Flow

```text
RuleSetLoad
 -> ValidateRules
 -> BuildIndexes
 -> Ready

NodeFacts
 -> Normalize
 -> ProtectionPass
 -> CandidatePass
 -> InformationalPass
 -> ResolveConflicts
 -> ClassificationResult
```

Malformed rules fail loading validation; do not partially apply an invalid ruleset silently.

## 8. Error Handling

- Invalid rule ID/version: reject rule set.
- Invalid path/glob: reject offending built-in set during development; production falls back to embedded last-known-good set if implemented.
- Rule evaluation exception: record engine warning and classify node conservatively as Unknown/Protected as context warrants; never crash entire scan because one optional informational rule failed.
- Missing timestamp/size: condition evaluating that field does not match unless rule explicitly handles unknown.

## 9. Edge Cases

- Path named `Temp` in a user's project that is not a known temp location.
- A protected directory contains files matching `.tmp` pattern.
- A huge `.iso` in Downloads.
- A cache path is a reparse point.
- Old log file belongs to a running application.
- Extension in uppercase/mixed case.
- Localized user profile path.
- Environment variable path unavailable.
- A file matches both cache and protected prefix.
- Allocated size unknown but logical size large.

## 10. Performance Consideration

- Protection prefix checks should be indexed/trie-like or sorted prefix checks if rule count grows.
- Avoid allocating many temporary normalized strings per rule per node.
- Compile glob matchers once.
- Rule evaluation target should be approximately O(N * small-candidate-rule-set), not O(N * all-rules) when indexes can narrow candidates.
- Explanation formatting can be deferred until requested by UI if memory profiling requires it; matched rule IDs and normalized result must still be persisted.

## 11. Testing Requirement

Every built-in rule MUST have tests for:

- positive match,
- near-miss negative match,
- precedence against protected rule where applicable,
- case-insensitive path behavior,
- missing metadata behavior.

Mandatory invariants:

- large size alone never produces high-confidence junk,
- protected wins over cleanup rule,
- same input + same rule-set version => same output,
- no rule deletes/mutates anything,
- all non-Unknown candidate classifications have at least one reason and matched rule.

## 12. Acceptance Criteria

- [ ] Rule results are deterministic and explainable.
- [ ] Protection precedence cannot be bypassed by generic junk rules.
- [ ] Size-only rules never call data junk.
- [ ] Risk and confidence remain separate concepts.
- [ ] Each classification records rule-set version and matched rule IDs.
- [ ] Unknown metadata does not create false certainty.
- [ ] Last-access timestamp is never sole "unused" evidence.
- [ ] Rule engine is pure/testable without live filesystem access.
