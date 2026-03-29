using System.IO;
using Microsoft.Data.Sqlite;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

public sealed class HistoryRepository : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _held;

    public HistoryRepository()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardHistory");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "history.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _held = new SqliteConnection(_connectionString);
            await _held.OpenAsync(ct).ConfigureAwait(false);

            await using (var cmd = _held.CreateCommand())
            {
                cmd.CommandText = """
                    CREATE TABLE IF NOT EXISTS history (
                      id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                      content TEXT NOT NULL,
                      created_at INTEGER NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_history_created ON history (created_at DESC);
                    """;
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> InsertAsync(string content, DateTime createdAtUtc, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            await using var cmd = _held!.CreateCommand();
            cmd.CommandText = "INSERT INTO history(content, created_at) VALUES ($c, $t) RETURNING id;";
            cmd.Parameters.AddWithValue("$c", content);
            cmd.Parameters.AddWithValue("$t", new DateTimeOffset(createdAtUtc).ToUnixTimeMilliseconds());
            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt64(scalar);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(long id, string content, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            await using var cmd = _held!.CreateCommand();
            cmd.CommandText = "UPDATE history SET content = $c WHERE id = $id;";
            cmd.Parameters.AddWithValue("$c", content);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            await using var cmd = _held!.CreateCommand();
            cmd.CommandText = "DELETE FROM history WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Newest row by <c>created_at</c>, or null when table is empty.</summary>
    public async Task<ClipboardItem?> GetLatestAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            await using var cmd = _held!.CreateCommand();
            cmd.CommandText = """
                SELECT id, content, created_at FROM history
                ORDER BY created_at DESC
                LIMIT 1;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return null;

            var id = reader.GetInt64(0);
            var content = reader.GetString(1);
            var ms = reader.GetInt64(2);
            var created = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            return new ClipboardItem { Id = id, Content = content, CreatedAtUtc = created };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Recent rows when query is null/empty; otherwise case-insensitive LIKE search.</summary>
    public async Task<IReadOnlyList<ClipboardItem>> QueryAsync(string? searchQuery, int limit, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureOpen();
            var list = new List<ClipboardItem>();

            await using var cmd = _held!.CreateCommand();
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            cmd.CommandText = """
                SELECT id, content, created_at FROM history
                ORDER BY created_at DESC
                LIMIT $lim;
                """;
            cmd.Parameters.AddWithValue("$lim", limit);
        }
        else
        {
            cmd.CommandText = """
                SELECT id, content, created_at FROM history
                WHERE content LIKE $q ESCAPE '\'
                ORDER BY created_at DESC
                LIMIT $lim;
                """;
            var escaped = EscapeLikePattern(searchQuery.Trim());
            cmd.Parameters.AddWithValue("$q", "%" + escaped + "%");
            cmd.Parameters.AddWithValue("$lim", limit);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt64(0);
                var content = reader.GetString(1);
                var ms = reader.GetInt64(2);
                var created = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                list.Add(new ClipboardItem { Id = id, Content = content, CreatedAtUtc = created });
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TrimToMaxAsync(int maxEntries, CancellationToken ct = default)
    {
        if (maxEntries < 1)
            return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureOpen();

            long toDelete;
            await using (var countCmd = _held!.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM history;";
                var countObj = await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                var count = Convert.ToInt64(countObj);
                toDelete = count - maxEntries;
            }

            if (toDelete <= 0)
                return;

            await using (var del = _held!.CreateCommand())
            {
                del.CommandText = """
                    DELETE FROM history WHERE id IN (
                      SELECT id FROM history ORDER BY created_at ASC LIMIT $n
                    );
                    """;
                del.Parameters.AddWithValue("$n", toDelete);
                await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureOpen()
    {
        if (_held is not { State: System.Data.ConnectionState.Open })
            throw new InvalidOperationException("Repository not initialized.");
    }

    private static string EscapeLikePattern(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_held != null)
                await _held.DisposeAsync().ConfigureAwait(false);
            _held = null;
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }
}
