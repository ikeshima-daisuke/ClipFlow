using System;
using System.Collections.Generic;
using System.IO;
using ClipFlow.Models;
using Microsoft.Data.Sqlite;

namespace ClipFlow.Services;

/// <summary>SQLite による履歴永続化。最大保持件数を超えた古い項目（ピン留め以外）を自動削除。</summary>
public sealed class HistoryStore
{
    public const int DefaultMaxItems = 100;

    /// <summary>保持件数の上限（ピン留め以外）。0以下なら無制限。実行中に変更可能（次回Add/ApplyMaxItemsから反映）。</summary>
    public int MaxItems { get; set; }

    private readonly string _connString;
    private readonly string _imagesDir;

    /// <summary>本番用。%APPDATA%\ClipFlow を使い、保持件数はユーザー設定（既定100件）に従う。</summary>
    public HistoryStore() : this(AppPaths.DbPath, AppPaths.ImagesDir, AppSettings.Load().MaxHistoryItems ?? DefaultMaxItems)
    {
        AppPaths.EnsureCreated();
    }

    /// <summary>テスト等で保存先・保持件数を差し替えるためのコンストラクタ。</summary>
    public HistoryStore(string dbPath, string imagesDir, int maxItems = DefaultMaxItems)
    {
        MaxItems = maxItems;
        _imagesDir = imagesDir;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        System.IO.Directory.CreateDirectory(imagesDir);
        _connString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        Init();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connString);
        c.Open();
        return c;
    }

    private void Init()
    {
        using var c = Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS clips (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    kind       INTEGER NOT NULL,
                    text       TEXT,
                    image_path TEXT,
                    thumb_path TEXT,
                    preview    TEXT NOT NULL,
                    hash       TEXT NOT NULL,
                    pinned     INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_clips_hash ON clips(hash);
                """;
            cmd.ExecuteNonQuery();
        }
        MigrateAddColumnIfMissing(c, "html");
        MigrateAddColumnIfMissing(c, "rtf");
    }

    /// <summary>既存DB（旧バージョンで作成済み）に不足カラムがあれば追加する。</summary>
    private static void MigrateAddColumnIfMissing(SqliteConnection c, string column)
    {
        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('clips') WHERE name = $n;";
            check.Parameters.AddWithValue("$n", column);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0)
                return;
        }
        using var alter = c.CreateCommand();
        alter.CommandText = $"ALTER TABLE clips ADD COLUMN {column} TEXT;";
        alter.ExecuteNonQuery();
    }

    public List<ClipItem> GetAll()
    {
        var list = new List<ClipItem>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, kind, text, image_path, thumb_path, preview, hash, pinned, created_at, html, rtf
            FROM clips
            ORDER BY pinned DESC, created_at DESC;
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(Read(r));
        return list;
    }

    private static ClipItem Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Kind = (ClipKind)r.GetInt32(1),
        Text = r.IsDBNull(2) ? null : r.GetString(2),
        ImagePath = r.IsDBNull(3) ? null : r.GetString(3),
        ThumbPath = r.IsDBNull(4) ? null : r.GetString(4),
        Preview = r.GetString(5),
        Hash = r.GetString(6),
        Pinned = r.GetInt32(7) != 0,
        CreatedAt = DateTime.Parse(r.GetString(8)),
        Html = r.IsDBNull(9) ? null : r.GetString(9),
        Rtf = r.IsDBNull(10) ? null : r.GetString(10),
    };

    /// <summary>
    /// 追加。同一ハッシュが既にあれば新規作成せず先頭へ繰り上げ（created_at更新）。
    /// 返り値は保存後の項目（Id付き）。
    /// </summary>
    public ClipItem Add(ClipItem item)
    {
        using var c = Open();

        // 重複: 既存を先頭へ
        using (var find = c.CreateCommand())
        {
            find.CommandText = "SELECT id FROM clips WHERE hash = $h LIMIT 1;";
            find.Parameters.AddWithValue("$h", item.Hash);
            var existing = find.ExecuteScalar();
            if (existing is long existingId)
            {
                using var bump = c.CreateCommand();
                bump.CommandText = "UPDATE clips SET created_at = $t WHERE id = $id;";
                bump.Parameters.AddWithValue("$t", item.CreatedAt.ToString("o"));
                bump.Parameters.AddWithValue("$id", existingId);
                bump.ExecuteNonQuery();
                item.Id = existingId;
                return item;
            }
        }

        using (var ins = c.CreateCommand())
        {
            ins.CommandText = """
                INSERT INTO clips (kind, text, image_path, thumb_path, preview, hash, pinned, created_at, html, rtf)
                VALUES ($kind, $text, $img, $thumb, $prev, $hash, $pin, $t, $html, $rtf);
                SELECT last_insert_rowid();
                """;
            ins.Parameters.AddWithValue("$kind", (int)item.Kind);
            ins.Parameters.AddWithValue("$text", (object?)item.Text ?? DBNull.Value);
            ins.Parameters.AddWithValue("$img", (object?)item.ImagePath ?? DBNull.Value);
            ins.Parameters.AddWithValue("$thumb", (object?)item.ThumbPath ?? DBNull.Value);
            ins.Parameters.AddWithValue("$prev", item.Preview);
            ins.Parameters.AddWithValue("$hash", item.Hash);
            ins.Parameters.AddWithValue("$pin", item.Pinned ? 1 : 0);
            ins.Parameters.AddWithValue("$t", item.CreatedAt.ToString("o"));
            ins.Parameters.AddWithValue("$html", (object?)item.Html ?? DBNull.Value);
            ins.Parameters.AddWithValue("$rtf", (object?)item.Rtf ?? DBNull.Value);
            item.Id = (long)ins.ExecuteScalar()!;
        }

        EnforceLimit(c);
        return item;
    }

    /// <summary>MaxItems の変更を即座に反映させたいときに呼ぶ（上限を減らした場合、超過分をその場で削除）。</summary>
    public void ApplyMaxItems()
    {
        using var c = Open();
        EnforceLimit(c);
    }

    /// <summary>保持上限超過分（ピン留め以外）を古い順に削除し、画像ファイルも消す。MaxItems が0以下なら無制限（何もしない）。</summary>
    private void EnforceLimit(SqliteConnection c)
    {
        if (MaxItems <= 0)
            return;

        var toDelete = new List<(long id, string? img, string? thumb)>();
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = """
                SELECT id, image_path, thumb_path FROM clips
                WHERE pinned = 0
                ORDER BY created_at DESC
                LIMIT -1 OFFSET $max;
                """;
            sel.Parameters.AddWithValue("$max", MaxItems);
            using var r = sel.ExecuteReader();
            while (r.Read())
                toDelete.Add((r.GetInt64(0),
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2)));
        }

        foreach (var (id, img, thumb) in toDelete)
        {
            using var del = c.CreateCommand();
            del.CommandText = "DELETE FROM clips WHERE id = $id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
            TryDeleteFile(img);
            TryDeleteFile(thumb);
        }
    }

    public void Delete(long id)
    {
        using var c = Open();
        string? img = null, thumb = null;
        using (var sel = c.CreateCommand())
        {
            sel.CommandText = "SELECT image_path, thumb_path FROM clips WHERE id = $id;";
            sel.Parameters.AddWithValue("$id", id);
            using var r = sel.ExecuteReader();
            if (r.Read())
            {
                img = r.IsDBNull(0) ? null : r.GetString(0);
                thumb = r.IsDBNull(1) ? null : r.GetString(1);
            }
        }
        using (var del = c.CreateCommand())
        {
            del.CommandText = "DELETE FROM clips WHERE id = $id;";
            del.Parameters.AddWithValue("$id", id);
            del.ExecuteNonQuery();
        }
        // 同じ画像を他項目が参照していなければファイル削除
        if (!IsImageReferenced(c, img))
        {
            TryDeleteFile(img);
            TryDeleteFile(thumb);
        }
    }

    public void SetPinned(long id, bool pinned)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE clips SET pinned = $p WHERE id = $id;";
        cmd.Parameters.AddWithValue("$p", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Clear(bool includePinned)
    {
        using var c = Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = includePinned
                ? "DELETE FROM clips;"
                : "DELETE FROM clips WHERE pinned = 0;";
            cmd.ExecuteNonQuery();
        }
        // 参照されなくなった画像を掃除
        CleanupOrphanImages(c);
    }

    private static bool IsImageReferenced(SqliteConnection c, string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM clips WHERE image_path = $p;";
        cmd.Parameters.AddWithValue("$p", imagePath);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private void CleanupOrphanImages(SqliteConnection c)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT image_path, thumb_path FROM clips WHERE image_path IS NOT NULL;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!r.IsDBNull(0)) referenced.Add(r.GetString(0));
                if (!r.IsDBNull(1)) referenced.Add(r.GetString(1));
            }
        }
        try
        {
            foreach (var f in Directory.EnumerateFiles(_imagesDir))
                if (!referenced.Contains(f))
                    TryDeleteFile(f);
        }
        catch { /* 掃除失敗は無視 */ }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ロック中などは無視 */ }
    }
}
