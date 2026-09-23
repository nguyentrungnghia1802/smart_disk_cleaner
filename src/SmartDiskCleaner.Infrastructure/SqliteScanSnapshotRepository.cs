namespace SmartDiskCleaner.Infrastructure;

using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using SmartDiskCleaner.Domain;

public sealed class SqliteScanSnapshotRepository : IScanSnapshotRepository
{
    private readonly string _connectionString;
    private readonly object _initLock = new();
    private bool _initialized;

    public SqliteScanSnapshotRepository(IAppPathProvider pathProvider)
        : this(BuildConnectionString(pathProvider.AppDataRoot))
    {
    }

    public SqliteScanSnapshotRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private static string BuildConnectionString(string appDataRoot)
    {
        if (!Directory.Exists(appDataRoot))
        {
            Directory.CreateDirectory(appDataRoot);
        }

        var dbPath = Path.Combine(appDataRoot, "snapshots.db");
        return new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS scan_sessions (
                    id TEXT PRIMARY KEY,
                    volume_root TEXT NOT NULL,
                    volume_identity TEXT NULL,
                    started_utc TEXT NOT NULL,
                    completed_utc TEXT NULL,
                    status INTEGER NOT NULL,
                    options_version TEXT NOT NULL,
                    ruleset_version TEXT NOT NULL,
                    accounting_confidence INTEGER NOT NULL,
                    node_count INTEGER NOT NULL,
                    warning_count INTEGER NOT NULL,
                    failure_code TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS scan_nodes (
                    id INTEGER NOT NULL,
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
                    physical_volume TEXT NULL,
                    physical_file TEXT NULL,
                    link_count INTEGER NULL,
                    category INTEGER NOT NULL,
                    risk INTEGER NOT NULL,
                    confidence INTEGER NOT NULL,
                    recommendation INTEGER NOT NULL,
                    primary_reason_code TEXT NULL,
                    is_protected INTEGER NOT NULL,
                    reclaimable_bytes_estimate INTEGER NULL,
                    evidence_flags INTEGER NOT NULL,
                    ruleset_version TEXT NOT NULL,
                    metadata_flags INTEGER NOT NULL,
                    accounting_confidence INTEGER NOT NULL,
                    PRIMARY KEY (session_id, id),
                    FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS rule_matches (
                    session_id TEXT NOT NULL,
                    node_id INTEGER NOT NULL,
                    rule_id TEXT NOT NULL,
                    is_primary INTEGER NOT NULL,
                    PRIMARY KEY (session_id, node_id, rule_id),
                    FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS scan_warnings (
                    id INTEGER NOT NULL,
                    session_id TEXT NOT NULL,
                    node_id INTEGER NULL,
                    path TEXT NULL,
                    operation TEXT NOT NULL,
                    code TEXT NOT NULL,
                    severity INTEGER NOT NULL,
                    message_safe TEXT NOT NULL,
                    exception_type TEXT NULL,
                    occurred_utc TEXT NOT NULL,
                    recoverable INTEGER NOT NULL,
                    PRIMARY KEY (session_id, id),
                    FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS presentation_decisions (
                    session_id TEXT NOT NULL,
                    node_id INTEGER NOT NULL,
                    visible_child_ids TEXT NOT NULL,
                    auto_expand_ids TEXT NOT NULL,
                    collapsed_ids TEXT NOT NULL,
                    ranking_metric INTEGER NOT NULL,
                    threshold_triggered INTEGER NOT NULL,
                    PRIMARY KEY (session_id, node_id),
                    FOREIGN KEY(session_id) REFERENCES scan_sessions(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_scan_nodes_parent ON scan_nodes(session_id, parent_id);
                CREATE INDEX IF NOT EXISTS idx_scan_nodes_path ON scan_nodes(session_id, full_path);
                CREATE INDEX IF NOT EXISTS idx_scan_nodes_category ON scan_nodes(session_id, category);
                CREATE INDEX IF NOT EXISTS idx_scan_nodes_risk ON scan_nodes(session_id, risk);
                CREATE INDEX IF NOT EXISTS idx_scan_nodes_size ON scan_nodes(session_id, recursive_unique_allocated_bytes DESC);
                CREATE INDEX IF NOT EXISTS idx_scan_warnings_session ON scan_warnings(session_id);
                CREATE INDEX IF NOT EXISTS idx_rule_matches_node ON rule_matches(session_id, node_id);
            ";
            cmd.ExecuteNonQuery();
            _initialized = true;
        }
    }

    public async Task SaveSnapshotAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureInitialized();

        var session = snapshot.Session;
        var sessionIdStr = session.Id.ToString("D");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 1. Insert session in Persisting state
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO scan_sessions (
                        id, volume_root, volume_identity, started_utc, completed_utc,
                        status, options_version, ruleset_version, accounting_confidence,
                        node_count, warning_count, failure_code
                    ) VALUES (
                        @id, @root, @volume_identity, @started, @completed,
                        @status, @options_version, @ruleset_version, @confidence,
                        @nodes, @warnings, @failure
                    );";

                cmd.Parameters.AddWithValue("@id", sessionIdStr);
                cmd.Parameters.AddWithValue("@root", session.VolumeRoot);
                cmd.Parameters.AddWithValue("@volume_identity", (object?)session.VolumeIdentity ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@started", session.StartedAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@completed", (object?)session.CompletedAtUtc?.ToString("O") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@status", (int)SessionStatus.Persisting);
                cmd.Parameters.AddWithValue("@options_version", session.OptionsVersion);
                cmd.Parameters.AddWithValue("@ruleset_version", session.RuleSetVersion);
                cmd.Parameters.AddWithValue("@confidence", (int)snapshot.Metrics.AccountingConfidence);
                cmd.Parameters.AddWithValue("@nodes", snapshot.Nodes.Count);
                cmd.Parameters.AddWithValue("@warnings", snapshot.Warnings.Count);
                cmd.Parameters.AddWithValue("@failure", (object?)session.FailureCode ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 2. Batch insert nodes
            const int batchSize = 500;
            for (var offset = 0; offset < snapshot.Nodes.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, snapshot.Nodes.Count - offset);

                await using var nodeCmd = connection.CreateCommand();
                nodeCmd.Transaction = transaction;

                var valuesClause = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var node = snapshot.Nodes[offset + i];
                    var p = $"@p{i}_";
                    valuesClause.Add($@"({p}id, '{sessionIdStr}', {p}parent_id, {p}depth, {p}name, {p}path, {p}type,
                        {p}log, {p}alloc, {p}ualloc, {p}rlog, {p}ralloc, {p}rualloc,
                        {p}cf, {p}cd, {p}df, {p}dd, {p}cre, {p}mod, {p}acc,
                        {p}attr, {p}rep, {p}reptag, {p}access, {p}pvol, {p}pfile, {p}links,
                        {p}cat, {p}risk, {p}conf, {p}rec, {p}reason, {p}prot, {p}reclaim, {p}ev,
                        {p}rver, {p}mflags, {p}aconf)");

                    nodeCmd.Parameters.AddWithValue($"{p}id", node.Id);
                    nodeCmd.Parameters.AddWithValue($"{p}parent_id", (object?)node.ParentId ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}depth", node.Depth);
                    nodeCmd.Parameters.AddWithValue($"{p}name", node.Name);
                    nodeCmd.Parameters.AddWithValue($"{p}path", node.FullPath);
                    nodeCmd.Parameters.AddWithValue($"{p}type", (int)node.NodeType);
                    nodeCmd.Parameters.AddWithValue($"{p}log", node.LogicalBytes);
                    nodeCmd.Parameters.AddWithValue($"{p}alloc", (object?)node.AllocatedBytes ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}ualloc", (object?)node.UniqueAllocatedBytes ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}rlog", node.RecursiveLogicalBytes);
                    nodeCmd.Parameters.AddWithValue($"{p}ralloc", (object?)node.RecursiveAllocatedBytes ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}rualloc", (object?)node.RecursiveUniqueAllocatedBytes ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}cf", node.Aggregate.DirectFileCount);
                    nodeCmd.Parameters.AddWithValue($"{p}cd", node.Aggregate.DirectDirectoryCount);
                    nodeCmd.Parameters.AddWithValue($"{p}df", node.Aggregate.DescendantFileCount);
                    nodeCmd.Parameters.AddWithValue($"{p}dd", node.Aggregate.DescendantDirectoryCount);
                    nodeCmd.Parameters.AddWithValue($"{p}cre", (object?)node.CreatedUtc?.ToString("O") ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}mod", (object?)node.ModifiedUtc?.ToString("O") ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}acc", (object?)node.LastAccessUtc?.ToString("O") ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}attr", (int)node.Attributes);
                    nodeCmd.Parameters.AddWithValue($"{p}rep", node.IsReparsePoint ? 1 : 0);
                    nodeCmd.Parameters.AddWithValue($"{p}reptag", (object?)node.ReparseTag ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}access", node.IsAccessible ? 1 : 0);
                    nodeCmd.Parameters.AddWithValue($"{p}pvol", (object?)node.PhysicalIdentity?.VolumeIdentity ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}pfile", (object?)node.PhysicalIdentity?.FileIdentity ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}links", (object?)node.LinkCount ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}cat", (int)node.Classification.Category);
                    nodeCmd.Parameters.AddWithValue($"{p}risk", (int)node.Classification.Risk);
                    nodeCmd.Parameters.AddWithValue($"{p}conf", (int)node.Classification.Confidence);
                    nodeCmd.Parameters.AddWithValue($"{p}rec", (int)node.Classification.Recommendation);
                    nodeCmd.Parameters.AddWithValue($"{p}reason", (object?)node.Classification.PrimaryReason ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}prot", node.Classification.IsProtected ? 1 : 0);
                    nodeCmd.Parameters.AddWithValue($"{p}reclaim", (object?)node.Classification.ReclaimableBytesEstimate ?? DBNull.Value);
                    nodeCmd.Parameters.AddWithValue($"{p}ev", (int)node.Classification.Evidence);
                    nodeCmd.Parameters.AddWithValue($"{p}rver", node.Classification.RuleSetVersion);
                    nodeCmd.Parameters.AddWithValue($"{p}mflags", (int)node.MetadataFlags);
                    nodeCmd.Parameters.AddWithValue($"{p}aconf", (int)node.AccountingConfidence);
                }

                nodeCmd.CommandText = $@"
                    INSERT INTO scan_nodes (
                        id, session_id, parent_id, depth, name, full_path, node_type,
                        logical_bytes, allocated_bytes, unique_allocated_bytes,
                        recursive_logical_bytes, recursive_allocated_bytes, recursive_unique_allocated_bytes,
                        child_file_count, child_directory_count, descendant_file_count, descendant_directory_count,
                        created_utc, modified_utc, last_access_utc,
                        attributes, is_reparse_point, reparse_tag, is_accessible,
                        physical_volume, physical_file, link_count,
                        category, risk, confidence, recommendation, primary_reason_code,
                        is_protected, reclaimable_bytes_estimate, evidence_flags,
                        ruleset_version, metadata_flags, accounting_confidence
                    ) VALUES {string.Join(",\n", valuesClause)};";

                await nodeCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 3. Batch insert rule matches
            var allMatches = new List<(long NodeId, string RuleId, bool IsPrimary)>();
            foreach (var node in snapshot.Nodes)
            {
                var primary = node.Classification.PrimaryReason;
                foreach (var ruleId in node.Classification.MatchedRuleIds)
                {
                    allMatches.Add((node.Id, ruleId, ruleId == primary));
                }
            }

            for (var offset = 0; offset < allMatches.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, allMatches.Count - offset);

                await using var matchCmd = connection.CreateCommand();
                matchCmd.Transaction = transaction;

                var values = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var m = allMatches[offset + i];
                    var p = $"@m{i}_";
                    values.Add($"('{sessionIdStr}', {p}nid, {p}rid, {p}prim)");
                    matchCmd.Parameters.AddWithValue($"{p}nid", m.NodeId);
                    matchCmd.Parameters.AddWithValue($"{p}rid", m.RuleId);
                    matchCmd.Parameters.AddWithValue($"{p}prim", m.IsPrimary ? 1 : 0);
                }

                matchCmd.CommandText = $@"
                    INSERT OR IGNORE INTO rule_matches (session_id, node_id, rule_id, is_primary)
                    VALUES {string.Join(",\n", values)};";

                await matchCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 4. Batch insert warnings
            for (var offset = 0; offset < snapshot.Warnings.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, snapshot.Warnings.Count - offset);

                await using var warnCmd = connection.CreateCommand();
                warnCmd.Transaction = transaction;

                var values = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var w = snapshot.Warnings[offset + i];
                    var p = $"@w{i}_";
                    values.Add($"({p}id, '{sessionIdStr}', {p}nid, {p}path, {p}op, {p}code, {p}sev, {p}msg, {p}ex, {p}time, {p}rec)");

                    warnCmd.Parameters.AddWithValue($"{p}id", w.Id);
                    warnCmd.Parameters.AddWithValue($"{p}nid", (object?)w.NodeId ?? DBNull.Value);
                    warnCmd.Parameters.AddWithValue($"{p}path", (object?)w.Path ?? DBNull.Value);
                    warnCmd.Parameters.AddWithValue($"{p}op", w.Operation);
                    warnCmd.Parameters.AddWithValue($"{p}code", w.Code);
                    warnCmd.Parameters.AddWithValue($"{p}sev", (int)w.Severity);
                    warnCmd.Parameters.AddWithValue($"{p}msg", w.MessageSafe);
                    warnCmd.Parameters.AddWithValue($"{p}ex", (object?)w.ExceptionType ?? DBNull.Value);
                    warnCmd.Parameters.AddWithValue($"{p}time", w.OccurredAtUtc.ToString("O"));
                    warnCmd.Parameters.AddWithValue($"{p}rec", w.Recoverable ? 1 : 0);
                }

