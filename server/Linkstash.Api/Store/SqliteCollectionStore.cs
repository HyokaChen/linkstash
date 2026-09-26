using Microsoft.Data.Sqlite;

namespace Linkstash.Api.Store;

public class SqliteCollectionStore : ICollectionStore
{
    private readonly string _connectionString;

    public SqliteCollectionStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
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
              created_at  TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_collections_created ON collections(created_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<CollectionItem> AddAsync(
        string url,
        string title,
        string translation,
        string? sourceUrl,
        bool isFallbackTitle
    )
    {
        var item = new CollectionItem(
            Guid.NewGuid().ToString("N"),
            url,
            title,
            translation,
            sourceUrl,
            isFallbackTitle,
            DateTimeOffset.UtcNow.ToString("o")
        );

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO collections (id, url, title, translation, source_url, is_fallback_title, created_at)
            VALUES (@id, @url, @title, @translation, @sourceUrl, @isFallbackTitle, @createdAt);
            """;
        cmd.Parameters.AddWithValue("@id", item.Id);
        cmd.Parameters.AddWithValue("@url", item.Url);
        cmd.Parameters.AddWithValue("@title", item.Title);
        cmd.Parameters.AddWithValue("@translation", item.Translation);
        cmd.Parameters.AddWithValue("@sourceUrl", (object?)item.SourceUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isFallbackTitle", item.IsFallbackTitle ? 1 : 0);
        cmd.Parameters.AddWithValue("@createdAt", item.CreatedAt);
        await cmd.ExecuteNonQueryAsync();
        return item;
    }

    public async Task<int> AddBatchAsync(IEnumerable<CollectionItem> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return 0;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = """
            INSERT INTO collections (id, url, title, translation, source_url, is_fallback_title, created_at)
            VALUES (@id, @url, @title, @translation, @sourceUrl, @isFallbackTitle, @createdAt);
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
        cmd.Parameters.Add(pId);
        cmd.Parameters.Add(pUrl);
        cmd.Parameters.Add(pTitle);
        cmd.Parameters.Add(pTrans);
        cmd.Parameters.Add(pSrc);
        cmd.Parameters.Add(pFb);
        cmd.Parameters.Add(pCreated);

        foreach (var raw in list)
        {
            var item =
                string.IsNullOrEmpty(raw.Id)
                    ? raw with
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                    }
                : string.IsNullOrEmpty(raw.CreatedAt)
                    ? raw with
                    {
                        CreatedAt = DateTimeOffset.UtcNow.ToString("o"),
                    }
                : raw;
            pId.Value = item.Id;
            pUrl.Value = item.Url;
            pTitle.Value = item.Title;
            pTrans.Value = item.Translation;
            pSrc.Value = (object?)item.SourceUrl ?? DBNull.Value;
            pFb.Value = item.IsFallbackTitle ? 1 : 0;
            pCreated.Value = item.CreatedAt;
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return list.Count;
    }

    public async Task<(List<CollectionItem> Items, int Total)> ListAsync(
        int page,
        int pageSize,
        string? search
    )
    {
        var where = "";
        var hasSearch = !string.IsNullOrWhiteSpace(search);
        if (hasSearch)
            where = " WHERE url LIKE @s OR title LIKE @s OR translation LIKE @s ";

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"SELECT COUNT(*) FROM collections {where};";
            if (hasSearch)
                countCmd.Parameters.AddWithValue("@s", $"%{search}%");
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            if (total == 0)
                return ([], 0);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT id, url, title, translation, source_url, is_fallback_title, created_at
                FROM collections {where}
                ORDER BY created_at DESC
                LIMIT @limit OFFSET @offset;
                """;
            if (hasSearch)
                cmd.Parameters.AddWithValue("@s", $"%{search}%");
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

            var items = new List<CollectionItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(
                    new CollectionItem(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.GetInt64(5) != 0,
                        reader.GetString(6)
                    )
                );
            }
            return (items, total);
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM collections WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
