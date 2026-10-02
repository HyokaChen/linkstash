using Microsoft.Data.Sqlite;

namespace Linkstash.Api.Store;

public class SqliteCollectionStore(string connectionString) : ICollectionStore
{
    private const string SelectColumns =
        "id, url, title, translation, source_url, is_fallback_title, created_at, tags, group_id";

    public async Task InitAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS collections (
              id          TEXT PRIMARY KEY,
              url         TEXT NOT NULL,
              title       TEXT NOT NULL,
              translation TEXT NOT NULL,
              source_url  TEXT,
              is_fallback_title INTEGER NOT NULL DEFAULT 0,
              created_at  TEXT NOT NULL,
              tags        TEXT NOT NULL DEFAULT '',
              group_id    TEXT
            );
            CREATE TABLE IF NOT EXISTS groups (
              id         TEXT PRIMARY KEY,
              name       TEXT NOT NULL,
              source_url TEXT,
              created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_collections_created ON collections(created_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        // 迁移：旧库缺列则补上（与既有 tags 列迁移同构）
        await EnsureColumnAsync(conn, "collections", "tags", "TEXT NOT NULL DEFAULT ''", ct);
        await EnsureColumnAsync(conn, "collections", "group_id", "TEXT", ct);

        await using var idx = conn.CreateCommand();
        idx.CommandText = """
            CREATE INDEX IF NOT EXISTS idx_collections_tags ON collections(tags);
            CREATE INDEX IF NOT EXISTS idx_collections_group ON collections(group_id);
            """;
        await idx.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection conn,
        string table,
        string column,
        string definition,
        CancellationToken ct
    )
    {
        await using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        var exists = false;
        await using (var reader = await check.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetString(1) == column)
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        await using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(ct);
    }

    public async Task<CollectionItem> AddAsync(
        string url,
        string title,
        string translation,
        string? sourceUrl,
        bool isFallbackTitle,
        string tags = "",
        string? groupId = null
    )
    {
        var item = new CollectionItem(
            Guid.NewGuid().ToString("N"),
            url,
            title,
            translation,
            sourceUrl,
            isFallbackTitle,
            DateTimeOffset.UtcNow.ToString("o"),
            tags,
            groupId
        );

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO collections
              ({SelectColumns})
            VALUES
              (@id, @url, @title, @translation, @sourceUrl, @isFallbackTitle, @createdAt, @tags, @groupId);
            """;
        Bind(cmd, item);
        await cmd.ExecuteNonQueryAsync();
        return item;
    }

    public async Task<int> AddBatchAsync(IEnumerable<CollectionItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return 0;

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = $"""
            INSERT INTO collections
              ({SelectColumns})
            VALUES
              (@id, @url, @title, @translation, @sourceUrl, @isFallbackTitle, @createdAt, @tags, @groupId);
            """;

        var parameters = new[]
        {
            NewParam(cmd, "@id"),
            NewParam(cmd, "@url"),
            NewParam(cmd, "@title"),
            NewParam(cmd, "@translation"),
            NewParam(cmd, "@sourceUrl"),
            NewParam(cmd, "@isFallbackTitle"),
            NewParam(cmd, "@createdAt"),
            NewParam(cmd, "@tags"),
            NewParam(cmd, "@groupId"),
        };

        foreach (var raw in list)
        {
            var now = DateTimeOffset.UtcNow.ToString("o");
            var item =
                string.IsNullOrEmpty(raw.Id)
                    ? raw with
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        CreatedAt = now,
                    }
                : string.IsNullOrEmpty(raw.CreatedAt) ? raw with { CreatedAt = now }
                : raw;

            parameters[0].Value = item.Id;
            parameters[1].Value = item.Url;
            parameters[2].Value = item.Title;
            parameters[3].Value = item.Translation;
            parameters[4].Value = (object?)item.SourceUrl ?? DBNull.Value;
            parameters[5].Value = item.IsFallbackTitle ? 1 : 0;
            parameters[6].Value = item.CreatedAt;
            parameters[7].Value = item.Tags ?? "";
            parameters[8].Value = (object?)item.GroupId ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return list.Count;
    }

    public async Task<(List<CollectionItem> Items, int Total)> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? tag = null
    )
    {
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            clauses.Add("(url LIKE @s OR title LIKE @s OR translation LIKE @s OR tags LIKE @s)");
        if (!string.IsNullOrWhiteSpace(tag))
            clauses.Add("tags LIKE @tag");

        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"SELECT COUNT(*) FROM collections{where};";
            BindFilters(countCmd, search, tag);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            if (total == 0)
                return ([], 0);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT {SelectColumns}
                FROM collections{where}
                ORDER BY created_at DESC
                LIMIT @limit OFFSET @offset;
                """;
            BindFilters(cmd, search, tag);
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

            return (await ReadAllAsync(cmd), total);
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM collections WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<CollectionItem?> UpdateTagsAsync(string id, string tags)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE collections SET tags = @tags
            WHERE id = @id
            RETURNING {SelectColumns};
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@tags", tags);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
    }

    public async Task<HashSet<string>> ExistingNormalizedUrlsAsync(
        IReadOnlyCollection<string> normalizedUrls
    )
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (normalizedUrls.Count == 0)
            return found;

        var candidates = normalizedUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        // 分批 IN 查询，避免超过 SQLite 变量上限；规范化在 C# 侧完成，
        // 避免 SQL 里重复实现 UrlNormalizer 规则。
        const int batchSize = 500;
        for (var offset = 0; offset < candidates.Count; offset += batchSize)
        {
            var batch = candidates.Skip(offset).Take(batchSize).ToList();

            //库中 url 可能比规范化值多一个尾部斜杠（https://x/y/），
            // 因此为每个候选生成「原值」与「补斜杠」两个变体一起匹配。
            // 同一参数名不能在 SQLite 中复用，故每个变体独立命名。
            var variants = new List<(string Name, string Value)>(batch.Count * 2);
            var placeholders = new List<string>(batch.Count * 2);
            for (var i = 0; i < batch.Count; i++)
            {
                var value = batch[i];
                if (value.EndsWith('/'))
                {
                    placeholders.Add($"@u{i}a");
                    variants.Add(($"@u{i}a", value));
                    placeholders.Add($"@u{i}b");
                    variants.Add(($"@u{i}b", value[..^1]));
                }
                else
                {
                    placeholders.Add($"@u{i}a");
                    variants.Add(($"@u{i}a", value));
                    placeholders.Add($"@u{i}b");
                    variants.Add(($"@u{i}b", value + "/"));
                }
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT url FROM collections WHERE url IN ({string.Join(",", placeholders)});";
            foreach (var (name, value) in variants)
                cmd.Parameters.AddWithValue(name, value);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var normalized = UrlNormalizer.Normalize(reader.GetString(0));
                if (normalized is not null && candidates.Contains(normalized))
                    found.Add(normalized);
            }
        }

        return found;
    }