                warnCmd.CommandText = $@"
                    INSERT INTO scan_warnings (
                        id, session_id, node_id, path, operation, code, severity,
                        message_safe, exception_type, occurred_utc, recoverable
                    ) VALUES {string.Join(",\n", values)};";

                await warnCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 5. Batch insert presentation decisions
            var decisionsList = snapshot.PresentationDecisions.ToList();
            for (var offset = 0; offset < decisionsList.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(batchSize, decisionsList.Count - offset);

                await using var presCmd = connection.CreateCommand();
                presCmd.Transaction = transaction;

                var values = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var kvp = decisionsList[offset + i];
                    var nodeId = kvp.Key;
                    var d = kvp.Value;
                    var p = $"@pd{i}_";

                    values.Add($"('{sessionIdStr}', {p}nid, {p}vis, {p}auto, {p}col, {p}met, {p}trig)");

                    presCmd.Parameters.AddWithValue($"{p}nid", nodeId);
                    presCmd.Parameters.AddWithValue($"{p}vis", string.Join(',', d.VisibleChildIds));
                    presCmd.Parameters.AddWithValue($"{p}auto", string.Join(',', d.AutoExpandDirectoryIds));
                    presCmd.Parameters.AddWithValue($"{p}col", string.Join(',', d.CollapsedDirectoryIds));
                    presCmd.Parameters.AddWithValue($"{p}met", (int)d.RankingMetric);
                    presCmd.Parameters.AddWithValue($"{p}trig", d.ThresholdTriggered ? 1 : 0);
                }

