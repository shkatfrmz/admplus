using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Admplus.Api;
using Admplus.Api.Models;
using Admplus.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:3001");
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<AppLog>();
builder.Services.AddSingleton<DirectoryStore>();
builder.Services.AddSingleton<ActiveDirectoryClient>();
builder.Services.AddSingleton<AzureAdClient>();

var app = builder.Build();
var log = app.Services.GetRequiredService<AppLog>();
app.UseExceptionHandler(err => err.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    log.Write("error", "http", $"Unhandled {ctx.Request.Method} {ctx.Request.Path}", ex: ex);
    ctx.Response.StatusCode = 400;
    await ctx.Response.WriteAsJsonAsync(new { message = ex?.Message ?? "Error" });
}));
app.Use(async (ctx, next) =>
{
    var started = DateTimeOffset.UtcNow;
    ctx.Request.EnableBuffering();
    string? body = null;
    if (ctx.Request.ContentLength is > 0 and < 32_000 &&
        ctx.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
    {
        using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, false, 1024, true);
        body = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
    }
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        log.Write("error", "http", $"{ctx.Request.Method} {ctx.Request.Path} failed", body, ex);
        throw;
    }
    var ms = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
    var level = ctx.Response.StatusCode >= 500 ? "error" : ctx.Response.StatusCode >= 400 ? "warn" : "debug";
    log.Write(level, "http", $"{ctx.Request.Method} {ctx.Request.Path} -> {ctx.Response.StatusCode} ({ms:0}ms)",
        string.IsNullOrWhiteSpace(body) ? null : body);
});
app.UseCors();
app.MapFeatures();

