using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using SlinClip.Models;

namespace SlinClip.Services;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public DatabaseService(string dbPath)
    {
        var dbDir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitDatabase();
    }

    private void InitDatabase()
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            // 启用高性能 WAL 模式与调优
            using var pragmaCmd = conn.CreateCommand();
            pragmaCmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA busy_timeout = 5000;
            ";
            pragmaCmd.ExecuteNonQuery();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS clips (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    type INTEGER NOT NULL,
                    content TEXT,
                    hash TEXT UNIQUE,
                    image_path TEXT,
                    thumbnail_path TEXT,
                    image_width INTEGER DEFAULT 0,
                    image_height INTEGER DEFAULT 0,
                    char_count INTEGER DEFAULT 0,
                    is_pinned INTEGER DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_clips_order ON clips(is_pinned DESC, updated_at DESC);
                CREATE INDEX IF NOT EXISTS idx_clips_hash ON clips(hash);
            ";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 插件启动时将最近的 N 条记录快速载入内存
    /// </summary>
    public List<ClipItem> LoadRecent(int limit = 1000)
    {
        var list = new List<ClipItem>(capacity: Math.Min(limit, 500));
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, type, content, hash, image_path, thumbnail_path, 
                       image_width, image_height, char_count, is_pinned, 
                       created_at, updated_at
                FROM clips
                ORDER BY is_pinned DESC, updated_at DESC
                LIMIT @limit;
            ";
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var content = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                list.Add(new ClipItem
                {
                    Id = reader.GetInt64(0),
                    Type = (ClipType)reader.GetInt32(1),
                    Content = content,
                    SearchText = content.ToLowerInvariant(),
                    Hash = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ThumbnailPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ImageWidth = reader.GetInt32(6),
                    ImageHeight = reader.GetInt32(7),
                    CharCount = reader.GetInt32(8),
                    IsPinned = reader.GetInt32(9) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(10)),
                    UpdatedAt = DateTime.Parse(reader.GetString(11))
                });
            }
        }
        return list;
    }

    /// <summary>
    /// 插入或更新剪贴板条目（若 Hash 存在则仅更新时间以置顶）
    /// </summary>
    public ClipItem Upsert(ClipItem item, int maxHistoryLimit = 500)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var tx = conn.BeginTransaction();
            try
            {
                var nowStr = DateTime.Now.ToString("o");

                // 检查是否已存在相同哈希
                using var checkCmd = conn.CreateCommand();
                checkCmd.Transaction = tx;
                checkCmd.CommandText = "SELECT id, is_pinned, image_path, thumbnail_path FROM clips WHERE hash = @hash LIMIT 1;";
                checkCmd.Parameters.AddWithValue("@hash", item.Hash);

                using (var reader = checkCmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        item.Id = reader.GetInt64(0);
                        item.IsPinned = reader.GetInt32(1) == 1;
                        item.UpdatedAt = DateTime.Now;

                        // 更新访问时间
                        using var updateCmd = conn.CreateCommand();
                        updateCmd.Transaction = tx;
                        updateCmd.CommandText = "UPDATE clips SET updated_at = @now WHERE id = @id;";
                        updateCmd.Parameters.AddWithValue("@now", nowStr);
                        updateCmd.Parameters.AddWithValue("@id", item.Id);
                        updateCmd.ExecuteNonQuery();

                        tx.Commit();
                        return item;
                    }
                }

                // 不存在则插入
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                    INSERT INTO clips (
                        type, content, hash, image_path, thumbnail_path,
                        image_width, image_height, char_count, is_pinned,
                        created_at, updated_at
                    ) VALUES (
                        @type, @content, @hash, @image_path, @thumbnail_path,
                        @image_width, @image_height, @char_count, @is_pinned,
                        @created_at, @updated_at
                    );
                    SELECT last_insert_rowid();
                ";
                insertCmd.Parameters.AddWithValue("@type", (int)item.Type);
                insertCmd.Parameters.AddWithValue("@content", (object?)item.Content ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@hash", item.Hash);
                insertCmd.Parameters.AddWithValue("@image_path", (object?)item.ImagePath ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@thumbnail_path", (object?)item.ThumbnailPath ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@image_width", item.ImageWidth);
                insertCmd.Parameters.AddWithValue("@image_height", item.ImageHeight);
                insertCmd.Parameters.AddWithValue("@char_count", item.CharCount);
                insertCmd.Parameters.AddWithValue("@is_pinned", item.IsPinned ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@created_at", nowStr);
                insertCmd.Parameters.AddWithValue("@updated_at", nowStr);

                item.Id = (long)(insertCmd.ExecuteScalar() ?? 0L);

                // 执行 LRU 淘汰旧数据
                PruneLruInternal(conn, tx, maxHistoryLimit);

                tx.Commit();
                return item;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// LRU 淘汰清理未置顶的超额旧记录并删除其关联图像文件
    /// </summary>
    private void PruneLruInternal(SqliteConnection conn, SqliteTransaction tx, int maxLimit)
    {
        // 查找需要淘汰的条目
        using var selectOldCmd = conn.CreateCommand();
        selectOldCmd.Transaction = tx;
        selectOldCmd.CommandText = @"
            SELECT id, image_path, thumbnail_path 
            FROM clips 
            WHERE is_pinned = 0 
            ORDER BY updated_at DESC 
            LIMIT -1 OFFSET @maxLimit;
        ";
        selectOldCmd.Parameters.AddWithValue("@maxLimit", maxLimit);

        var idsToDelete = new List<long>();
        var filesToDelete = new List<string>();

        using (var reader = selectOldCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                idsToDelete.Add(reader.GetInt64(0));
                if (!reader.IsDBNull(1)) filesToDelete.Add(reader.GetString(1));
                if (!reader.IsDBNull(2)) filesToDelete.Add(reader.GetString(2));
            }
        }

        if (idsToDelete.Count > 0)
        {
            using var delCmd = conn.CreateCommand();
            delCmd.Transaction = tx;
            delCmd.CommandText = $"DELETE FROM clips WHERE id IN ({string.Join(",", idsToDelete)});";
            delCmd.ExecuteNonQuery();

            // 异步删除物理图片文件，避免阻塞 SQLite 事务
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(file) && File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch { }
                }
            });
        }
    }

    public void TogglePin(long id, bool isPinned)
    {
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE clips SET is_pinned = @pinned, updated_at = @now WHERE id = @id;";
            cmd.Parameters.AddWithValue("@pinned", isPinned ? 1 : 0);
            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(long id)
    {
        string? imgPath = null;
        string? thumbPath = null;

        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var queryCmd = conn.CreateCommand();
            queryCmd.CommandText = "SELECT image_path, thumbnail_path FROM clips WHERE id = @id;";
            queryCmd.Parameters.AddWithValue("@id", id);

            using (var reader = queryCmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    imgPath = reader.IsDBNull(0) ? null : reader.GetString(0);
                    thumbPath = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
            }

            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM clips WHERE id = @id;";
            delCmd.Parameters.AddWithValue("@id", id);
            delCmd.ExecuteNonQuery();
        }

        if (!string.IsNullOrEmpty(imgPath) && File.Exists(imgPath))
        {
            try { File.Delete(imgPath); } catch { }
        }
        if (!string.IsNullOrEmpty(thumbPath) && File.Exists(thumbPath))
        {
            try { File.Delete(thumbPath); } catch { }
        }
    }

    public void ClearAll(bool preservePinned = true)
    {
        var filesToDelete = new List<string>();

        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var queryCmd = conn.CreateCommand();
            queryCmd.CommandText = preservePinned 
                ? "SELECT image_path, thumbnail_path FROM clips WHERE is_pinned = 0;"
                : "SELECT image_path, thumbnail_path FROM clips;";

            using (var reader = queryCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0)) filesToDelete.Add(reader.GetString(0));
                    if (!reader.IsDBNull(1)) filesToDelete.Add(reader.GetString(1));
                }
            }

            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = preservePinned 
                ? "DELETE FROM clips WHERE is_pinned = 0;"
                : "DELETE FROM clips;";
            delCmd.ExecuteNonQuery();
        }

        foreach (var file in filesToDelete)
        {
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch { }
        }
    }

    public void Dispose()
    {
        // 释放可能占用的连接资源
        SqliteConnection.ClearAllPools();
    }
}
