using Microsoft.Data.Sqlite;

namespace Linkstash.Api.Store;

public class SqliteCollectionStore(string connectionString) : ICollectionStore
{
    public async Task InitAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        // tags 列以 ALTER TABLE 追加，兼容已存在的数据库。
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS collections (
              id          TEXT PRIMARY KEY,
              url         TEXT NOT NULL,
              title       TEXT NOT NULL,
              translation TEXT NOT NULL,
              source_url  TEXT,
              is_fallback_title INTEGER NOT NULL DEFAULT 0,
              created_at  TEXT NOT NULL,
              tags        TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_collections_created ON collections(created_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        // 迁移：旧库没有 tags 列则补上
        await using var check = conn.CreateCommand();
        check.CommandText = "PRAGMA table_info(collections);";
        var hasTags = false;
        await using (var reader = await check.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (reader.GetString(1) == "tags")
                {
                    hasTags = true;
                    break;
                }
            }
        }

        if (!hasTags)
        {
            await using var alter = conn.CreateCommand();
            alter.CommandText =
                "ALTER TABLE collections ADD COLUMN tags TEXT NOT NULL DEFAULT '';";
            await alter.ExecuteNonQueryAsync(ct);
        }

        await using var idx = conn.CreateCommand();
        idx.CommandText =
            "CREATE INDEX IF NOT EXISTS idx_collections_tags ON collections(tags);";
        await idx.ExecuteNonQueryAsync(ct);
    }

    public async Task<CollectionItem> AddAsync(
        string url,
        string title,
        string translation,
        string? sourceUrl,
        bool isFallbackTitle,
        string tags = ""
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
            tags
        );

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO collections
              (id, url, title, translation, source_url, is_fallback_title, created_at, tags)
            VALUES
              (@id, @url, @title, @translation, @sourceUrl, @isFallbackTitle, @createdAt, @tags);
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
        cmd.CommandText = """
            INSERT INTO collections
              (id, url, title, translation, source_url, is_fallback_title, created_at, tags)
            VALUES
              (@id, @url, @title, @translation, @sourceUrl, @isFallbackTitle, @createdAt, @tags);
            """;

        var pId = cmd.CreateParameter();
        pId.ParameterName = "@id";
        var pUrl = cmd.CreateParameter();
        pUrl.ParameterName = "@url";
        var pTitle = cmd.CreateParameter();
        pTitle.ParameterName = "@title";
        var pTrans = cmd.CreateParameter();
        pTrans.ParameterName = "@translation";
        var pSrc = cmd.CreateParameter();
        pSrc.ParameterName = "@sourceUrl";
        var pFb = cmd.CreateParameter();
        pFb.ParameterName = "@isFallbackTitle";
        var pCreated = cmd.CreateParameter();
        pCreated.ParameterName = "@createdAt";
        var pTags = cmd.CreateParameter();
        pTags.ParameterName = "@tags";
        cmd.Parameters.Add(pId);
        cmd.Parameters.Add(pUrl);
        cmd.Parameters.Add(pTitle);
        cmd.Parameters.Add(pTrans);
        cmd.Parameters.Add(pSrc);
        cmd.Parameters.Add(pFb);
        cmd.Parameters.Add(pCreated);
        cmd.Parameters.Add(pTags);

        foreach (var raw in list)
        {
            var now = DateTimeOffset.UtcNow.ToString("o");
            var item = string.IsNullOrEmpty(raw.Id)
                ? raw with { Id = Guid.NewGuid().ToString("N"), CreatedAt = now }
                : string.IsNullOrEmpty(raw.CreatedAt)
                    ? raw with { CreatedAt = now }
                    : raw;
            pId.Value = item.Id;
            pUrl.Value = item.Url;
            pTitle.Value = item.Title;
            pTrans.Value = item.Translation;
            pSrc.Value = (object?)item.SourceUrl ?? DBNull.Value;
            pFb.Value = item.IsFallbackTitle ? 1 : 0;
            pCreated.Value = item.CreatedAt;
            pTags.Value = item.Tags ?? "";
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
                SELECT id, url, title, translation, source_url, is_fallback_title, created_at, tags
                FROM collections{where}
                ORDER BY created_at DESC
                LIMIT @limit OFFSET @offset;
                """;
            BindFilters(cmd, search, tag);
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

            var items = new List<CollectionItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(Read(reader));
            }
            return (items, total);
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
        cmd.CommandText = """
            UPDATE collections SET tags = @tags
            WHERE id = @id
            RETURNING id, url, title, translation, source_url, is_fallback_title, created_at, tags;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@tags", tags);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Read(reader) : null;
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
            reader.IsDBNull(7) ? "" : reader.GetString(7)
        );
}