object Page<T>(IEnumerable<T> source, string? q, string? type, int page, int pageSize, Func<T, string>? typeOf = null, Func<T, string>? searchText = null)
{
    var items = source;
    if (!string.IsNullOrWhiteSpace(q))
    {
        var needle = q.Trim();
        items = searchText != null
            ? items.Where(x => searchText(x).Contains(needle, StringComparison.OrdinalIgnoreCase))
            : items.Where(x => JsonSerializer.Serialize(x).Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
    if (!string.IsNullOrWhiteSpace(type) && typeOf != null)
        items = items.Where(x => typeOf(x) == type);
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 1000);
    var list = items.ToList();
    return new { items = list.Skip((page - 1) * pageSize).Take(pageSize), total = list.Count, page, pageSize };
}

string UserSearch(DirectoryUser u) => string.Join('\n', u.SamAccountName, u.DisplayName, u.GivenName, u.Surname, u.UserPrincipalName, u.Email, u.Department, u.Title, u.Office, u.Phone, u.Type, u.Dn);
string ComputerSearch(DirectoryComputer c) => string.Join('\n', c.Name, c.DnsHostName, c.Os, c.OsVersion, c.Type, c.Description, c.ManagedBy, c.Dn);
string GroupSearch(DirectoryGroup g) => string.Join('\n', g.Name, g.SamAccountName, g.Type, g.Scope, g.Description, g.Mail, g.Dn);
string GpoSearch(DirectoryGpo g) => string.Join('\n', g.Name, g.Status, g.Description);
string ShareSearch(DirectoryShare s) => string.Join('\n', s.Name, s.Path, s.Server, s.Description);

string Mask(string? secret) => string.IsNullOrEmpty(secret) ? "" : "********";

app.MapGet("/api/health", () => Results.Json(new { ok = true, name = "Admplus", stack = "ASP.NET Core 8 / DirectoryServices.Protocols / MSAL", time = DateTimeOffset.UtcNow }));

app.MapGet("/api/dashboard", (DirectoryStore store) =>
{
    var s = store.Snapshot();
    var enabledUsers = s.Users.Count(u => u.Enabled);
    return Results.Json(new
    {
        counts = new
        {
            users = s.Users.Count,
            enabledUsers,
            disabledUsers = s.Users.Count - enabledUsers,
            lockedUsers = s.Users.Count(u => u.Locked),
            computers = s.Computers.Count,
            computersOnline = s.Computers.Count(c => c.Enabled),
            groups = s.Groups.Count,
            gpos = s.Gpos.Count,
            gpoEnabled = s.Gpos.Count(g => g.Status == "enabled"),
            shares = s.Shares.Count,
            laps = s.Laps.Count,
            lapsExpiring = s.Laps.Count(l => l.Expiration - DateTimeOffset.UtcNow < TimeSpan.FromDays(14)),
            audit = s.Audit.Count
        },
        connections = new
        {
            domainController = new
            {
                connected = s.Settings.DomainController.Connected,
                host = string.IsNullOrEmpty(s.Settings.DomainController.Host) ? (string?)null : s.Settings.DomainController.Host,
                domain = string.IsNullOrEmpty(s.Settings.DomainController.Domain) ? (string?)null : s.Settings.DomainController.Domain,
                lastTest = s.Settings.DomainController.LastTest
            },
            azureAd = new
            {
                connected = s.Settings.AzureAd.Connected,
                tenantId = string.IsNullOrEmpty(s.Settings.AzureAd.TenantId) ? (string?)null : s.Settings.AzureAd.TenantId,
                lastTest = s.Settings.AzureAd.LastTest
            }
        },
        recentAudit = s.Audit.Take(8),
        userTypes = s.Users.GroupBy(u => u.Type).ToDictionary(g => g.Key, g => g.Count()),
        groupTypes = s.Groups.GroupBy(g => g.Type).ToDictionary(g => g.Key, g => g.Count())
    });
});

app.MapGet("/api/settings", (DirectoryStore store) =>
{
    var s = store.Snapshot().Settings;
    return Results.Json(new
    {
        domainController = new
        {
            s.DomainController.Host,
            s.DomainController.Port,
            s.DomainController.UseSsl,
            s.DomainController.BindDn,
            password = Mask(s.DomainController.Password),
            s.DomainController.BaseDn,
            s.DomainController.Domain,
            s.DomainController.SearchPageSize,
            s.DomainController.Connected,
            s.DomainController.LastTest,
            s.DomainController.LastError
        },
        azureAd = new
        {
            s.AzureAd.TenantId,
            s.AzureAd.ClientId,
            clientSecret = Mask(s.AzureAd.ClientSecret),
            s.AzureAd.Connected,
            s.AzureAd.LastTest,
            s.AzureAd.LastError
        }
    });
});

app.MapPut("/api/settings/domain-controller", (DomainControllerSettings body, DirectoryStore store, AppLog appLog) =>
{
    var dc = store.Update(s =>
    {
        var cur = s.Settings.DomainController;
        DirectoryConnection.Apply(cur, body);
        DirectoryStore.AddAudit(s, "update", "settings", "domain-controller", "Updated domain controller connection settings");
        return cur;
    });
    appLog.Write("info", "settings", "Saved domain controller settings", DirectoryConnection.Describe(dc));
    return Results.Json(new
    {
        dc.Host, dc.Port, dc.UseSsl, dc.BindDn, password = Mask(dc.Password),
        dc.BaseDn, dc.Domain, dc.SearchPageSize, dc.Connected, dc.LastTest, dc.LastError
    });
});

app.MapPut("/api/settings/azure-ad", (AzureAdSettings body, DirectoryStore store) =>
{
    var az = store.Update(s =>
    {
        var cur = s.Settings.AzureAd;
        cur.TenantId = body.TenantId ?? cur.TenantId;
        cur.ClientId = body.ClientId ?? cur.ClientId;
        if (!string.IsNullOrEmpty(body.ClientSecret) && body.ClientSecret != "********")
            cur.ClientSecret = body.ClientSecret;
        DirectoryStore.AddAudit(s, "update", "settings", "azure-ad", "Updated Azure AD connection settings");
        return cur;
    });
    return Results.Json(new
    {
        az.TenantId, az.ClientId, clientSecret = Mask(az.ClientSecret),
        az.Connected, az.LastTest, az.LastError
    });
});

app.MapPost("/api/settings/domain-controller/test", async (DomainControllerSettings? body, DirectoryStore store, ActiveDirectoryClient ad, AppLog appLog) =>
{
    var dc = store.Update(s =>
    {
        if (DirectoryConnection.HasConnectionFields(body))
            DirectoryConnection.Apply(s.Settings.DomainController, body!);
        else
            DirectoryConnection.Normalize(s.Settings.DomainController);
        return s.Settings.DomainController;
    });
    appLog.Write("info", "ldap", "Testing domain controller bind", DirectoryConnection.Describe(dc));
    try
    {
        await ad.TestConnectionAsync(dc);
        store.Update(st =>
        {
            st.Settings.DomainController.Connected = true;
            st.Settings.DomainController.LastTest = DateTimeOffset.UtcNow;
            st.Settings.DomainController.LastError = null;
            DirectoryStore.AddAudit(st, "connect", "settings", "domain-controller", $"Bound to {st.Settings.DomainController.Host} via System.DirectoryServices.Protocols");
        });
        var last = store.Snapshot().Settings.DomainController.LastTest;
        appLog.Write("info", "ldap", $"LDAP bind succeeded to {dc.Host}:{dc.Port}");
        return Results.Json(new { ok = true, message = $"Bound to {dc.Host} using LDAP (DirectoryServices.Protocols)", lastTest = last, host = dc.Host, bindDn = dc.BindDn, baseDn = dc.BaseDn, domain = dc.Domain });
    }
    catch (Exception ex)
    {
        appLog.Write("error", "ldap", $"LDAP bind failed to {dc.Host}:{dc.Port} as {dc.BindDn}", DirectoryConnection.Describe(dc), ex);
        store.Update(st =>
        {
            st.Settings.DomainController.Connected = false;
            st.Settings.DomainController.LastTest = DateTimeOffset.UtcNow;
            st.Settings.DomainController.LastError = ex.Message;
            DirectoryStore.AddAudit(st, "connect", "settings", "domain-controller", ex.Message, "failure");
        });
        return Results.Json(new { ok = false, message = ex.Message, lastTest = DateTimeOffset.UtcNow, host = dc.Host, bindDn = dc.BindDn, baseDn = dc.BaseDn, domain = dc.Domain }, statusCode: 400);
    }
});

app.MapGet("/api/logs", (AppLog appLog, string? q, string? level, int take = 200) =>
{
    var items = appLog.Query(q, level, take);
    return Results.Json(new { items, total = items.Count, file = appLog.FilePath });
});

app.MapGet("/api/logs/file", (AppLog appLog) =>
{
    if (!File.Exists(appLog.FilePath)) return Results.Text("", "text/plain");
    return Results.Text(File.ReadAllText(appLog.FilePath), "text/plain");
});

app.MapPost("/api/settings/azure-ad/test", async (AzureAdSettings? body, DirectoryStore store, AzureAdClient azure, AppLog appLog) =>
{
    if (body != null)
    {
        store.Update(st =>
        {
            var cur = st.Settings.AzureAd;
            if (!string.IsNullOrWhiteSpace(body.TenantId)) cur.TenantId = body.TenantId.Trim();
            if (!string.IsNullOrWhiteSpace(body.ClientId)) cur.ClientId = body.ClientId.Trim();
            if (!string.IsNullOrEmpty(body.ClientSecret) && body.ClientSecret != "********")
                cur.ClientSecret = body.ClientSecret;
        });
    }
    var s = store.Snapshot();
    appLog.Write("info", "azure", "Testing Azure AD client credentials", new { s.Settings.AzureAd.TenantId, s.Settings.AzureAd.ClientId });
    try
    {
        await azure.TestConnectionAsync(s.Settings.AzureAd);
        store.Update(st =>
        {
            st.Settings.AzureAd.Connected = true;
            st.Settings.AzureAd.LastTest = DateTimeOffset.UtcNow;
            st.Settings.AzureAd.LastError = null;
            DirectoryStore.AddAudit(st, "connect", "settings", "azure-ad", "Azure AD application credentials accepted (MSAL client credentials)");
        });
        appLog.Write("info", "azure", "Azure AD token acquired");
        return Results.Json(new { ok = true, message = "Azure AD application credentials accepted via Microsoft.Identity.Client", lastTest = DateTimeOffset.UtcNow });
    }
    catch (Exception ex)
    {
        appLog.Write("error", "azure", "Azure AD test failed", new { s.Settings.AzureAd.TenantId, s.Settings.AzureAd.ClientId }, ex);
        store.Update(st =>
        {
            st.Settings.AzureAd.Connected = false;
            st.Settings.AzureAd.LastTest = DateTimeOffset.UtcNow;
            st.Settings.AzureAd.LastError = ex.Message;
            DirectoryStore.AddAudit(st, "connect", "settings", "azure-ad", ex.Message, "failure");
        });
        return Results.Json(new { ok = false, message = ex.Message, lastTest = DateTimeOffset.UtcNow }, statusCode: 400);
    }
});

app.MapPost("/api/settings/sync", (DirectoryStore store, ActiveDirectoryClient ad, AppLog appLog) =>
{
    var s = store.Snapshot();
    if (!s.Settings.DomainController.Connected || string.IsNullOrWhiteSpace(s.Settings.DomainController.Host))
        return Results.Json(new { ok = false, message = "Domain controller is not connected" }, statusCode: 400);
    var dc = s.Settings.DomainController;
    try
    {
        var liveUsers = ad.SearchUsers(dc);
        var liveComputers = ad.SearchComputers(dc);
        var liveGroups = ad.SearchGroups(dc);
        var liveOus = ad.SearchOus(dc);
        var liveGpos = ad.SearchGpos(dc);

        var result = new
        {
            users = new { total = liveUsers.Count, created = 0, updated = 0 },
            computers = new { total = liveComputers.Count, created = 0, updated = 0 },
            groups = new { total = liveGroups.Count, created = 0, updated = 0 },
            ous = new { total = liveOus.Count, created = 0, updated = 0 },
            gpos = new { total = liveGpos.Count, created = 0, updated = 0 }
        };

        int uC = 0, uU = 0, cC = 0, cU = 0, gC = 0, gU = 0, oC = 0, oU = 0, pC = 0, pU = 0;

        store.Update(st =>
        {
            foreach (var u in liveUsers)
            {
                var existing = st.Users.FirstOrDefault(x => x.SamAccountName.Equals(u.SamAccountName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.DisplayName = u.DisplayName;
                    existing.GivenName = u.GivenName;
                    existing.Surname = u.Surname;
                    existing.UserPrincipalName = u.UserPrincipalName;
                    existing.Email = u.Email;
                    existing.Type = u.Type;
                    existing.Enabled = u.Enabled;
                    existing.Locked = u.Locked;
                    existing.PasswordNeverExpires = u.PasswordNeverExpires;
                    existing.Department = u.Department;
                    existing.Title = u.Title;
                    existing.Office = u.Office;
                    existing.Phone = u.Phone;
                    existing.Manager = u.Manager;
                    existing.Ou = u.Ou;
                    existing.Dn = u.Dn;
                    existing.LastLogon = u.LastLogon;
                    existing.Source = "ad-live";
                    uU++;
                }
                else
                {
                    u.Id = DirectoryStore.NewId("u");
                    st.Users.Add(u);
                    uC++;
                }
            }

            foreach (var c in liveComputers)
            {
                var existing = st.Computers.FirstOrDefault(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.DnsHostName = c.DnsHostName;
                    existing.Os = c.Os;
                    existing.OsVersion = c.OsVersion;
                    existing.Type = c.Type;
                    existing.Enabled = c.Enabled;
                    existing.Ou = c.Ou;
                    existing.Dn = c.Dn;
                    existing.Description = c.Description;
                    existing.ManagedBy = c.ManagedBy;
                    existing.ServicePrincipalNames = c.ServicePrincipalNames;
                    existing.LastLogon = c.LastLogon;
                    existing.Source = "ad-live";
                    cU++;
                }
                else
                {
                    c.Id = DirectoryStore.NewId("c");
                    st.Computers.Add(c);
                    cC++;
                }
            }

            foreach (var g in liveGroups)
            {
                var existing = st.Groups.FirstOrDefault(x => x.SamAccountName.Equals(g.SamAccountName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = g.Name;
                    existing.Type = g.Type;
                    existing.Scope = g.Scope;
                    existing.Description = g.Description;
                    existing.Ou = g.Ou;
                    existing.Dn = g.Dn;
                    existing.Mail = g.Mail;
                    existing.Members = g.Members;
                    existing.Source = "ad-live";
                    gU++;
                }
                else
                {
                    g.Id = DirectoryStore.NewId("g");
                    st.Groups.Add(g);
                    gC++;
                }
            }

            var dnToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in st.Users) if (!string.IsNullOrEmpty(u.Dn)) dnToId[u.Dn] = u.Id;
            foreach (var c in st.Computers) if (!string.IsNullOrEmpty(c.Dn)) dnToId[c.Dn] = c.Id;
            foreach (var g in st.Groups) if (!string.IsNullOrEmpty(g.Dn)) dnToId[g.Dn] = g.Id;

            var userById = st.Users.ToDictionary(u => u.Id, u => u);
            foreach (var g in liveGroups)
            {
                var node = st.Groups.FirstOrDefault(x => x.SamAccountName.Equals(g.SamAccountName, StringComparison.OrdinalIgnoreCase));
                if (node is null) continue;
                node.Members = g.Members
                    .Select(m => dnToId.TryGetValue(m, out var id) ? id : null)
                    .Where(id => id != null)
                    .Select(id => id!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            foreach (var u in st.Users)
                if (u.Source == "ad-live") u.Groups = new List<string>();
            foreach (var g in st.Groups)
                foreach (var mid in g.Members)
                    if (userById.TryGetValue(mid, out var member) && !member.Groups.Contains(g.Id))
                        member.Groups.Add(g.Id);

            foreach (var o in liveOus)
            {
                var existing = st.Ous.FirstOrDefault(x => x.Dn.Equals(o.Dn, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = o.Name;
                    existing.Description = o.Description;
                    existing.Source = "ad-live";
                    oU++;
                }
                else
                {
                    st.Ous.Add(o);
                    oC++;
                }
            }

            var ouByDn = st.Ous
                .GroupBy(x => x.Dn, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var o in liveOus)
            {
                if (!ouByDn.TryGetValue(o.Dn, out var node)) continue;
                var parentDn = ActiveDirectoryClient.ParentDnOf(o.Dn);
                node.ParentId = parentDn.Length > 0 && ouByDn.TryGetValue(parentDn, out var parent) ? parent.Id : "";
            }
            appLog.Write("info", "sync", $"OU tree: {st.Ous.Count} nodes, {st.Ous.Count(x => !string.IsNullOrEmpty(x.ParentId))} nested (have a parent), {st.Ous.Count(x => string.IsNullOrEmpty(x.ParentId))} at root");

            foreach (var p in liveGpos)
            {
                var existing = st.Gpos.FirstOrDefault(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Name = p.Name;
                    existing.Status = p.Status;
                    existing.Description = p.Description;
                    existing.LinkedOus = p.LinkedOus;
                    existing.Enforced = p.Enforced;
                    existing.Settings = p.Settings;
                    existing.Source = "ad-live";
                    pU++;
                }
                else
                {
                    st.Gpos.Add(p);
                    pC++;
                }
            }

            DirectoryStore.AddAudit(st, "sync", "directory", "full",
                $"LDAP sync: users {liveUsers.Count} ({uC} new/{uU} upd), computers {liveComputers.Count} ({cC}/{cU}), " +
                $"groups {liveGroups.Count} ({gC}/{gU}), OUs {liveOus.Count} ({oC}/{oU}), GPOs {liveGpos.Count} ({pC}/{pU})");
        });

        appLog.Write("info", "sync", $"Directory sync complete: users={liveUsers.Count} computers={liveComputers.Count} groups={liveGroups.Count} ous={liveOus.Count} gpos={liveGpos.Count}");
        result = new
        {
            users = new { total = liveUsers.Count, created = uC, updated = uU },
            computers = new { total = liveComputers.Count, created = cC, updated = cU },
            groups = new { total = liveGroups.Count, created = gC, updated = gU },
            ous = new { total = liveOus.Count, created = oC, updated = oU },
            gpos = new { total = liveGpos.Count, created = pC, updated = pU }
        };
        return Results.Json(new { ok = true, result });
    }
    catch (Exception ex)
    {
        appLog.Write("error", "sync", "Directory sync failed", null, ex);
        return Results.Json(new { ok = false, message = ex.Message }, statusCode: 400);
    }
});

app.MapGet("/api/users", (DirectoryStore store, string? q, string? type, int page = 1, int pageSize = 50) =>
    Results.Json(Page(store.Snapshot().Users, q, type, page, pageSize, u => u.Type, UserSearch)));

app.MapGet("/api/users/{id}", (string id, DirectoryStore store) =>
{
    var s = store.Snapshot();
    var item = s.Users.FirstOrDefault(u => u.Id == id);
    if (item is null) return Results.Json(new { message = "User not found" }, statusCode: 404);
    var groups = s.Groups.Where(g => item.Groups.Contains(g.Id)).ToList();
    return Results.Json(new
    {
        item.Id, samAccountName = item.SamAccountName, displayName = item.DisplayName, givenName = item.GivenName,
        surname = item.Surname, userPrincipalName = item.UserPrincipalName, email = item.Email, type = item.Type,
        enabled = item.Enabled, locked = item.Locked, passwordNeverExpires = item.PasswordNeverExpires,
        cannotChangePassword = item.CannotChangePassword, mustChangePassword = item.MustChangePassword,
        department = item.Department, title = item.Title, office = item.Office, phone = item.Phone,
        manager = item.Manager, ou = item.Ou, groups = item.Groups, lastLogon = item.LastLogon,
        created = item.Created, source = item.Source, groupDetails = groups
    });
});

app.MapPost("/api/users", (DirectoryUser body, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    if (string.IsNullOrWhiteSpace(body.SamAccountName) || string.IsNullOrWhiteSpace(body.DisplayName))
        return Results.Json(new { message = "Missing required fields: samAccountName, displayName" }, statusCode: 400);
    try
    {
        var item = store.Update(s =>
        {
            if (s.Users.Any(u => u.SamAccountName.Equals(body.SamAccountName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("sAMAccountName already exists");
            var domain = string.IsNullOrWhiteSpace(s.Settings.DomainController.Domain) ? "contoso.local" : s.Settings.DomainController.Domain;
            var created = new DirectoryUser
            {
                Id = DirectoryStore.NewId("u"),
                SamAccountName = body.SamAccountName,
                DisplayName = body.DisplayName,
                GivenName = body.GivenName ?? "",
                Surname = body.Surname ?? "",
                UserPrincipalName = string.IsNullOrWhiteSpace(body.UserPrincipalName) ? $"{body.SamAccountName}@{domain}" : body.UserPrincipalName,
                Email = body.Email ?? "",
                Type = string.IsNullOrWhiteSpace(body.Type) ? "user" : body.Type,
                Enabled = body.Enabled,
                PasswordNeverExpires = body.PasswordNeverExpires,
                CannotChangePassword = body.CannotChangePassword,
                MustChangePassword = body.MustChangePassword,
                Department = body.Department ?? "",
                Title = body.Title ?? "",
                Office = body.Office ?? "",
                Phone = body.Phone ?? "",
                Manager = body.Manager ?? "",
                Ou = string.IsNullOrWhiteSpace(body.Ou) ? "CN=Users,DC=contoso,DC=local" : body.Ou,
                Groups = body.Groups is { Count: > 0 } ? body.Groups : new List<string> { "g-domain-users" },
                Created = DateTimeOffset.UtcNow
            };
            if (s.Settings.DomainController.Connected)
            {
                try { ad.CreateUser(s.Settings.DomainController, created, ActiveDirectoryClient.RandomPassword()); }
                catch (Exception ex) { created.Source = "ad-pending"; DirectoryStore.AddAudit(s, "create", "user", created.SamAccountName, $"LDAP create failed, stored locally: {ex.Message}", "failure"); }
            }
            s.Users.Add(created);
            foreach (var gid in created.Groups)
            {
                var g = s.Groups.FirstOrDefault(x => x.Id == gid);
                if (g != null && !g.Members.Contains(created.Id)) g.Members.Add(created.Id);
            }
            DirectoryStore.AddAudit(s, "create", "user", created.SamAccountName, $"Created {created.Type} {created.DisplayName}");
            return created;
        });
        return Results.Json(item, statusCode: 201);
    }
    catch (InvalidOperationException ex) { return Results.Json(new { message = ex.Message }, statusCode: 409); }
});

app.MapPut("/api/users/{id}", (string id, DirectoryUser body, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var u = s.Users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        u.DisplayName = body.DisplayName ?? u.DisplayName;
        u.GivenName = body.GivenName ?? u.GivenName;
        u.Surname = body.Surname ?? u.Surname;
        u.UserPrincipalName = body.UserPrincipalName ?? u.UserPrincipalName;
        u.Email = body.Email ?? u.Email;
        u.Type = body.Type ?? u.Type;
        u.Enabled = body.Enabled;
        u.Locked = body.Locked;
        u.PasswordNeverExpires = body.PasswordNeverExpires;
        u.CannotChangePassword = body.CannotChangePassword;
        u.MustChangePassword = body.MustChangePassword;
        u.Department = body.Department ?? u.Department;
        u.Title = body.Title ?? u.Title;
        u.Office = body.Office ?? u.Office;
        u.Phone = body.Phone ?? u.Phone;
        u.Manager = body.Manager ?? u.Manager;
        u.Ou = body.Ou ?? u.Ou;
        if (body.Groups != null)
        {
            foreach (var g in s.Groups) g.Members.RemoveAll(m => m == u.Id);
            u.Groups = body.Groups;
            foreach (var gid in u.Groups)
            {
                var g = s.Groups.FirstOrDefault(x => x.Id == gid);
                if (g != null && !g.Members.Contains(u.Id)) g.Members.Add(u.Id);
            }
        }
        DirectoryStore.AddAudit(s, "update", "user", u.SamAccountName, $"Updated user {u.DisplayName}");
        return u;
    });
    return Results.Json(item);
});

void UserFlag(string id, DirectoryStore store, ActiveDirectoryClient ad, Action<DirectoryUser, DirectoryState> apply, string action, string detail)
{
    store.Update(s =>
    {
        var u = s.Users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("User not found");
        apply(u, s);
        if (s.Settings.DomainController.Connected && LooksLikeDn(DnOf(u.Dn, u.Ou)))
        {
            try
            {
                if (action == "enable") ad.SetUserEnabled(s.Settings.DomainController, DnOf(u.Dn, u.Ou), true);
                if (action == "disable") ad.SetUserEnabled(s.Settings.DomainController, DnOf(u.Dn, u.Ou), false);
                if (action == "unlock") ad.UnlockUser(s.Settings.DomainController, DnOf(u.Dn, u.Ou));
                if (action == "reset-password") ad.ResetPassword(s.Settings.DomainController, DnOf(u.Dn, u.Ou), ActiveDirectoryClient.RandomPassword());
            }
            catch { /* local store remains source of truth when DC op fails */ }
        }
        DirectoryStore.AddAudit(s, action, "user", u.SamAccountName, detail);
    });
}

bool LooksLikeDn(string? v) => !string.IsNullOrEmpty(v) && v.Contains("DC=", StringComparison.OrdinalIgnoreCase) && v.Contains("CN=", StringComparison.OrdinalIgnoreCase);
string DnOf(string? dn, string? ou) => string.IsNullOrEmpty(dn) ? (ou ?? "") : dn;

app.MapPost("/api/users/{id}/enable", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    UserFlag(id, store, ad, (u, _) => u.Enabled = true, "enable", "Account enabled");
    return Results.Json(store.Snapshot().Users.First(u => u.Id == id));
});
app.MapPost("/api/users/{id}/disable", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    UserFlag(id, store, ad, (u, _) => u.Enabled = false, "disable", "Account disabled");
    return Results.Json(store.Snapshot().Users.First(u => u.Id == id));
});
app.MapPost("/api/users/{id}/unlock", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    UserFlag(id, store, ad, (u, _) => u.Locked = false, "unlock", "Account unlocked");
    return Results.Json(store.Snapshot().Users.First(u => u.Id == id));
});
app.MapPost("/api/users/{id}/reset-password", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    UserFlag(id, store, ad, (u, _) => u.MustChangePassword = true, "reset-password", "Password reset; must change at next logon");
    return Results.Json(new { ok = true, mustChangePassword = true });
});
app.MapDelete("/api/users/{id}", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    store.Update(s =>
    {
        var u = s.Users.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        if (s.Settings.DomainController.Connected && LooksLikeDn(DnOf(u.Dn, u.Ou)))
        {
            try { ad.DeleteObject(s.Settings.DomainController, DnOf(u.Dn, u.Ou)); } catch { }
        }
        DirectoryStore.Recycle(s, "user", u.Id, u.DisplayName, u.Ou, u);
        s.Users.RemoveAll(x => x.Id == id);
        foreach (var g in s.Groups) g.Members.RemoveAll(m => m == id);
        DirectoryStore.AddAudit(s, "delete", "user", u.SamAccountName, $"Deleted user {u.DisplayName} (recycle bin)");
    });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/computers", (DirectoryStore store, string? q, string? type, int page = 1, int pageSize = 50) =>
    Results.Json(Page(store.Snapshot().Computers, q, type, page, pageSize, c => c.Type, ComputerSearch)));

app.MapGet("/api/computers/{id}", (string id, DirectoryStore store) =>
{
    var s = store.Snapshot();
    var item = s.Computers.FirstOrDefault(c => c.Id == id);
    if (item is null) return Results.Json(new { message = "Computer not found" }, statusCode: 404);
    var laps = s.Laps.FirstOrDefault(l => l.ComputerId == item.Id);
    return Results.Json(new
    {
        item.Id, name = item.Name, dnsHostName = item.DnsHostName, os = item.Os, osVersion = item.OsVersion,
        type = item.Type, enabled = item.Enabled, ou = item.Ou, description = item.Description,
        ipAddress = item.IpAddress, lastLogon = item.LastLogon, created = item.Created,
        managedBy = item.ManagedBy, source = item.Source,
        laps = laps == null ? null : new { laps.Id, expiration = laps.Expiration, lastRotated = laps.LastRotated }
    });
});

app.MapPost("/api/computers", (DirectoryComputer body, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.Json(new { message = "Missing required fields: name" }, statusCode: 400);
    var item = store.Update(s =>
    {
        var domain = string.IsNullOrWhiteSpace(s.Settings.DomainController.Domain) ? "contoso.local" : s.Settings.DomainController.Domain;
        var created = new DirectoryComputer
        {
            Id = DirectoryStore.NewId("c"),
            Name = body.Name,
            DnsHostName = string.IsNullOrWhiteSpace(body.DnsHostName) ? $"{body.Name.ToLowerInvariant()}.{domain}" : body.DnsHostName,
            Os = string.IsNullOrWhiteSpace(body.Os) ? "Windows 11 Enterprise" : body.Os,
            OsVersion = body.OsVersion ?? "",
            Type = string.IsNullOrWhiteSpace(body.Type) ? "workstation" : body.Type,
            Enabled = body.Enabled,
            Ou = string.IsNullOrWhiteSpace(body.Ou) ? "OU=Computers,DC=contoso,DC=local" : body.Ou,
            Description = body.Description ?? "",
            IpAddress = body.IpAddress ?? "",
            ManagedBy = body.ManagedBy ?? "",
            Created = DateTimeOffset.UtcNow
        };
        if (s.Settings.DomainController.Connected)
        {
            try { ad.CreateComputer(s.Settings.DomainController, created); } catch { }
        }
        s.Computers.Add(created);
        DirectoryStore.AddAudit(s, "create", "computer", created.Name, $"Created computer {created.Name}");
        return created;
    });
    return Results.Json(item, statusCode: 201);
});

app.MapPut("/api/computers/{id}", (string id, DirectoryComputer body, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var c = s.Computers.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        c.Name = body.Name ?? c.Name;
        c.DnsHostName = body.DnsHostName ?? c.DnsHostName;
        c.Os = body.Os ?? c.Os;
        c.OsVersion = body.OsVersion ?? c.OsVersion;
        c.Type = body.Type ?? c.Type;
        c.Enabled = body.Enabled;
        c.Ou = body.Ou ?? c.Ou;
        c.Description = body.Description ?? c.Description;
        c.IpAddress = body.IpAddress ?? c.IpAddress;
        c.ManagedBy = body.ManagedBy ?? c.ManagedBy;
        DirectoryStore.AddAudit(s, "update", "computer", c.Name, $"Updated computer {c.Name}");
        return c;
    });
    return Results.Json(item);
});