                presCmd.CommandText = $@"
                    INSERT INTO presentation_decisions (
                        session_id, node_id, visible_child_ids, auto_expand_ids, collapsed_ids,
                        ranking_metric, threshold_triggered
                    ) VALUES {string.Join(",\n", values)};";

                await presCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // 6. Finalize session status to Completed
            await using (var finalCmd = connection.CreateCommand())
            {
                finalCmd.Transaction = transaction;
                finalCmd.CommandText = @"
                    UPDATE scan_sessions
                    SET status = @status, completed_utc = @completed
                    WHERE id = @id;";

                finalCmd.Parameters.AddWithValue("@status", (int)session.Status);
                finalCmd.Parameters.AddWithValue("@completed", (object?)session.CompletedAtUtc?.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"));
                finalCmd.Parameters.AddWithValue("@id", sessionIdStr);

                await finalCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ScanSnapshot?> LoadSnapshotAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var sessionIdStr = sessionId.ToString("D");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // 1. Load Session
        ScanSession? session = null;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM scan_sessions WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", sessionIdStr);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var volRoot = reader.GetString(reader.GetOrdinal("volume_root"));
                var volIdentity = reader.IsDBNull(reader.GetOrdinal("volume_identity")) ? null : reader.GetString(reader.GetOrdinal("volume_identity"));
                var started = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_utc")), CultureInfo.InvariantCulture);
                DateTimeOffset? completed = reader.IsDBNull(reader.GetOrdinal("completed_utc"))
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("completed_utc")), CultureInfo.InvariantCulture);
                var status = (SessionStatus)reader.GetInt32(reader.GetOrdinal("status"));
                var optionsVer = reader.GetString(reader.GetOrdinal("options_version"));
                var rulesetVer = reader.GetString(reader.GetOrdinal("ruleset_version"));
                var failure = reader.IsDBNull(reader.GetOrdinal("failure_code")) ? null : reader.GetString(reader.GetOrdinal("failure_code"));

