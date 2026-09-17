using System.Text.Json;
using Admplus.Api.Models;
using Microsoft.Data.Sqlite;

namespace Admplus.Api.Services;

public sealed class SqlitePersistence : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _cn;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public SqlitePersistence(string dbPath)
    {
        _dbPath = dbPath;
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        _cn = new SqliteConnection(cs);
        _cn.Open();
        using (var pragma = _cn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;";
            pragma.ExecuteNonQuery();
        }
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS items (kind TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(kind, id));
            """;
        cmd.ExecuteNonQuery();
    }

    public bool HasData()
    {
        using var cmd = _cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM kv WHERE key = 'settings'";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public DirectoryState? Load()
    {
        if (!HasData()) return null;
        var state = new DirectoryState();
        using (var cmd = _cn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, json FROM kv";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                var json = r.GetString(1);
                if (key == "settings") state.Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new();
                else if (key == "securityHealth") state.SecurityHealth = JsonSerializer.Deserialize<SecurityHealth>(json, JsonOpts) ?? new();
            }
        }

        using (var cmd = _cn.CreateCommand())
        {
            cmd.CommandText = "SELECT kind, id, json FROM items";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var kind = r.GetString(0);
                var id = r.GetString(1);
                var json = r.GetString(2);
                _cache[kind + "\0" + id] = json;
                switch (kind)
                {
                    case "users": Add(state.Users, json); break;
                    case "computers": Add(state.Computers, json); break;
                    case "groups": Add(state.Groups, json); break;
                    case "gpos": Add(state.Gpos, json); break;
                    case "shares": Add(state.Shares, json); break;
                    case "laps": Add(state.Laps, json); break;
                    case "audit": Add(state.Audit, json); break;
                    case "ous": Add(state.Ous, json); break;
                    case "recycleBin": Add(state.RecycleBin, json); break;
                    case "jitGrants": Add(state.JitGrants, json); break;
                    case "passwordPolicies": Add(state.PasswordPolicies, json); break;
                    case "gpoBackups": Add(state.GpoBackups, json); break;
                    case "bitLockerKeys": Add(state.BitLockerKeys, json); break;
                    case "spns": Add(state.Spns, json); break;
                    case "operators": Add(state.Operators, json); break;
                    case "lapsGrants": Add(state.LapsGrants, json); break;
                    case "hybrid": Add(state.Hybrid, json); break;
                }
            }
        }
        return state;
    }

    public void Save(DirectoryState state)
    {
        using var tx = _cn.BeginTransaction();
        PutKv(tx, "settings", state.Settings);
        PutKv(tx, "securityHealth", state.SecurityHealth);
        Sync(tx, "users", state.Users, u => u.Id);
        Sync(tx, "computers", state.Computers, c => c.Id);
        Sync(tx, "groups", state.Groups, g => g.Id);
        Sync(tx, "gpos", state.Gpos, g => g.Id);
        Sync(tx, "shares", state.Shares, s => s.Id);
        Sync(tx, "laps", state.Laps, l => l.Id);
        Sync(tx, "audit", state.Audit, a => a.Id);
        Sync(tx, "ous", state.Ous, o => o.Id);
        Sync(tx, "recycleBin", state.RecycleBin, r => r.Id);
        Sync(tx, "jitGrants", state.JitGrants, j => j.Id);
        Sync(tx, "passwordPolicies", state.PasswordPolicies, p => p.Id);
        Sync(tx, "gpoBackups", state.GpoBackups, b => b.Id);
        Sync(tx, "bitLockerKeys", state.BitLockerKeys, b => b.Id);
        Sync(tx, "spns", state.Spns, s => s.Id);
        Sync(tx, "operators", state.Operators, o => o.Id);
        Sync(tx, "lapsGrants", state.LapsGrants, g => g.Id);
        Sync(tx, "hybrid", state.Hybrid, h => h.UserId);
        tx.Commit();
    }

    private void PutKv<T>(SqliteTransaction tx, string key, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        var cacheKey = "kv\0" + key;
        if (_cache.TryGetValue(cacheKey, out var prev) && prev == json) return;
        using var cmd = _cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO kv(key, json) VALUES ($k, $j) ON CONFLICT(key) DO UPDATE SET json = excluded.json";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$j", json);
        cmd.ExecuteNonQuery();
        _cache[cacheKey] = json;
    }

    private void Sync<T>(SqliteTransaction tx, string kind, IEnumerable<T> items, Func<T, string> idOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var upsert = _cn.CreateCommand();
        upsert.Transaction = tx;
        upsert.CommandText = "INSERT INTO items(kind, id, json) VALUES ($k, $i, $j) ON CONFLICT(kind, id) DO UPDATE SET json = excluded.json";
        var pKind = upsert.Parameters.AddWithValue("$k", kind);
        var pId = upsert.Parameters.Add("$i", SqliteType.Text);
        var pJson = upsert.Parameters.Add("$j", SqliteType.Text);

        foreach (var item in items)
        {
            var id = idOf(item);
            if (string.IsNullOrEmpty(id)) continue;
            seen.Add(id);
            var json = JsonSerializer.Serialize(item, JsonOpts);
            var cacheKey = kind + "\0" + id;
            if (_cache.TryGetValue(cacheKey, out var prev) && prev == json) continue;
            pId.Value = id;
            pJson.Value = json;
            upsert.ExecuteNonQuery();
            _cache[cacheKey] = json;
        }

        var stale = _cache.Keys.Where(k => k.StartsWith(kind + "\0", StringComparison.Ordinal) && !seen.Contains(k[(kind.Length + 1)..])).ToList();
        if (stale.Count == 0) return;
        using var del = _cn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM items WHERE kind = $k AND id = $i";
        del.Parameters.AddWithValue("$k", kind);
        var dId = del.Parameters.Add("$i", SqliteType.Text);
        foreach (var key in stale)
        {
            dId.Value = key[(kind.Length + 1)..];
            del.ExecuteNonQuery();
            _cache.Remove(key);
        }
    }

    private static void Add<T>(List<T> list, string json)
    {
        var item = JsonSerializer.Deserialize<T>(json, JsonOpts);
        if (item != null) list.Add(item);
    }

    public void Dispose() => _cn.Dispose();
}