app.MapPost("/api/computers/{id}/enable", (string id, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var c = s.Computers.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        c.Enabled = true;
        DirectoryStore.AddAudit(s, "enable", "computer", c.Name, "Computer enabled");
        return c;
    });
    return Results.Json(item);
});
app.MapPost("/api/computers/{id}/disable", (string id, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var c = s.Computers.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        c.Enabled = false;
        DirectoryStore.AddAudit(s, "disable", "computer", c.Name, "Computer disabled");
        return c;
    });
    return Results.Json(item);
});
app.MapDelete("/api/computers/{id}", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    store.Update(s =>
    {
        var c = s.Computers.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        if (s.Settings.DomainController.Connected && LooksLikeDn(DnOf(c.Dn, c.Ou)))
        {
            try { ad.DeleteObject(s.Settings.DomainController, DnOf(c.Dn, c.Ou)); } catch { }
        }
        DirectoryStore.Recycle(s, "computer", c.Id, c.Name, c.Ou, c);
        s.Computers.RemoveAll(x => x.Id == id);
        s.Laps.RemoveAll(l => l.ComputerId == id);
        DirectoryStore.AddAudit(s, "delete", "computer", c.Name, $"Deleted computer {c.Name} (recycle bin)");
    });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/groups", (DirectoryStore store, string? q, string? type, int page = 1, int pageSize = 50) =>
    Results.Json(Page(store.Snapshot().Groups, q, type, page, pageSize, g => g.Type, GroupSearch)));