                session = new ScanSession(sessionId, volRoot, volIdentity, started, completed, status, optionsVer, rulesetVer, failure);
            }
        }

        if (session == null) return null;

        // 2. Load Rule Matches
        var matchesByNode = new Dictionary<long, List<string>>();
        await using (var matchCmd = connection.CreateCommand())
        {
            matchCmd.CommandText = "SELECT node_id, rule_id FROM rule_matches WHERE session_id = @id ORDER BY node_id;";
            matchCmd.Parameters.AddWithValue("@id", sessionIdStr);

            await using var reader = await matchCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var nid = reader.GetInt64(0);
                var rid = reader.GetString(1);
                if (!matchesByNode.TryGetValue(nid, out var list))
                {
                    list = new List<string>();
                    matchesByNode[nid] = list;
                }
                list.Add(rid);
            }
        }

        // 3. Load Warnings
        var warnings = new List<ScanWarning>();
        await using (var warnCmd = connection.CreateCommand())
        {
            warnCmd.CommandText = "SELECT * FROM scan_warnings WHERE session_id = @id ORDER BY id;";
            warnCmd.Parameters.AddWithValue("@id", sessionIdStr);

            await using var reader = await warnCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var wid = reader.GetInt64(reader.GetOrdinal("id"));
                long? nid = reader.IsDBNull(reader.GetOrdinal("node_id")) ? null : reader.GetInt64(reader.GetOrdinal("node_id"));
                var path = reader.IsDBNull(reader.GetOrdinal("path")) ? null : reader.GetString(reader.GetOrdinal("path"));
                var op = reader.GetString(reader.GetOrdinal("operation"));
                var code = reader.GetString(reader.GetOrdinal("code"));
                var sev = (WarningSeverity)reader.GetInt32(reader.GetOrdinal("severity"));
                var msg = reader.GetString(reader.GetOrdinal("message_safe"));
                var ex = reader.IsDBNull(reader.GetOrdinal("exception_type")) ? null : reader.GetString(reader.GetOrdinal("exception_type"));
                var occ = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("occurred_utc")), CultureInfo.InvariantCulture);
                var rec = reader.GetInt32(reader.GetOrdinal("recoverable")) == 1;

                warnings.Add(new ScanWarning(wid, sessionId, path, op, code, msg, ex, occ, sev, rec, nid));
            }
        }

        // 4. Load Presentation Decisions
        var presentation = new Dictionary<long, PresentationDecision>();
        await using (var presCmd = connection.CreateCommand())
        {
            presCmd.CommandText = "SELECT * FROM presentation_decisions WHERE session_id = @id;";
            presCmd.Parameters.AddWithValue("@id", sessionIdStr);

            await using var reader = await presCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var nid = reader.GetInt64(reader.GetOrdinal("node_id"));
                var visStr = reader.GetString(reader.GetOrdinal("visible_child_ids"));
                var autoStr = reader.GetString(reader.GetOrdinal("auto_expand_ids"));
                var colStr = reader.GetString(reader.GetOrdinal("collapsed_ids"));
                var met = (RankingMetricKind)reader.GetInt32(reader.GetOrdinal("ranking_metric"));
                var trig = reader.GetInt32(reader.GetOrdinal("threshold_triggered")) == 1;

                var vis = string.IsNullOrWhiteSpace(visStr) ? Array.Empty<long>() : visStr.Split(',').Select(long.Parse).ToArray();
                var auto = string.IsNullOrWhiteSpace(autoStr) ? Array.Empty<long>() : autoStr.Split(',').Select(long.Parse).ToArray();
                var col = string.IsNullOrWhiteSpace(colStr) ? Array.Empty<long>() : colStr.Split(',').Select(long.Parse).ToArray();

                presentation[nid] = new PresentationDecision(vis, auto, col, met, trig);
            }
        }

        // 5. Load Nodes
        var nodes = new List<ScanNode>();
        await using (var nodeCmd = connection.CreateCommand())
        {
            nodeCmd.CommandText = "SELECT * FROM scan_nodes WHERE session_id = @id ORDER BY id;";
            nodeCmd.Parameters.AddWithValue("@id", sessionIdStr);

            await using var reader = await nodeCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var id = reader.GetInt64(reader.GetOrdinal("id"));
                long? pid = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetInt64(reader.GetOrdinal("parent_id"));
                var depth = reader.GetInt32(reader.GetOrdinal("depth"));
                var name = reader.GetString(reader.GetOrdinal("name"));
                var fullPath = reader.GetString(reader.GetOrdinal("full_path"));
                var nodeType = (NodeType)reader.GetInt32(reader.GetOrdinal("node_type"));
                var logical = reader.GetInt64(reader.GetOrdinal("logical_bytes"));
                long? allocated = reader.IsDBNull(reader.GetOrdinal("allocated_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("allocated_bytes"));
                long? uniqueAllocated = reader.IsDBNull(reader.GetOrdinal("unique_allocated_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("unique_allocated_bytes"));

                var rlogical = reader.GetInt64(reader.GetOrdinal("recursive_logical_bytes"));
                long? rallocated = reader.IsDBNull(reader.GetOrdinal("recursive_allocated_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("recursive_allocated_bytes"));
                long? runiqueAllocated = reader.IsDBNull(reader.GetOrdinal("recursive_unique_allocated_bytes")) ? null : reader.GetInt64(reader.GetOrdinal("recursive_unique_allocated_bytes"));

                var cf = reader.GetInt32(reader.GetOrdinal("child_file_count"));
                var cd = reader.GetInt32(reader.GetOrdinal("child_directory_count"));
                var df = reader.GetInt32(reader.GetOrdinal("descendant_file_count"));
                var dd = reader.GetInt32(reader.GetOrdinal("descendant_directory_count"));

                DateTimeOffset? cre = reader.IsDBNull(reader.GetOrdinal("created_utc")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc")), CultureInfo.InvariantCulture);
                DateTimeOffset? mod = reader.IsDBNull(reader.GetOrdinal("modified_utc")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("modified_utc")), CultureInfo.InvariantCulture);
                DateTimeOffset? acc = reader.IsDBNull(reader.GetOrdinal("last_access_utc")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("last_access_utc")), CultureInfo.InvariantCulture);

                var attr = (FileAttributes)reader.GetInt32(reader.GetOrdinal("attributes"));
                uint? reptag = reader.IsDBNull(reader.GetOrdinal("reparse_tag")) ? null : (uint)reader.GetInt64(reader.GetOrdinal("reparse_tag"));
                var access = reader.GetInt32(reader.GetOrdinal("is_accessible")) == 1;

                string? pvol = reader.IsDBNull(reader.GetOrdinal("physical_volume")) ? null : reader.GetString(reader.GetOrdinal("physical_volume"));
                string? pfile = reader.IsDBNull(reader.GetOrdinal("physical_file")) ? null : reader.GetString(reader.GetOrdinal("physical_file"));
                PhysicalFileIdentity? physId = pvol != null && pfile != null ? new PhysicalFileIdentity(pvol, pfile) : null;

                uint? links = reader.IsDBNull(reader.GetOrdinal("link_count")) ? null : (uint)reader.GetInt32(reader.GetOrdinal("link_count"));

                var cat = (CleanupCategory)reader.GetInt32(reader.GetOrdinal("category"));
                var risk = (RiskLevel)reader.GetInt32(reader.GetOrdinal("risk"));
                var conf = (ConfidenceLevel)reader.GetInt32(reader.GetOrdinal("confidence"));
                var rec = (RecommendationCode)reader.GetInt32(reader.GetOrdinal("recommendation"));
                string? reason = reader.IsDBNull(reader.GetOrdinal("primary_reason_code")) ? null : reader.GetString(reader.GetOrdinal("primary_reason_code"));
                var prot = reader.GetInt32(reader.GetOrdinal("is_protected")) == 1;
                long? reclaim = reader.IsDBNull(reader.GetOrdinal("reclaimable_bytes_estimate")) ? null : reader.GetInt64(reader.GetOrdinal("reclaimable_bytes_estimate"));
                var ev = (EvidenceFlags)reader.GetInt32(reader.GetOrdinal("evidence_flags"));
                var rver = reader.GetString(reader.GetOrdinal("ruleset_version"));

                var mflags = (MetadataFlags)reader.GetInt32(reader.GetOrdinal("metadata_flags"));
                var aconf = (AccountingConfidence)reader.GetInt32(reader.GetOrdinal("accounting_confidence"));

                var matchedRules = matchesByNode.TryGetValue(id, out var ml) ? (IReadOnlyList<string>)ml : Array.Empty<string>();
                var classification = new ClassificationResult(cat, risk, conf, rec, reason, matchedRules, prot, reclaim, ev, rver);

                var aggregate = new DirectoryAggregate(
                    cf, cd, df, dd,
                    rlogical,
                    rallocated ?? 0,
                    rallocated,
                    runiqueAllocated ?? 0,
                    runiqueAllocated,
                    0, 0);

                nodes.Add(new ScanNode(
                    id, pid, depth, fullPath, name, nodeType, attr,
                    logical, allocated, uniqueAllocated,
                    physId, links, reptag, cre, mod, acc,
                    mflags, access, aconf, aggregate, classification));
            }
        }

        var metrics = BuildMetrics(nodes, warnings.Count);
        return new ScanSnapshot(session, nodes, warnings, metrics, presentation);
    }

    public async Task<IReadOnlyList<ScanSession>> GetRecentSessionsAsync(int maxCount = 20, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var list = new List<ScanSession>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT * FROM scan_sessions
            WHERE status = @completedStatus
            ORDER BY started_utc DESC
            LIMIT @limit;";

        cmd.Parameters.AddWithValue("@completedStatus", (int)SessionStatus.Completed);
        cmd.Parameters.AddWithValue("@limit", Math.Max(1, maxCount));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var id = Guid.Parse(reader.GetString(reader.GetOrdinal("id")));
            var root = reader.GetString(reader.GetOrdinal("volume_root"));
            var volIdentity = reader.IsDBNull(reader.GetOrdinal("volume_identity")) ? null : reader.GetString(reader.GetOrdinal("volume_identity"));
            var started = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_utc")), CultureInfo.InvariantCulture);
            DateTimeOffset? completed = reader.IsDBNull(reader.GetOrdinal("completed_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("completed_utc")), CultureInfo.InvariantCulture);
            var status = (SessionStatus)reader.GetInt32(reader.GetOrdinal("status"));
            var optVer = reader.GetString(reader.GetOrdinal("options_version"));
            var ruleVer = reader.GetString(reader.GetOrdinal("ruleset_version"));
            var fail = reader.IsDBNull(reader.GetOrdinal("failure_code")) ? null : reader.GetString(reader.GetOrdinal("failure_code"));

            list.Add(new ScanSession(id, root, volIdentity, started, completed, status, optVer, ruleVer, fail));
        }

        return list;
    }

    public async Task<bool> RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sessionIdStr = sessionId.ToString("D");

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM scan_sessions WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", sessionIdStr);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return rows > 0;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static ScanMetrics BuildMetrics(IReadOnlyList<ScanNode> nodes, int warningCount)
    {
        var roots = nodes.Where(n => n.ParentId is null).ToArray();
        long logical = 0;
        long knownAllocated = 0;
        long knownUnique = 0;
        checked
        {
            foreach (var root in roots)
            {
                logical += root.RecursiveLogicalBytes;
                knownAllocated += root.NodeType == NodeType.Directory ? root.Aggregate.KnownAllocatedBytesRecursive : root.AllocatedBytes ?? 0;
                knownUnique += root.NodeType == NodeType.Directory ? root.Aggregate.KnownUniqueAllocatedBytesRecursive : root.UniqueAllocatedBytes ?? 0;
            }
        }

        var allocatedComplete = roots.Length > 0 && roots.All(r => r.RecursiveAllocatedBytes.HasValue);
        var uniqueComplete = roots.Length > 0 && roots.All(r => r.RecursiveUniqueAllocatedBytes.HasValue);
        var files = nodes.Where(n => n.NodeType == NodeType.File).ToArray();
        var confidence = uniqueComplete
            ? AccountingConfidence.ExactWithinCapturedMetadata
            : files.Length > 0 && files.All(f => f.AccountingConfidence == AccountingConfidence.Unsupported)
                ? AccountingConfidence.Unsupported
                : AccountingConfidence.Partial;

        var candidateTotals = files
            .Where(n => !n.Classification.IsProtected && n.UniqueAllocatedBytes.HasValue)
            .GroupBy(n => n.Classification.Category)
            .ToDictionary(g => g.Key, g => g.Aggregate(0L, (sum, node) => checked(sum + node.UniqueAllocatedBytes!.Value)));

        return new ScanMetrics(
            nodes.Count,
            files.Length,
            nodes.Count - files.Length,
            warningCount,
            logical,
            knownAllocated,
            allocatedComplete ? knownAllocated : null,
            knownUnique,
            uniqueComplete ? knownUnique : null,
            confidence,
            candidateTotals);
    }
}