    public async Task<ImportGroup> CreateGroupAsync(string name, string? sourceUrl)
    {
        var group = new ImportGroup(
            Guid.NewGuid().ToString("N"),
            name,
            sourceUrl,
            DateTimeOffset.UtcNow.ToString("o")
        );

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO groups (id, name, source_url, created_at)
            VALUES (@id, @name, @sourceUrl, @createdAt);
            """;
        cmd.Parameters.AddWithValue("@id", group.Id);
        cmd.Parameters.AddWithValue("@name", group.Name);
        cmd.Parameters.AddWithValue("@sourceUrl", (object?)group.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", group.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
        return group;
    }

    public async Task<List<CollectionItem>> GetGroupItemsAsync(string groupId)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {SelectColumns}
            FROM collections
            WHERE group_id = @groupId
            ORDER BY created_at ASC;
            """;
        cmd.Parameters.AddWithValue("@groupId", groupId);
        return await ReadAllAsync(cmd);
    }

    public async Task<List<GroupSummary>> ListGroupsAsync()
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT g.id, g.name, COUNT(c.id) AS member_count
            FROM groups g
            LEFT JOIN collections c ON c.group_id = g.id
            GROUP BY g.id, g.name
            ORDER BY g.created_at DESC;
            """;

        var result = new List<GroupSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(
                new GroupSummary(reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2))
            );
        }
        return result;
    }

    private static async Task<List<CollectionItem>> ReadAllAsync(SqliteCommand cmd)
    {
        var items = new List<CollectionItem>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(Read(reader));
        }
        return items;
    }

    private static SqliteParameter NewParam(SqliteCommand cmd, string name)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        cmd.Parameters.Add(p);
        return p;
    }

    private static void BindFilters(SqliteCommand cmd, string? search, string? tag)
    {
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@s", $"%{search}%");
        if (!string.IsNullOrWhiteSpace(tag))
            cmd.Parameters.AddWithValue("@tag", $"%{tag}%");
    }

    private static void Bind(SqliteCommand cmd, CollectionItem item)
    {
        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.Parameters.AddWithValue("@url", item.Url);
        cmd.Parameters.AddWithValue("@title", item.Title);
        cmd.Parameters.AddWithValue("@translation", item.Translation);
        cmd.Parameters.AddWithValue("@sourceUrl", (object?)item.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isFallbackTitle", item.IsFallbackTitle ? 1 : 0);
        cmd.Parameters.AddWithValue("@createdAt", item.CreatedAt);
        cmd.Parameters.AddWithValue("@tags", item.Tags ?? "");
        cmd.Parameters.AddWithValue("@groupId", (object?)item.GroupId ?? DBNull.Value);
    }

    private static CollectionItem Read(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt64(5) != 0,
            reader.GetString(6),
            reader.IsDBNull(7) ? "" : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8)
        );
}