app.MapGet("/api/groups/{id}", (string id, DirectoryStore store) =>
{
    var s = store.Snapshot();
    var item = s.Groups.FirstOrDefault(g => g.Id == id);
    if (item is null) return Results.Json(new { message = "Group not found" }, statusCode: 404);
    var members = item.Members.Select(mid =>
    {
        var u = s.Users.FirstOrDefault(x => x.Id == mid);
        if (u != null) return new { id = u.Id, name = u.DisplayName, kind = "user", extra = u.SamAccountName };
        var c = s.Computers.FirstOrDefault(x => x.Id == mid);
        if (c != null) return new { id = c.Id, name = c.Name, kind = "computer", extra = c.DnsHostName };
        var g = s.Groups.FirstOrDefault(x => x.Id == mid);
        if (g != null) return new { id = g.Id, name = g.Name, kind = "group", extra = g.SamAccountName };
        return new { id = mid, name = mid, kind = "unknown", extra = "" };
    });
    return Results.Json(new
    {
        item.Id, name = item.Name, samAccountName = item.SamAccountName, type = item.Type, scope = item.Scope,
        description = item.Description, ou = item.Ou, members = item.Members, memberOf = item.MemberOf,
        mail = item.Mail, created = item.Created, source = item.Source, memberDetails = members
    });
});

