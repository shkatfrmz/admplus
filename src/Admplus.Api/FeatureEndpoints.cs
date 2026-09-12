using System.Text;
using System.Text.Json;
using Admplus.Api.Models;
using Admplus.Api.Services;

namespace Admplus.Api;

public static class FeatureEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void MapFeatures(this WebApplication app)
    {
        app.MapGet("/api/ous", (DirectoryStore store) =>
        {
            var s = store.Snapshot();
            var nodes = s.Ous.Select(o => new
            {
                o.Id, o.Name, o.Dn, o.ParentId, o.Description, o.Created,
                users = s.Users.Count(u => string.Equals(u.Ou, o.Dn, StringComparison.OrdinalIgnoreCase)),
                computers = s.Computers.Count(c => string.Equals(c.Ou, o.Dn, StringComparison.OrdinalIgnoreCase)),
                groups = s.Groups.Count(g => string.Equals(g.Ou, o.Dn, StringComparison.OrdinalIgnoreCase)),
                gpos = s.Gpos.Count(g => g.LinkedOus.Any(l => string.Equals(l, o.Dn, StringComparison.OrdinalIgnoreCase)))
            }).ToList();
            return Results.Json(new { items = nodes, total = nodes.Count });
        });

        app.MapGet("/api/ous/{id}", (string id, DirectoryStore store) =>
        {
            var s = store.Snapshot();
            var ou = s.Ous.FirstOrDefault(o => o.Id == id);
            if (ou is null) return Results.Json(new { message = "OU not found" }, statusCode: 404);
            return Results.Json(new
            {
                ou.Id, ou.Name, ou.Dn, ou.ParentId, ou.Description, ou.Created,
                users = s.Users.Where(u => string.Equals(u.Ou, ou.Dn, StringComparison.OrdinalIgnoreCase)),
                computers = s.Computers.Where(c => string.Equals(c.Ou, ou.Dn, StringComparison.OrdinalIgnoreCase)),
                groups = s.Groups.Where(g => string.Equals(g.Ou, ou.Dn, StringComparison.OrdinalIgnoreCase)),
                children = s.Ous.Where(o => o.ParentId == ou.Id)
            });
        });

        app.MapPost("/api/ous", (OrganizationalUnit body, DirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.Json(new { message = "Name is required" }, statusCode: 400);
            var item = store.Update(s =>
            {
                var parent = s.Ous.FirstOrDefault(o => o.Id == body.ParentId) ?? s.Ous.FirstOrDefault(o => o.ParentId == "");
                var dn = string.IsNullOrWhiteSpace(body.Dn)
                    ? $"OU={body.Name},{(parent?.Dn ?? "DC=contoso,DC=local")}"
                    : body.Dn;
                var created = new OrganizationalUnit
                {
                    Id = DirectoryStore.NewId("ou"),
                    Name = body.Name,
                    Dn = dn,
                    ParentId = parent?.Id ?? "",
                    Description = body.Description ?? "",
                    Created = DateTimeOffset.UtcNow
                };
                s.Ous.Add(created);
                DirectoryStore.AddAudit(s, "create", "ou", created.Name, $"Created OU {created.Dn}");
                return created;
            });
            return Results.Json(item, statusCode: 201);
        });

        app.MapPost("/api/ous/move", (JsonElement body, DirectoryStore store) =>
        {
            var objectId = body.GetString("objectId");
            var objectType = body.GetString("objectType");
            var ouId = body.GetString("ouId");
            if (string.IsNullOrEmpty(objectId) || string.IsNullOrEmpty(objectType) || string.IsNullOrEmpty(ouId))
                return Results.Json(new { message = "objectId, objectType, and ouId are required" }, statusCode: 400);
            store.Update(s =>
            {
                var ou = s.Ous.FirstOrDefault(o => o.Id == ouId) ?? throw new KeyNotFoundException("OU not found");
                if (objectType == "user")
                {
                    var u = s.Users.FirstOrDefault(x => x.Id == objectId) ?? throw new KeyNotFoundException();
                    u.Ou = ou.Dn;
                    DirectoryStore.AddAudit(s, "move", "user", u.SamAccountName, $"Moved to {ou.Dn}");
                }
                else if (objectType == "computer")
                {
                    var c = s.Computers.FirstOrDefault(x => x.Id == objectId) ?? throw new KeyNotFoundException();
                    c.Ou = ou.Dn;
                    DirectoryStore.AddAudit(s, "move", "computer", c.Name, $"Moved to {ou.Dn}");
                }
                else if (objectType == "group")
                {
                    var g = s.Groups.FirstOrDefault(x => x.Id == objectId) ?? throw new KeyNotFoundException();
                    g.Ou = ou.Dn;
                    DirectoryStore.AddAudit(s, "move", "group", g.Name, $"Moved to {ou.Dn}");
                }
                else if (objectType == "ou")
                {
                    var child = s.Ous.FirstOrDefault(x => x.Id == objectId) ?? throw new KeyNotFoundException();
                    child.ParentId = ou.Id;
                    child.Dn = $"OU={child.Name},{ou.Dn}";
                    DirectoryStore.AddAudit(s, "move", "ou", child.Name, $"Moved under {ou.Dn}");
                }
            });
            return Results.Json(new { ok = true });
        });

        app.MapGet("/api/hybrid", (DirectoryStore store) =>
        {
            var s = store.Snapshot();
            ExpireJit(store);
            var items = s.Users.Select(u =>
            {
                var h = s.Hybrid.FirstOrDefault(x => x.UserId == u.Id);
                return new
                {
                    userId = u.Id,
                    displayName = u.DisplayName,
                    samAccountName = u.SamAccountName,
                    upn = u.UserPrincipalName,
                    onPremSid = h?.OnPremSid ?? "",
                    immutableId = h?.ImmutableId ?? u.ImmutableId,
                    sourceAnchor = h?.SourceAnchor ?? u.ImmutableId,
                    syncStatus = h?.SyncStatus ?? u.SyncStatus,
                    passwordHashSync = h?.PasswordHashSync ?? u.PasswordHashSync,
                    passThroughAuth = h?.PassThroughAuth ?? false,
                    lastSync = h?.LastSync,
                    cloudUpn = h?.CloudUpn ?? ""
                };
            }).ToList();
            return Results.Json(new
            {
                items,
                summary = new
                {
                    synced = items.Count(i => i.syncStatus == "synced"),
                    pending = items.Count(i => i.syncStatus == "pending"),
                    onPrem = items.Count(i => i.syncStatus == "onPrem"),
                    error = items.Count(i => i.syncStatus == "error"),
                    phs = items.Count(i => i.passwordHashSync)
                }
            });
        });

        app.MapPost("/api/hybrid/{userId}/sync", (string userId, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var u = s.Users.FirstOrDefault(x => x.Id == userId) ?? throw new KeyNotFoundException();
                var h = s.Hybrid.FirstOrDefault(x => x.UserId == userId);
                if (h == null)
                {
                    h = new HybridIdentity { UserId = u.Id, DisplayName = u.DisplayName };
                    s.Hybrid.Add(h);
                }
                if (string.IsNullOrEmpty(h.ImmutableId))
                    h.ImmutableId = Convert.ToBase64String(Encoding.UTF8.GetBytes(u.SamAccountName));
                h.SourceAnchor = h.ImmutableId;
                h.SyncStatus = "synced";
                h.PasswordHashSync = true;
                h.LastSync = DateTimeOffset.UtcNow;
                h.CloudUpn = string.IsNullOrEmpty(h.CloudUpn) ? u.SamAccountName + "@contoso.onmicrosoft.com" : h.CloudUpn;
                u.ImmutableId = h.ImmutableId;
                u.SyncStatus = "synced";
                u.PasswordHashSync = true;
                DirectoryStore.AddAudit(s, "sync", "hybrid", u.SamAccountName, "Forced Azure AD Connect sync / ImmutableId stamp");
                return h;
            });
            return Results.Json(item);
        });

        app.MapGet("/api/jit", (DirectoryStore store) =>
        {
            ExpireJit(store);
            var s = store.Snapshot();
            var items = s.JitGrants.Select(j => EnrichJit(s, j)).ToList();
            return Results.Json(new { items, total = items.Count });
        });

        app.MapPost("/api/jit", (JitGrant body, DirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.UserId) || string.IsNullOrWhiteSpace(body.GroupId))
                return Results.Json(new { message = "userId and groupId are required" }, statusCode: 400);
            var hours = body.ExpiresAt == default ? 4 : Math.Max(1, (int)(body.ExpiresAt - DateTimeOffset.UtcNow).TotalHours);
            var item = store.Update(s =>
            {
                var user = s.Users.FirstOrDefault(u => u.Id == body.UserId) ?? throw new KeyNotFoundException("User not found");
                var group = s.Groups.FirstOrDefault(g => g.Id == body.GroupId) ?? throw new KeyNotFoundException("Group not found");
                var grant = new JitGrant
                {
                    Id = DirectoryStore.NewId("jit"),
                    UserId = user.Id,
                    GroupId = group.Id,
                    Reason = body.Reason ?? "",
                    Ticket = body.Ticket ?? "",
                    Status = "active",
                    StartsAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(hours <= 0 ? 4 : hours),
                    RequestedBy = string.IsNullOrEmpty(s.Settings.CurrentOperatorId) ? "op-admin" : s.Settings.CurrentOperatorId
                };
                if (!group.Members.Contains(user.Id)) group.Members.Add(user.Id);
                if (!user.Groups.Contains(group.Id)) user.Groups.Add(group.Id);
                s.JitGrants.Insert(0, grant);
                DirectoryStore.AddAudit(s, "jit-grant", "group", group.Name, $"JIT membership for {user.SamAccountName} until {grant.ExpiresAt:u}");
                return grant;
            });
            return Results.Json(item, statusCode: 201);
        });

        app.MapPost("/api/jit/{id}/revoke", (string id, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var g = s.JitGrants.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                RevokeJit(s, g);
                return g;
            });
            return Results.Json(item);
        });

        app.MapGet("/api/password-policies", (DirectoryStore store) =>
            Results.Json(new { items = store.Snapshot().PasswordPolicies }));

        app.MapPost("/api/password-policies", (PasswordSettingsObject body, DirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.Json(new { message = "Name is required" }, statusCode: 400);
            var item = store.Update(s =>
            {
                var created = new PasswordSettingsObject
                {
                    Id = DirectoryStore.NewId("pso"),
                    Name = body.Name,
                    Precedence = body.Precedence == 0 ? 100 : body.Precedence,
                    MinLength = body.MinLength == 0 ? 12 : body.MinLength,
                    HistoryCount = body.HistoryCount,
                    MaxAgeDays = body.MaxAgeDays == 0 ? 90 : body.MaxAgeDays,
                    MinAgeDays = body.MinAgeDays,
                    Complexity = body.Complexity,
                    LockoutThreshold = body.LockoutThreshold,
                    LockoutMinutes = body.LockoutMinutes,
                    AppliesTo = body.AppliesTo ?? new()
                };
                s.PasswordPolicies.Add(created);
                DirectoryStore.AddAudit(s, "create", "pso", created.Name, "Created fine-grained password policy");
                return created;
            });
            return Results.Json(item, statusCode: 201);
        });

        app.MapPut("/api/password-policies/{id}", (string id, PasswordSettingsObject body, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var p = s.PasswordPolicies.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                p.Name = body.Name ?? p.Name;
                if (body.Precedence != 0) p.Precedence = body.Precedence;
                if (body.MinLength != 0) p.MinLength = body.MinLength;
                p.HistoryCount = body.HistoryCount;
                if (body.MaxAgeDays != 0) p.MaxAgeDays = body.MaxAgeDays;
                p.MinAgeDays = body.MinAgeDays;
                p.Complexity = body.Complexity;
                p.LockoutThreshold = body.LockoutThreshold;
                p.LockoutMinutes = body.LockoutMinutes;
                if (body.AppliesTo != null) p.AppliesTo = body.AppliesTo;
                DirectoryStore.AddAudit(s, "update", "pso", p.Name, "Updated PSO");
                return p;
            });
            return Results.Json(item);
        });

        app.MapPost("/api/password-policies/simulate", (JsonElement body, DirectoryStore store) =>
        {
            var password = body.GetString("password") ?? "";
            var userId = body.GetString("userId");
            var s = store.Snapshot();
            var user = s.Users.FirstOrDefault(u => u.Id == userId) ?? s.Users.First();
            var pso = EffectivePso(s, user);
            var issues = new List<string>();
            if (password.Length < pso.MinLength) issues.Add($"Shorter than minimum length {pso.MinLength}");
            if (pso.Complexity)
            {
                var cats = 0;
                if (password.Any(char.IsUpper)) cats++;
                if (password.Any(char.IsLower)) cats++;
                if (password.Any(char.IsDigit)) cats++;
                if (password.Any(ch => !char.IsLetterOrDigit(ch))) cats++;
                if (cats < 3) issues.Add("Complexity requires 3 of: upper, lower, digit, symbol");
                if (password.Contains(user.SamAccountName, StringComparison.OrdinalIgnoreCase) ||
                    (user.GivenName.Length > 2 && password.Contains(user.GivenName, StringComparison.OrdinalIgnoreCase)))
                    issues.Add("Password contains account name");
            }
            return Results.Json(new
            {
                user = new { user.Id, user.DisplayName, user.SamAccountName },
                policy = pso,
                passed = issues.Count == 0,
                issues
            });
        });

        app.MapGet("/api/gpos/{id}/rsop", (string id, DirectoryStore store, string? ou) =>
        {
            var s = store.Snapshot();
            var target = s.Gpos.FirstOrDefault(g => g.Id == id);
            if (target is null) return Results.Json(new { message = "GPO not found" }, statusCode: 404);
            var linked = string.IsNullOrEmpty(ou)
                ? s.Gpos.Where(g => g.LinkedOus.Intersect(target.LinkedOus).Any() || g.Id == id).ToList()
                : s.Gpos.Where(g => g.LinkedOus.Any(l => ou.EndsWith(l, StringComparison.OrdinalIgnoreCase) || l.Equals(ou, StringComparison.OrdinalIgnoreCase))).ToList();
            if (!linked.Any(g => g.Id == id)) linked.Add(target);
            var winner = new Dictionary<string, object>();
            var trace = new List<object>();
            foreach (var g in linked.OrderBy(g => g.Enforced ? 0 : 1).ThenBy(g => g.Name))
            {
                foreach (var kv in g.Settings)
                {
                    var overwritten = winner.ContainsKey(kv.Key);
                    winner[kv.Key] = kv.Value;
                    trace.Add(new { gpo = g.Name, setting = kv.Key, value = kv.Value, enforced = g.Enforced, overwritten });
                }
            }
            return Results.Json(new { gpo = target, resultingSet = winner, trace, computers = s.Computers.Where(c => target.LinkedOus.Contains(c.Ou)).Select(c => c.Name) });
        });

        app.MapGet("/api/gpos/{id}/backups", (string id, DirectoryStore store) =>
            Results.Json(new { items = store.Snapshot().GpoBackups.Where(b => b.GpoId == id).ToList() }));

        app.MapPost("/api/gpos/{id}/backup", (string id, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var g = s.Gpos.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                var bak = new GpoBackup
                {
                    Id = DirectoryStore.NewId("gb"),
                    GpoId = g.Id,
                    Name = g.Name,
                    Created = DateTimeOffset.UtcNow,
                    Snapshot = new Dictionary<string, object>
                    {
                        ["status"] = g.Status,
                        ["enforced"] = g.Enforced,
                        ["description"] = g.Description,
                        ["linkedOus"] = g.LinkedOus,
                        ["settings"] = g.Settings
                    }
                };
                s.GpoBackups.Insert(0, bak);
                DirectoryStore.AddAudit(s, "backup", "gpo", g.Name, "Created GPO backup");
                return bak;
            });
            return Results.Json(item, statusCode: 201);
        });

        app.MapPost("/api/gpos/{id}/restore/{backupId}", (string id, string backupId, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var g = s.Gpos.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                var bak = s.GpoBackups.FirstOrDefault(b => b.Id == backupId && b.GpoId == id) ?? throw new KeyNotFoundException("Backup not found");
                if (bak.Snapshot.TryGetValue("status", out var st)) g.Status = st.ToString() ?? g.Status;
                if (bak.Snapshot.TryGetValue("enforced", out var en) && en is bool b) g.Enforced = b;
                if (bak.Snapshot.TryGetValue("description", out var d)) g.Description = d.ToString() ?? g.Description;
                g.Modified = DateTimeOffset.UtcNow;
                DirectoryStore.AddAudit(s, "restore", "gpo", g.Name, $"Restored from backup {backupId}");
                return g;
            });
            return Results.Json(item);
        });

        app.MapGet("/api/bitlocker", (DirectoryStore store, string? q) =>
        {
            var items = store.Snapshot().BitLockerKeys.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var n = q.ToLowerInvariant();
                items = items.Where(k => k.ComputerName.ToLowerInvariant().Contains(n) || k.RecoveryGuid.ToLowerInvariant().Contains(n));
            }
            var list = items.Select(k => new
            {
                k.Id, k.ComputerId, k.ComputerName, k.RecoveryGuid, k.Volume, k.Created, hasPassword = true
            }).ToList();
            return Results.Json(new { items = list, total = list.Count });
        });

        app.MapGet("/api/bitlocker/{id}", (string id, DirectoryStore store) =>
        {
            BitLockerKey? item = null;
            store.Update(s =>
            {
                item = s.BitLockerKeys.FirstOrDefault(x => x.Id == id);
                if (item != null)
                    DirectoryStore.AddAudit(s, "view-recovery", "bitlocker", item.ComputerName, $"Retrieved BitLocker key {item.RecoveryGuid}");
            });
            return item is null ? Results.Json(new { message = "Key not found" }, statusCode: 404) : Results.Json(item);
        });

        app.MapPost("/api/bitlocker", (BitLockerKey body, DirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.ComputerId))
                return Results.Json(new { message = "computerId is required" }, statusCode: 400);
            var item = store.Update(s =>
            {
                var c = s.Computers.FirstOrDefault(x => x.Id == body.ComputerId) ?? throw new KeyNotFoundException("Computer not found");
                var created = new BitLockerKey
                {
                    Id = DirectoryStore.NewId("bl"),
                    ComputerId = c.Id,
                    ComputerName = c.Name,
                    RecoveryGuid = string.IsNullOrWhiteSpace(body.RecoveryGuid) ? Guid.NewGuid().ToString() : body.RecoveryGuid,
                    RecoveryPassword = string.IsNullOrWhiteSpace(body.RecoveryPassword) ? RandomRecovery() : body.RecoveryPassword,
                    Volume = string.IsNullOrWhiteSpace(body.Volume) ? "C:" : body.Volume,
                    Created = DateTimeOffset.UtcNow
                };
                s.BitLockerKeys.Add(created);
                DirectoryStore.AddAudit(s, "create", "bitlocker", c.Name, "Stored BitLocker recovery password");
                return created;
            });
            return Results.Json(item, statusCode: 201);
        });

        app.MapGet("/api/stale", (DirectoryStore store, int days = 90) =>
        {
            var s = store.Snapshot();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, days));
            var users = s.Users.Where(u => u.LastLogon is null || u.LastLogon < cutoff).ToList();
            var computers = s.Computers.Where(c => c.Type != "domainController" && (c.LastLogon is null || c.LastLogon < cutoff)).ToList();
            return Results.Json(new { days, cutoff, users, computers, userCount = users.Count, computerCount = computers.Count });
        });

        app.MapPost("/api/stale/cleanup", (JsonElement body, DirectoryStore store) =>
        {
            var ids = body.TryGetProperty("ids", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : new List<string>();
            var type = body.GetString("objectType") ?? "user";
            var moved = store.Update(s =>
            {
                var count = 0;
                if (type == "computer")
                {
                    foreach (var id in ids)
                    {
                        var c = s.Computers.FirstOrDefault(x => x.Id == id);
                        if (c == null) continue;
                        DirectoryStore.Recycle(s, "computer", c.Id, c.Name, c.Ou, c);
                        s.Computers.Remove(c);
                        s.Laps.RemoveAll(l => l.ComputerId == id);
                        count++;
                    }
                }
                else
                {
                    foreach (var id in ids)
                    {
                        var u = s.Users.FirstOrDefault(x => x.Id == id);
                        if (u == null) continue;
                        DirectoryStore.Recycle(s, "user", u.Id, u.DisplayName, u.Ou, u);
                        s.Users.Remove(u);
                        foreach (var g in s.Groups) g.Members.RemoveAll(m => m == id);
                        count++;
                    }
                }
                DirectoryStore.AddAudit(s, "cleanup", "stale", type, $"Moved {count} stale {type}(s) to recycle bin");
                return count;
            });
            return Results.Json(new { ok = true, moved });
        });

        app.MapGet("/api/recycle-bin", (DirectoryStore store) =>
            Results.Json(new { items = store.Snapshot().RecycleBin, total = store.Snapshot().RecycleBin.Count }));

        app.MapPost("/api/recycle-bin/{id}/restore", (string id, DirectoryStore store) =>
        {
            store.Update(s =>
            {
                var item = s.RecycleBin.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                if (item.ObjectType == "user")
                {
                    var u = JsonSerializer.Deserialize<DirectoryUser>(item.PayloadJson, JsonOpts);
                    if (u != null && s.Users.All(x => x.Id != u.Id)) s.Users.Add(u);
                }
                else if (item.ObjectType == "computer")
                {
                    var c = JsonSerializer.Deserialize<DirectoryComputer>(item.PayloadJson, JsonOpts);
                    if (c != null && s.Computers.All(x => x.Id != c.Id)) s.Computers.Add(c);
                }
                else if (item.ObjectType == "group")
                {
                    var g = JsonSerializer.Deserialize<DirectoryGroup>(item.PayloadJson, JsonOpts);
                    if (g != null && s.Groups.All(x => x.Id != g.Id)) s.Groups.Add(g);
                }
                s.RecycleBin.RemoveAll(x => x.Id == id);
                DirectoryStore.AddAudit(s, "restore", "recycle-bin", item.Name, $"Restored {item.ObjectType} {item.Name}");
            });
            return Results.Json(new { ok = true });
        });

        app.MapPost("/api/import/users", (JsonElement body, DirectoryStore store) =>
        {
            if (!body.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return Results.Json(new { message = "rows array is required" }, statusCode: 400);
            var created = 0;
            var skipped = 0;
            store.Update(s =>
            {
                var domain = string.IsNullOrWhiteSpace(s.Settings.DomainController.Domain) ? "contoso.local" : s.Settings.DomainController.Domain;
                foreach (var row in rows.EnumerateArray())
                {
                    var sam = row.GetString("samAccountName") ?? row.GetString("sAMAccountName") ?? "";
                    var display = row.GetString("displayName") ?? sam;
                    if (string.IsNullOrWhiteSpace(sam)) { skipped++; continue; }
                    if (s.Users.Any(u => u.SamAccountName.Equals(sam, StringComparison.OrdinalIgnoreCase))) { skipped++; continue; }
                    var u = new DirectoryUser
                    {
                        Id = DirectoryStore.NewId("u"),
                        SamAccountName = sam,
                        DisplayName = display,
                        GivenName = row.GetString("givenName") ?? "",
                        Surname = row.GetString("surname") ?? "",
                        Email = row.GetString("email") ?? "",
                        Department = row.GetString("department") ?? "",
                        Title = row.GetString("title") ?? "",
                        Type = row.GetString("type") ?? "user",
                        UserPrincipalName = row.GetString("userPrincipalName") ?? $"{sam}@{domain}",
                        Ou = row.GetString("ou") ?? "CN=Users,DC=contoso,DC=local",
                        Enabled = true,
                        MustChangePassword = true,
                        Groups = { "g-domain-users" },
                        Created = DateTimeOffset.UtcNow,
                        SyncStatus = "onPrem"
                    };
                    s.Users.Add(u);
                    created++;
                }
                DirectoryStore.AddAudit(s, "import", "user", "csv", $"Imported {created} users ({skipped} skipped)");
            });
            return Results.Json(new { ok = true, created, skipped });
        });

        app.MapGet("/api/export/powershell", (DirectoryStore store) =>
        {
            var s = store.Snapshot();
            var sb = new StringBuilder();
            sb.AppendLine("# Admplus export — Windows PowerShell / ActiveDirectory module");
            sb.AppendLine("Import-Module ActiveDirectory");
            foreach (var u in s.Users)
            {
                sb.AppendLine($"New-ADUser -Name '{Escape(u.DisplayName)}' -SamAccountName '{Escape(u.SamAccountName)}' -UserPrincipalName '{Escape(u.UserPrincipalName)}' -Enabled ${(u.Enabled ? "$true" : "$false")} -Path '{Escape(ParentDn(u.Ou))}' -Department '{Escape(u.Department)}' -Title '{Escape(u.Title)}'");
            }
            foreach (var c in s.Computers)
            {
                sb.AppendLine($"New-ADComputer -Name '{Escape(c.Name)}' -DNSHostName '{Escape(c.DnsHostName)}' -Path '{Escape(ParentDn(c.Ou))}' -Enabled ${(c.Enabled ? "$true" : "$false")}");
            }
            foreach (var g in s.Groups)
            {
                var groupCat = g.Type == "distribution" ? "Distribution" : "Security";
                var scope = g.Scope == "universal" ? "Universal" : g.Scope == "domainLocal" ? "DomainLocal" : "Global";
                sb.AppendLine($"New-ADGroup -Name '{Escape(g.Name)}' -GroupScope {scope} -GroupCategory {groupCat} -Path '{Escape(ParentDn(g.Ou))}'");
            }
            return Results.Text(sb.ToString(), "text/plain");
        });

        app.MapGet("/api/export/csharp", (DirectoryStore store) =>
        {
            var s = store.Snapshot();
            var sb = new StringBuilder();
            sb.AppendLine("// Admplus export — System.DirectoryServices.Protocols");
            sb.AppendLine("using System.DirectoryServices.Protocols;");
            sb.AppendLine("var connection = new LdapConnection(new LdapDirectoryIdentifier(\"" + (s.Settings.DomainController.Host.Length > 0 ? s.Settings.DomainController.Host : "dc01.contoso.local") + "\"));");
            foreach (var u in s.Users.Take(25))
            {
                sb.AppendLine($"// New-ADUser {u.SamAccountName}");
                sb.AppendLine($"connection.SendRequest(new AddRequest(\"CN={Escape(u.DisplayName)},{Escape(ParentDn(u.Ou))}\", new DirectoryAttribute(\"objectClass\", \"user\"), new DirectoryAttribute(\"sAMAccountName\", \"{Escape(u.SamAccountName)}\")));");
            }
            return Results.Text(sb.ToString(), "text/plain");
        });

        app.MapGet("/api/security/health", (DirectoryStore store) =>
        {
            var s = store.Snapshot();
            var spnIndex = new Dictionary<string, List<SpnRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in AllSpns(s))
            {
                if (!spnIndex.TryGetValue(rec.Spn, out var list))
                {
                    list = new List<SpnRecord>();
                    spnIndex[rec.Spn] = list;
                }
                list.Add(rec);
            }
            var duplicates = spnIndex.Where(kv => kv.Value.Select(v => v.PrincipalId).Distinct().Count() > 1)
                .Select(kv => new { spn = kv.Key, principals = kv.Value.Select(v => new { v.Principal, v.PrincipalId, v.Kind }).ToList() })
                .ToList();
            var findings = new List<object>();
            if (!s.SecurityHealth.NtlmV1Disabled) findings.Add(new { severity = "high", code = "NTLMv1", detail = "LM/NTLMv1 is still accepted. Disable via Network security: LAN Manager authentication level." });
            if (!s.SecurityHealth.LdapSigningRequired) findings.Add(new { severity = "high", code = "LDAP_SIGNING", detail = "LDAP signing is not required." });
            if (!s.SecurityHealth.LdapChannelBinding) findings.Add(new { severity = "medium", code = "CHANNEL_BINDING", detail = "LDAP channel binding is not enforced." });
            if (!s.SecurityHealth.KerberosAes) findings.Add(new { severity = "medium", code = "KERBEROS_AES", detail = "AES Kerberos encryption types are not required." });
            if (duplicates.Count > 0) findings.Add(new { severity = "high", code = "DUPLICATE_SPN", detail = $"{duplicates.Count} duplicate SPN(s) detected." });
            return Results.Json(new
            {
                health = s.SecurityHealth,
                findings,
                duplicates,
                spns = AllSpns(s)
            });
        });

        app.MapPut("/api/security/health", (SecurityHealth body, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                s.SecurityHealth.LdapSigningRequired = body.LdapSigningRequired;
                s.SecurityHealth.LdapChannelBinding = body.LdapChannelBinding;
                s.SecurityHealth.KerberosAes = body.KerberosAes;
                s.SecurityHealth.NtlmV1Disabled = body.NtlmV1Disabled;
                s.SecurityHealth.SmbSigning = body.SmbSigning;
                s.SecurityHealth.CheckedAt = DateTimeOffset.UtcNow;
                DirectoryStore.AddAudit(s, "update", "security", "health", "Updated Kerberos/NTLM/LDAP health flags");
                return s.SecurityHealth;
            });
            return Results.Json(item);
        });

        app.MapGet("/api/operators", (DirectoryStore store) =>
        {
            var s = store.Snapshot();
            return Results.Json(new { items = s.Operators, currentOperatorId = s.Settings.CurrentOperatorId });
        });

        app.MapPost("/api/operators", (OperatorAccount body, DirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.Json(new { message = "Name is required" }, statusCode: 400);
            var item = store.Update(s =>
            {
                var created = new OperatorAccount
                {
                    Id = DirectoryStore.NewId("op"),
                    Name = body.Name,
                    Upn = body.Upn ?? "",
                    Role = string.IsNullOrWhiteSpace(body.Role) ? "helpdesk" : body.Role,
                    Enabled = body.Enabled,
                    Permissions = body.Permissions is { Count: > 0 } ? body.Permissions : DefaultPerms(body.Role)
                };
                s.Operators.Add(created);
                DirectoryStore.AddAudit(s, "create", "operator", created.Name, $"Created operator role {created.Role}");
                return created;
            });
            return Results.Json(item, statusCode: 201);
        });

        app.MapPut("/api/operators/{id}", (string id, OperatorAccount body, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var o = s.Operators.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                o.Name = body.Name ?? o.Name;
                o.Upn = body.Upn ?? o.Upn;
                o.Role = body.Role ?? o.Role;
                o.Enabled = body.Enabled;
                if (body.Permissions != null) o.Permissions = body.Permissions;
                DirectoryStore.AddAudit(s, "update", "operator", o.Name, "Updated operator");
                return o;
            });
            return Results.Json(item);
        });

        app.MapPost("/api/operators/assume/{id}", (string id, DirectoryStore store) =>
        {
            var item = store.Update(s =>
            {
                var o = s.Operators.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
                s.Settings.CurrentOperatorId = o.Id;
                DirectoryStore.AddAudit(s, "assume", "operator", o.Name, $"Assumed role {o.Role}", actor: o.Upn);
                return o;
            });
            return Results.Json(item);
        });

        app.MapGet("/api/laps-grants", (DirectoryStore store) =>
        {
            ExpireLaps(store);
            var s = store.Snapshot();
            var items = s.LapsGrants.Select(g =>
            {
                var op = s.Operators.FirstOrDefault(o => o.Id == g.OperatorId);
                var c = s.Computers.FirstOrDefault(x => x.Id == g.ComputerId);
                return new { g.Id, g.OperatorId, operatorName = op?.Name, g.ComputerId, computerName = c?.Name, g.Reason, g.ExpiresAt, g.Status };
            });
            return Results.Json(new { items, currentOperatorId = s.Settings.CurrentOperatorId });
        });

        app.MapPost("/api/laps-grants", (LapsJitGrant body, DirectoryStore store) =>
        {
            if (string.IsNullOrWhiteSpace(body.OperatorId) || string.IsNullOrWhiteSpace(body.ComputerId))
                return Results.Json(new { message = "operatorId and computerId are required" }, statusCode: 400);
            var item = store.Update(s =>
            {
                var created = new LapsJitGrant
                {
                    Id = DirectoryStore.NewId("lj"),
                    OperatorId = body.OperatorId,
                    ComputerId = body.ComputerId,
                    Reason = body.Reason ?? "",
                    ExpiresAt = body.ExpiresAt == default ? DateTimeOffset.UtcNow.AddHours(2) : body.ExpiresAt,
                    Status = "active"
                };
                s.LapsGrants.Insert(0, created);
                DirectoryStore.AddAudit(s, "jit-grant", "laps", body.ComputerId, $"JIT LAPS access for {body.OperatorId}");
                return created;
            });
            return Results.Json(item, statusCode: 201);
        });
    }

    public static void ExpireJit(DirectoryStore store)
    {
        store.Update(s =>
        {
            foreach (var g in s.JitGrants.Where(x => x.Status == "active" && x.ExpiresAt <= DateTimeOffset.UtcNow).ToList())
                RevokeJit(s, g, expired: true);
        });
    }

    private static void ExpireLaps(DirectoryStore store)
    {
        store.Update(s =>
        {
            foreach (var g in s.LapsGrants.Where(x => x.Status == "active" && x.ExpiresAt <= DateTimeOffset.UtcNow))
                g.Status = "expired";
        });
    }

    private static void RevokeJit(DirectoryState s, JitGrant g, bool expired = false)
    {
        var group = s.Groups.FirstOrDefault(x => x.Id == g.GroupId);
        var user = s.Users.FirstOrDefault(x => x.Id == g.UserId);
        if (group != null) group.Members.RemoveAll(m => m == g.UserId);
        if (user != null) user.Groups.RemoveAll(x => x == g.GroupId);
        g.Status = expired ? "expired" : "revoked";
        DirectoryStore.AddAudit(s, expired ? "jit-expire" : "jit-revoke", "group", group?.Name ?? g.GroupId, $"{user?.SamAccountName} removed from privileged group");
    }

    private static object EnrichJit(DirectoryState s, JitGrant j)
    {
        var u = s.Users.FirstOrDefault(x => x.Id == j.UserId);
        var g = s.Groups.FirstOrDefault(x => x.Id == j.GroupId);
        return new
        {
            j.Id, j.UserId, userName = u?.DisplayName, sam = u?.SamAccountName,
            j.GroupId, groupName = g?.Name, j.Reason, j.Ticket, j.Status, j.StartsAt, j.ExpiresAt, j.RequestedBy
        };
    }

    private static PasswordSettingsObject EffectivePso(DirectoryState s, DirectoryUser user)
    {
        var matches = s.PasswordPolicies
            .Where(p => p.IsDomainDefault || p.AppliesTo.Intersect(user.Groups).Any())
            .OrderBy(p => p.IsDomainDefault ? int.MaxValue : p.Precedence)
            .ToList();
        return matches.FirstOrDefault() ?? new PasswordSettingsObject { Name = "Fallback", MinLength = 8, Complexity = true };
    }

    private static List<SpnRecord> AllSpns(DirectoryState s)
    {
        if (s.Spns.Count > 0) return s.Spns;
        var list = new List<SpnRecord>();
        foreach (var c in s.Computers)
            foreach (var spn in c.ServicePrincipalNames)
                list.Add(new SpnRecord { Id = c.Id + spn, Principal = c.Name, PrincipalId = c.Id, Spn = spn, Kind = "computer" });
        return list;
    }

    private static List<string> DefaultPerms(string? role) => role switch
    {
        "domainAdmin" => new List<string> { "*" },
        "auditor" => new List<string> { "audit.read", "reports.read" },
        _ => new List<string> { "users.resetPassword", "users.unlock", "laps.view.jit" }
    };

    private static string RandomRecovery()
    {
        var rnd = Random.Shared;
        return string.Join("-", Enumerable.Range(0, 8).Select(_ => rnd.Next(100000, 999999).ToString()));
    }

    private static string Escape(string? v) => (v ?? "").Replace("'", "''");
    private static string ParentDn(string dn)
    {
        var i = dn.IndexOf(',');
        return i > 0 ? dn[(i + 1)..] : dn;
    }

    private static string GetString(this JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