app.MapPost("/api/groups", (DirectoryGroup body, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.Json(new { message = "Missing required fields: name" }, statusCode: 400);
    var item = store.Update(s =>
    {
        var created = new DirectoryGroup
        {
            Id = DirectoryStore.NewId("g"),
            Name = body.Name,
            SamAccountName = string.IsNullOrWhiteSpace(body.SamAccountName) ? body.Name : body.SamAccountName,
            Type = string.IsNullOrWhiteSpace(body.Type) ? "security" : body.Type,
            Scope = string.IsNullOrWhiteSpace(body.Scope) ? "global" : body.Scope,
            Description = body.Description ?? "",
            Ou = string.IsNullOrWhiteSpace(body.Ou) ? "OU=Groups,DC=contoso,DC=local" : body.Ou,
            Members = body.Members ?? new(),
            MemberOf = body.MemberOf ?? new(),
            Mail = body.Mail ?? "",
            Created = DateTimeOffset.UtcNow
        };
        if (s.Settings.DomainController.Connected)
        {
            try { ad.CreateGroup(s.Settings.DomainController, created); } catch { }
        }
        s.Groups.Add(created);
        DirectoryStore.AddAudit(s, "create", "group", created.Name, $"Created {created.Scope} {created.Type} group");
        return created;
    });
    return Results.Json(item, statusCode: 201);
});

app.MapPut("/api/groups/{id}", (string id, DirectoryGroup body, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var g = s.Groups.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        g.Name = body.Name ?? g.Name;
        g.SamAccountName = body.SamAccountName ?? g.SamAccountName;
        g.Type = body.Type ?? g.Type;
        g.Scope = body.Scope ?? g.Scope;
        g.Description = body.Description ?? g.Description;
        g.Ou = body.Ou ?? g.Ou;
        g.Mail = body.Mail ?? g.Mail;
        if (body.Members != null) g.Members = body.Members;
        if (body.MemberOf != null) g.MemberOf = body.MemberOf;
        DirectoryStore.AddAudit(s, "update", "group", g.Name, $"Updated group {g.Name}");
        return g;
    });
    return Results.Json(item);
});

app.MapPost("/api/groups/{id}/members", (string id, JsonElement body, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    var memberId = body.TryGetProperty("memberId", out var p) ? p.GetString() : null;
    if (string.IsNullOrEmpty(memberId)) return Results.Json(new { message = "memberId is required" }, statusCode: 400);
    var item = store.Update(s =>
    {
        var g = s.Groups.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        if (!g.Members.Contains(memberId)) g.Members.Add(memberId);
        var user = s.Users.FirstOrDefault(u => u.Id == memberId);
        if (user != null && !user.Groups.Contains(g.Id)) user.Groups.Add(g.Id);
        if (s.Settings.DomainController.Connected && LooksLikeDn(DnOf(g.Dn, g.Ou)) && user != null && LooksLikeDn(DnOf(user.Dn, user.Ou)))
        {
            try { ad.AddGroupMember(s.Settings.DomainController, DnOf(g.Dn, g.Ou), DnOf(user.Dn, user.Ou)); } catch { }
        }
        DirectoryStore.AddAudit(s, "add-member", "group", g.Name, $"Added member {memberId}");
        return g;
    });
    return Results.Json(item);
});

app.MapDelete("/api/groups/{id}/members/{memberId}", (string id, string memberId, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    var item = store.Update(s =>
    {
        var g = s.Groups.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        g.Members.RemoveAll(m => m == memberId);
        var user = s.Users.FirstOrDefault(u => u.Id == memberId);
        if (user != null) user.Groups.RemoveAll(x => x == g.Id);
        if (s.Settings.DomainController.Connected && LooksLikeDn(DnOf(g.Dn, g.Ou)) && user != null && LooksLikeDn(DnOf(user.Dn, user.Ou)))
        {
            try { ad.RemoveGroupMember(s.Settings.DomainController, DnOf(g.Dn, g.Ou), DnOf(user.Dn, user.Ou)); } catch { }
        }
        DirectoryStore.AddAudit(s, "remove-member", "group", g.Name, $"Removed member {memberId}");
        return g;
    });
    return Results.Json(item);
});

app.MapDelete("/api/groups/{id}", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    store.Update(s =>
    {
        var g = s.Groups.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        if (s.Settings.DomainController.Connected && LooksLikeDn(DnOf(g.Dn, g.Ou)))
        {
            try { ad.DeleteObject(s.Settings.DomainController, DnOf(g.Dn, g.Ou)); } catch { }
        }
        DirectoryStore.Recycle(s, "group", g.Id, g.Name, g.Ou, g);
        s.Groups.RemoveAll(x => x.Id == id);
        foreach (var u in s.Users) u.Groups.RemoveAll(x => x == id);
        DirectoryStore.AddAudit(s, "delete", "group", g.Name, $"Deleted group {g.Name} (recycle bin)");
    });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/gpos", (DirectoryStore store, string? q, string? type, int page = 1, int pageSize = 50) =>
    Results.Json(Page(store.Snapshot().Gpos, q, type, page, pageSize, g => g.Status, GpoSearch)));

app.MapGet("/api/gpos/{id}", (string id, DirectoryStore store) =>
{
    var item = store.Snapshot().Gpos.FirstOrDefault(g => g.Id == id);
    return item is null ? Results.Json(new { message = "GPO not found" }, statusCode: 404) : Results.Json(item);
});

app.MapPost("/api/gpos", (DirectoryGpo body, DirectoryStore store) =>
{
    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.Json(new { message = "Missing required fields: name" }, statusCode: 400);
    var item = store.Update(s =>
    {
        var created = new DirectoryGpo
        {
            Id = DirectoryStore.NewId("gpo"),
            Name = body.Name,
            Status = string.IsNullOrWhiteSpace(body.Status) ? "enabled" : body.Status,
            LinkedOus = body.LinkedOus ?? new(),
            Enforced = body.Enforced,
            Description = body.Description ?? "",
            Created = DateTimeOffset.UtcNow,
            Modified = DateTimeOffset.UtcNow,
            Settings = body.Settings ?? new()
        };
        s.Gpos.Add(created);
        DirectoryStore.AddAudit(s, "create", "gpo", created.Name, "Created group policy object");
        return created;
    });
    return Results.Json(item, statusCode: 201);
});

app.MapPut("/api/gpos/{id}", (string id, DirectoryGpo body, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var g = s.Gpos.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        g.Name = body.Name ?? g.Name;
        g.Status = body.Status ?? g.Status;
        if (body.LinkedOus != null) g.LinkedOus = body.LinkedOus;
        g.Enforced = body.Enforced;
        g.Description = body.Description ?? g.Description;
        if (body.Settings != null) g.Settings = body.Settings;
        g.Modified = DateTimeOffset.UtcNow;
        DirectoryStore.AddAudit(s, "update", "gpo", g.Name, "Updated group policy object");
        return g;
    });
    return Results.Json(item);
});

app.MapDelete("/api/gpos/{id}", (string id, DirectoryStore store) =>
{
    store.Update(s =>
    {
        var g = s.Gpos.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        s.Gpos.RemoveAll(x => x.Id == id);
        DirectoryStore.AddAudit(s, "delete", "gpo", g.Name, "Deleted group policy object");
    });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/shares", (DirectoryStore store, string? q, string? type, int page = 1, int pageSize = 50) =>
    Results.Json(Page(store.Snapshot().Shares, q, type, page, pageSize, null, ShareSearch)));

app.MapGet("/api/shares/{id}", (string id, DirectoryStore store) =>
{
    var item = store.Snapshot().Shares.FirstOrDefault(x => x.Id == id);
    return item is null ? Results.Json(new { message = "Share not found" }, statusCode: 404) : Results.Json(item);
});

app.MapPost("/api/shares", (DirectoryShare body, DirectoryStore store) =>
{
    if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Path))
        return Results.Json(new { message = "Missing required fields: name, path" }, statusCode: 400);
    var item = store.Update(s =>
    {
        var created = new DirectoryShare
        {
            Id = DirectoryStore.NewId("sh"),
            Name = body.Name,
            Path = body.Path,
            Server = body.Server ?? "",
            Description = body.Description ?? "",
            Permissions = body.Permissions ?? new(),
            Hidden = body.Hidden,
            Created = DateTimeOffset.UtcNow
        };
        s.Shares.Add(created);
        DirectoryStore.AddAudit(s, "create", "share", created.Name, $"Created share {created.Path}");
        return created;
    });
    return Results.Json(item, statusCode: 201);
});

app.MapPut("/api/shares/{id}", (string id, DirectoryShare body, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var sh = s.Shares.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        sh.Name = body.Name ?? sh.Name;
        sh.Path = body.Path ?? sh.Path;
        sh.Server = body.Server ?? sh.Server;
        sh.Description = body.Description ?? sh.Description;
        if (body.Permissions != null) sh.Permissions = body.Permissions;
        sh.Hidden = body.Hidden;
        DirectoryStore.AddAudit(s, "update", "share", sh.Name, $"Updated share {sh.Name}");
        return sh;
    });
    return Results.Json(item);
});

app.MapDelete("/api/shares/{id}", (string id, DirectoryStore store) =>
{
    store.Update(s =>
    {
        var sh = s.Shares.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        s.Shares.RemoveAll(x => x.Id == id);
        DirectoryStore.AddAudit(s, "delete", "share", sh.Name, $"Deleted share {sh.Name}");
    });
    return Results.Json(new { ok = true });
});

app.MapGet("/api/laps", (DirectoryStore store, string? q, int page = 1, int pageSize = 50) =>
{
    var list = store.Snapshot().Laps.Select(l => new
    {
        l.Id, computerId = l.ComputerId, computerName = l.ComputerName, account = l.Account,
        expiration = l.Expiration, lastRotated = l.LastRotated,
        daysRemaining = (int)Math.Ceiling((l.Expiration - DateTimeOffset.UtcNow).TotalDays)
    });
    return Results.Json(Page(list, q, null, page, pageSize, null, l => $"{l.computerName}\n{l.account}"));
});

app.MapGet("/api/laps/{id}", (string id, DirectoryStore store, ActiveDirectoryClient ad) =>
{
    LapsRecord? item = null;
    store.Update(s =>
    {
        item = s.Laps.FirstOrDefault(x => x.Id == id);
        if (item == null) return;
        var op = s.Operators.FirstOrDefault(o => o.Id == s.Settings.CurrentOperatorId);
        var allowed = op == null || op.Permissions.Contains("*") || op.Role == "domainAdmin" ||
            s.LapsGrants.Any(g => g.Status == "active" && g.ExpiresAt > DateTimeOffset.UtcNow && g.OperatorId == op.Id && g.ComputerId == item.ComputerId);
        if (!allowed)
            throw new InvalidOperationException("LAPS access denied. Request a JIT grant for this computer.");
        if (s.Settings.DomainController.Connected)
        {
            var computer = s.Computers.FirstOrDefault(c => c.Id == item.ComputerId);
            if (computer != null && LooksLikeDn(DnOf(computer.Dn, computer.Ou)))
            {
                try
                {
                    var live = ad.ReadMsLapsPassword(s.Settings.DomainController, DnOf(computer.Dn, computer.Ou));
                    if (!string.IsNullOrEmpty(live)) item.Password = live;
                }
                catch { }
            }
        }
        DirectoryStore.AddAudit(s, "view-password", "laps", item.ComputerName, "Retrieved LAPS password (msLAPS-Password / ms-Mcs-AdmPwd)");
    });
    return item is null ? Results.Json(new { message = "LAPS record not found" }, statusCode: 404) : Results.Json(item);
});

app.MapPost("/api/laps", (JsonElement body, DirectoryStore store) =>
{
    var computerId = body.TryGetProperty("computerId", out var p) ? p.GetString() : null;
    if (string.IsNullOrEmpty(computerId))
        return Results.Json(new { message = "Missing required fields: computerId" }, statusCode: 400);
    try
    {
        var item = store.Update(s =>
        {
            var computer = s.Computers.FirstOrDefault(c => c.Id == computerId) ?? throw new KeyNotFoundException("Computer not found");
            if (s.Laps.Any(l => l.ComputerId == computer.Id))
                throw new InvalidOperationException("LAPS already configured for this computer");
            var account = body.TryGetProperty("account", out var a) ? a.GetString() : "Administrator";
            var ageDays = body.TryGetProperty("ageDays", out var d) && d.TryGetInt32(out var n) ? n : 30;
            var created = new LapsRecord
            {
                Id = DirectoryStore.NewId("laps"),
                ComputerId = computer.Id,
                ComputerName = computer.Name,
                Account = string.IsNullOrWhiteSpace(account) ? "Administrator" : account!,
                Password = ActiveDirectoryClient.RandomPassword(),
                Expiration = DateTimeOffset.UtcNow.AddDays(ageDays),
                LastRotated = DateTimeOffset.UtcNow
            };
            s.Laps.Add(created);
            DirectoryStore.AddAudit(s, "create", "laps", created.ComputerName, "Enabled LAPS for computer");
            return created;
        });
        return Results.Json(item, statusCode: 201);
    }
    catch (InvalidOperationException ex) { return Results.Json(new { message = ex.Message }, statusCode: 409); }
    catch (KeyNotFoundException ex) { return Results.Json(new { message = ex.Message }, statusCode: 404); }
});

app.MapPost("/api/laps/{id}/rotate", (string id, DirectoryStore store) =>
{
    var item = store.Update(s =>
    {
        var l = s.Laps.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
        l.Password = ActiveDirectoryClient.RandomPassword();
        l.LastRotated = DateTimeOffset.UtcNow;
        l.Expiration = DateTimeOffset.UtcNow.AddDays(30);
        DirectoryStore.AddAudit(s, "rotate", "laps", l.ComputerName, "Rotated LAPS password");
        return l;
    });
    return Results.Json(item);
});

app.MapGet("/api/audit", (DirectoryStore store, string? q, int page = 1, int pageSize = 50) =>
    Results.Json(Page(store.Snapshot().Audit, q, null, page, pageSize, null, a => $"{a.Action}\n{a.TargetType}\n{a.Target}\n{a.Detail}\n{a.Actor}\n{a.Result}")));

app.MapGet("/api/reports", (DirectoryStore store) =>
{
    var s = store.Snapshot();
    var stale = TimeSpan.FromDays(90);
    var now = DateTimeOffset.UtcNow;
    var inactiveUsers = s.Users.Where(u => u.LastLogon is null || now - u.LastLogon > stale).ToList();
    var disabledUsers = s.Users.Where(u => !u.Enabled).ToList();
    var lockedUsers = s.Users.Where(u => u.Locked).ToList();
    var passwordNeverExpires = s.Users.Where(u => u.PasswordNeverExpires).ToList();
    var privileged = s.Groups.Where(g => g.Name.Contains("admin", StringComparison.OrdinalIgnoreCase)).Select(g => new
    {
        group = g.Name,
        members = g.Members.Select(mid =>
        {
            var u = s.Users.FirstOrDefault(x => x.Id == mid);
            return u == null ? new { id = mid, name = mid, sam = "" } : new { id = u.Id, name = u.DisplayName, sam = u.SamAccountName };
        })
    });
    var emptyGroups = s.Groups.Where(g => g.Members.Count == 0).ToList();
    var unlinkedGpos = s.Gpos.Where(g => g.LinkedOus.Count == 0).ToList();
    var lapsSoon = s.Laps.Where(l => l.Expiration - now < TimeSpan.FromDays(14)).ToList();
    var computersNoLaps = s.Computers.Where(c => c.Type != "domainController" && s.Laps.All(l => l.ComputerId != c.Id)).ToList();
    return Results.Json(new
    {
        generatedAt = now,
        summaries = new
        {
            inactiveUsers = inactiveUsers.Count,
            disabledUsers = disabledUsers.Count,
            lockedUsers = lockedUsers.Count,
            passwordNeverExpires = passwordNeverExpires.Count,
            emptyGroups = emptyGroups.Count,
            unlinkedGpos = unlinkedGpos.Count,
            lapsExpiringSoon = lapsSoon.Count,
            computersNoLaps = computersNoLaps.Count
        },
        inactiveUsers, disabledUsers, lockedUsers, passwordNeverExpires, privileged, emptyGroups, unlinkedGpos, lapsSoon, computersNoLaps
    });
});

app.Run();
