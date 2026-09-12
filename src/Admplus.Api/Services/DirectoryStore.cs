using System.Text.Json;
using Admplus.Api.Models;

namespace Admplus.Api.Services;

public class DirectoryStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private DirectoryState _state;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DirectoryStore(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "store.json");
        _state = LoadOrSeed();
    }

    public DirectoryState Snapshot()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(_state, JsonOpts);
            return JsonSerializer.Deserialize<DirectoryState>(json, JsonOpts)!;
        }
    }

    public T Update<T>(Func<DirectoryState, T> mutator)
    {
        lock (_gate)
        {
            var result = mutator(_state);
            Persist();
            return result;
        }
    }

    public void Update(Action<DirectoryState> mutator)
    {
        lock (_gate)
        {
            mutator(_state);
            Persist();
        }
    }

    public void Audit(string action, string targetType, string target, string detail, string result = "success", string actor = "operator")
    {
        lock (_gate)
        {
            AddAudit(_state, action, targetType, target, detail, result, actor);
            Persist();
        }
    }

    public static void AddAudit(DirectoryState state, string action, string targetType, string target, string detail, string result = "success", string actor = "operator")
    {
        state.Audit.Insert(0, new AuditEntry
        {
            Id = NewId("a"),
            Time = DateTimeOffset.UtcNow,
            Actor = actor,
            Action = action,
            TargetType = targetType,
            Target = target,
            Detail = detail,
            Result = result
        });
        if (state.Audit.Count > 500)
            state.Audit.RemoveRange(500, state.Audit.Count - 500);
    }

    public static string NewId(string prefix) =>
        $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Guid.NewGuid().ToString("n")[..6]}";

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_state, JsonOpts);
        File.WriteAllText(_path, json);
    }

    public static void Recycle(DirectoryState state, string objectType, string objectId, string name, string originalOu, object payload)
    {
        state.RecycleBin.Insert(0, new RecycleBinItem
        {
            Id = NewId("rb"),
            ObjectType = objectType,
            ObjectId = objectId,
            Name = name,
            OriginalOu = originalOu,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOpts),
            DeletedAt = DateTimeOffset.UtcNow
        });
        if (state.RecycleBin.Count > 200)
            state.RecycleBin.RemoveRange(200, state.RecycleBin.Count - 200);
    }

    private DirectoryState LoadOrSeed()
    {
        if (File.Exists(_path))
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<DirectoryState>(json, JsonOpts) ?? Seed();
            EnsureFeatureDefaults(loaded);
            PersistLoaded(loaded);
            return loaded;
        }
        var seeded = Seed();
        File.WriteAllText(_path, JsonSerializer.Serialize(seeded, JsonOpts));
        return seeded;
    }

    private void PersistLoaded(DirectoryState state)
    {
        _state = state;
        Persist();
    }

    private static void EnsureFeatureDefaults(DirectoryState s)
    {
        if (s.Ous.Count == 0 || s.PasswordPolicies.Count == 0 || s.Operators.Count == 0)
        {
            var extra = Seed();
            if (s.Ous.Count == 0) s.Ous = extra.Ous;
            if (s.PasswordPolicies.Count == 0) s.PasswordPolicies = extra.PasswordPolicies;
            if (s.Operators.Count == 0) s.Operators = extra.Operators;
            if (s.BitLockerKeys.Count == 0) s.BitLockerKeys = extra.BitLockerKeys;
            if (s.Spns.Count == 0) s.Spns = extra.Spns;
            if (s.Hybrid.Count == 0) s.Hybrid = extra.Hybrid;
            if (s.JitGrants.Count == 0) s.JitGrants = extra.JitGrants;
            if (s.LapsGrants.Count == 0) s.LapsGrants = extra.LapsGrants;
            if (s.GpoBackups.Count == 0) s.GpoBackups = extra.GpoBackups;
            s.SecurityHealth ??= extra.SecurityHealth;
            if (string.IsNullOrEmpty(s.Settings.CurrentOperatorId))
                s.Settings.CurrentOperatorId = "op-admin";
            foreach (var u in s.Users)
            {
                if (string.IsNullOrEmpty(u.SyncStatus)) u.SyncStatus = "onPrem";
            }
            foreach (var c in s.Computers)
            {
                if (c.ServicePrincipalNames.Count == 0 && !string.IsNullOrEmpty(c.DnsHostName))
                {
                    c.ServicePrincipalNames.Add($"HOST/{c.Name}");
                    c.ServicePrincipalNames.Add($"HOST/{c.DnsHostName}");
                }
            }
        }
    }

    private static DirectoryState Seed()
    {
        var created = DateTimeOffset.UtcNow;
        return new DirectoryState
        {
            Users =
            {
                new DirectoryUser
                {
                    Id = "u-admin", SamAccountName = "administrator", DisplayName = "Domain Administrator",
                    GivenName = "Domain", Surname = "Admin", UserPrincipalName = "administrator@contoso.local",
                    Email = "admin@contoso.local", Type = "user", Enabled = true, PasswordNeverExpires = true,
                    Department = "IT", Title = "Enterprise Admin", Office = "HQ", Phone = "+1-555-0100",
                    Ou = "CN=Users,DC=contoso,DC=local", Groups = { "g-ea", "g-da", "g-domain-users" },
                    LastLogon = created, Created = created, ImmutableId = "YWRtaW5pc3RyYXRvcg==", SyncStatus = "synced", PasswordHashSync = true
                },
                new DirectoryUser
                {
                    Id = "u-jsmith", SamAccountName = "jsmith", DisplayName = "Jane Smith",
                    GivenName = "Jane", Surname = "Smith", UserPrincipalName = "jsmith@contoso.local",
                    Email = "jane.smith@contoso.com", Type = "user", Department = "Finance", Title = "Controller",
                    Office = "Floor 3", Phone = "+1-555-0142", Manager = "u-admin",
                    Ou = "OU=Finance,DC=contoso,DC=local", Groups = { "g-finance", "g-domain-users" },
                    LastLogon = created, Created = created, ImmutableId = "anNtaXRo", SyncStatus = "synced", PasswordHashSync = true
                },
                new DirectoryUser
                {
                    Id = "u-mjones", SamAccountName = "mjones", DisplayName = "Marcus Jones",
                    GivenName = "Marcus", Surname = "Jones", UserPrincipalName = "mjones@contoso.local",
                    Email = "marcus.jones@contoso.com", Type = "user", Department = "Engineering",
                    Title = "Systems Engineer", Office = "Lab A", Phone = "+1-555-0188", Manager = "u-admin",
                    MustChangePassword = true, Ou = "OU=Engineering,DC=contoso,DC=local",
                    Groups = { "g-eng", "g-helpdesk", "g-domain-users" }, LastLogon = created, Created = created,
                    ImmutableId = "bWpvbmVz", SyncStatus = "pending", PasswordHashSync = true
                },
                new DirectoryUser
                {
                    Id = "u-svc-sql", SamAccountName = "svc-sql", DisplayName = "SQL Service Account",
                    GivenName = "SQL", Surname = "Service", UserPrincipalName = "svc-sql@contoso.local",
                    Type = "service", PasswordNeverExpires = true, CannotChangePassword = true,
                    Department = "IT", Title = "Service Account", Ou = "OU=Service Accounts,DC=contoso,DC=local",
                    Groups = { "g-domain-users" }, LastLogon = created, Created = created,
                    SyncStatus = "onPrem", PasswordHashSync = false
                },
                new DirectoryUser
                {
                    Id = "u-guest", SamAccountName = "guest", DisplayName = "Guest", Surname = "Guest",
                    UserPrincipalName = "guest@contoso.local", Type = "guest", Enabled = false,
                    PasswordNeverExpires = true, CannotChangePassword = true,
                    Ou = "CN=Users,DC=contoso,DC=local", Groups = { "g-domain-guests" }, Created = created,
                    SyncStatus = "onPrem"
                },
                new DirectoryUser
                {
                    Id = "u-inet", SamAccountName = "inet-klee", DisplayName = "Kai Lee (InetOrg)",
                    GivenName = "Kai", Surname = "Lee", UserPrincipalName = "klee@contoso.local",
                    Email = "kai.lee@contoso.com", Type = "inetOrgPerson", Department = "Marketing",
                    Title = "Campaign Lead", Office = "Remote", Phone = "+1-555-0199", Manager = "u-jsmith",
                    Ou = "OU=Marketing,DC=contoso,DC=local", Groups = { "g-mkt", "g-domain-users" },
                    LastLogon = created, Created = created, ImmutableId = "a2xlZQ==", SyncStatus = "synced", PasswordHashSync = true
                }
            },
            Computers =
            {
                new DirectoryComputer
                {
                    Id = "c-dc01", Name = "DC01", DnsHostName = "dc01.contoso.local",
                    Os = "Windows Server 2022 Datacenter", OsVersion = "10.0.20348", Type = "domainController",
                    Ou = "OU=Domain Controllers,DC=contoso,DC=local", Description = "Primary domain controller",
                    IpAddress = "10.0.0.10", LastLogon = created, Created = created, ManagedBy = "u-admin",
                    ServicePrincipalNames = { "HOST/DC01", "HOST/dc01.contoso.local", "ldap/dc01.contoso.local", "GC/dc01.contoso.local" }
                },
                new DirectoryComputer
                {
                    Id = "c-fs01", Name = "FS01", DnsHostName = "fs01.contoso.local",
                    Os = "Windows Server 2022 Standard", OsVersion = "10.0.20348", Type = "server",
                    Ou = "OU=Servers,DC=contoso,DC=local", Description = "File server",
                    IpAddress = "10.0.0.21", LastLogon = created, Created = created, ManagedBy = "u-mjones",
                    ServicePrincipalNames = { "HOST/FS01", "HOST/fs01.contoso.local", "RestrictedKrbHost/FS01" }
                },
                new DirectoryComputer
                {
                    Id = "c-wks-042", Name = "WKS-042", DnsHostName = "wks-042.contoso.local",
                    Os = "Windows 11 Enterprise", OsVersion = "10.0.22631", Type = "workstation",
                    Ou = "OU=Workstations,DC=contoso,DC=local", Description = "Finance workstation",
                    IpAddress = "10.0.10.42", LastLogon = created, Created = created, ManagedBy = "u-jsmith",
                    ServicePrincipalNames = { "HOST/WKS-042", "HOST/wks-042.contoso.local", "RestrictedKrbHost/FS01" }
                },
                new DirectoryComputer
                {
                    Id = "c-lap-018", Name = "LAP-018", DnsHostName = "lap-018.contoso.local",
                    Os = "Windows 11 Pro", OsVersion = "10.0.22631", Type = "laptop",
                    Ou = "OU=Laptops,DC=contoso,DC=local", Description = "Engineering laptop",
                    IpAddress = "10.0.20.18", LastLogon = created, Created = created, ManagedBy = "u-mjones",
                    ServicePrincipalNames = { "HOST/LAP-018", "HOST/lap-018.contoso.local", "TERMSRV/lap-018.contoso.local" }
                }
            },
            Groups =
            {
                new DirectoryGroup { Id = "g-ea", Name = "Enterprise Admins", SamAccountName = "Enterprise Admins", Type = "security", Scope = "universal", Description = "Members can make forest-wide changes", Ou = "CN=Users,DC=contoso,DC=local", Members = { "u-admin" }, Created = created },
                new DirectoryGroup { Id = "g-da", Name = "Domain Admins", SamAccountName = "Domain Admins", Type = "security", Scope = "global", Description = "Designated administrators of the domain", Ou = "CN=Users,DC=contoso,DC=local", Members = { "u-admin" }, MemberOf = { "g-ea" }, Created = created },
                new DirectoryGroup { Id = "g-domain-users", Name = "Domain Users", SamAccountName = "Domain Users", Type = "security", Scope = "global", Description = "All domain users", Ou = "CN=Users,DC=contoso,DC=local", Members = { "u-admin", "u-jsmith", "u-mjones", "u-svc-sql", "u-inet" }, Created = created },
                new DirectoryGroup { Id = "g-domain-guests", Name = "Domain Guests", SamAccountName = "Domain Guests", Type = "security", Scope = "global", Description = "All domain guests", Ou = "CN=Users,DC=contoso,DC=local", Members = { "u-guest" }, Created = created },
                new DirectoryGroup { Id = "g-finance", Name = "Finance", SamAccountName = "Finance", Type = "security", Scope = "global", Description = "Finance department", Ou = "OU=Groups,DC=contoso,DC=local", Members = { "u-jsmith" }, Mail = "finance@contoso.com", Created = created },
                new DirectoryGroup { Id = "g-eng", Name = "Engineering", SamAccountName = "Engineering", Type = "security", Scope = "global", Description = "Engineering department", Ou = "OU=Groups,DC=contoso,DC=local", Members = { "u-mjones" }, Mail = "engineering@contoso.com", Created = created },
                new DirectoryGroup { Id = "g-mkt", Name = "Marketing DL", SamAccountName = "Marketing-DL", Type = "distribution", Scope = "universal", Description = "Marketing distribution list", Ou = "OU=Groups,DC=contoso,DC=local", Members = { "u-inet" }, Mail = "marketing@contoso.com", Created = created },
                new DirectoryGroup { Id = "g-helpdesk", Name = "Helpdesk", SamAccountName = "Helpdesk", Type = "security", Scope = "domainLocal", Description = "Helpdesk operators", Ou = "OU=Groups,DC=contoso,DC=local", Members = { "u-mjones" }, Created = created }
            },
            Gpos =
            {
                new DirectoryGpo
                {
                    Id = "gpo-default-domain", Name = "Default Domain Policy", Status = "enabled",
                    LinkedOus = { "DC=contoso,DC=local" }, Enforced = true,
                    Description = "Password, lockout, and Kerberos settings", Created = created, Modified = created,
                    Settings = new Dictionary<string, object> { ["passwordMinLength"] = 12, ["passwordComplexity"] = true, ["lockoutThreshold"] = 5, ["lockoutDurationMinutes"] = 30, ["auditLogonEvents"] = true }
                },
                new DirectoryGpo
                {
                    Id = "gpo-default-dc", Name = "Default Domain Controllers Policy", Status = "enabled",
                    LinkedOus = { "OU=Domain Controllers,DC=contoso,DC=local" }, Enforced = true,
                    Description = "DC audit and user rights", Created = created, Modified = created,
                    Settings = new Dictionary<string, object> { ["auditAccountLogon"] = true, ["auditDirectoryService"] = true, ["restrictAnonymous"] = true }
                },
                new DirectoryGpo
                {
                    Id = "gpo-workstation", Name = "Workstation Hardening", Status = "enabled",
                    LinkedOus = { "OU=Workstations,DC=contoso,DC=local", "OU=Laptops,DC=contoso,DC=local" },
                    Description = "Firewall, BitLocker, and Windows Update", Created = created, Modified = created,
                    Settings = new Dictionary<string, object> { ["bitlockerRequired"] = true, ["firewallEnabled"] = true, ["windowsUpdate"] = "WSUS" }
                },
                new DirectoryGpo
                {
                    Id = "gpo-laps", Name = "LAPS Policy", Status = "enabled",
                    LinkedOus = { "OU=Workstations,DC=contoso,DC=local", "OU=Laptops,DC=contoso,DC=local", "OU=Servers,DC=contoso,DC=local" },
                    Enforced = true, Description = "Local Administrator Password Solution", Created = created, Modified = created,
                    Settings = new Dictionary<string, object> { ["passwordLength"] = 16, ["passwordAgeDays"] = 30, ["complexity"] = true }
                }
            },
            Shares =
            {
                new DirectoryShare
                {
                    Id = "sh-dept", Name = "Departments", Path = @"\\FS01\Departments", Server = "FS01",
                    Description = "Department file shares", Created = created,
                    Permissions = { new() { Principal = "g-finance", Access = "modify" }, new() { Principal = "g-eng", Access = "modify" }, new() { Principal = "g-da", Access = "full" } }
                },
                new DirectoryShare
                {
                    Id = "sh-it", Name = "IT$", Path = @"\\FS01\IT$", Server = "FS01",
                    Description = "Hidden IT share", Hidden = true, Created = created,
                    Permissions = { new() { Principal = "g-da", Access = "full" } }
                },
                new DirectoryShare
                {
                    Id = "sh-public", Name = "Public", Path = @"\\FS01\Public", Server = "FS01",
                    Description = "Company public files", Created = created,
                    Permissions = { new() { Principal = "g-domain-users", Access = "read" }, new() { Principal = "g-da", Access = "full" } }
                }
            },
            Laps =
            {
                new LapsRecord { Id = "laps-wks-042", ComputerId = "c-wks-042", ComputerName = "WKS-042", Password = "Kx7!mP2qR9sT4vW1", Expiration = created.AddDays(14), LastRotated = created },
                new LapsRecord { Id = "laps-lap-018", ComputerId = "c-lap-018", ComputerName = "LAP-018", Password = "Nz3#bL8cY1dH6jK2", Expiration = created.AddDays(21), LastRotated = created },
                new LapsRecord { Id = "laps-fs01", ComputerId = "c-fs01", ComputerName = "FS01", Password = "Qv5$tF0wE4xU9iA7", Expiration = created.AddDays(7), LastRotated = created }
            },
            Audit =
            {
                new AuditEntry
                {
                    Id = "a-1", Time = created, Actor = "system", Action = "seed", TargetType = "store",
                    Target = "initial", Detail = "Directory store initialized with sample objects (ASP.NET Core + DirectoryServices.Protocols)"
                }
            },
            Ous =
            {
                new OrganizationalUnit { Id = "ou-root", Name = "contoso.local", Dn = "DC=contoso,DC=local", ParentId = "", Description = "Domain root", Created = created },
                new OrganizationalUnit { Id = "ou-users", Name = "Users", Dn = "CN=Users,DC=contoso,DC=local", ParentId = "ou-root", Description = "Default users container", Created = created },
                new OrganizationalUnit { Id = "ou-finance", Name = "Finance", Dn = "OU=Finance,DC=contoso,DC=local", ParentId = "ou-root", Description = "Finance department", Created = created },
                new OrganizationalUnit { Id = "ou-eng", Name = "Engineering", Dn = "OU=Engineering,DC=contoso,DC=local", ParentId = "ou-root", Description = "Engineering department", Created = created },
                new OrganizationalUnit { Id = "ou-mkt", Name = "Marketing", Dn = "OU=Marketing,DC=contoso,DC=local", ParentId = "ou-root", Description = "Marketing", Created = created },
                new OrganizationalUnit { Id = "ou-svc", Name = "Service Accounts", Dn = "OU=Service Accounts,DC=contoso,DC=local", ParentId = "ou-root", Description = "Service accounts", Created = created },
                new OrganizationalUnit { Id = "ou-groups", Name = "Groups", Dn = "OU=Groups,DC=contoso,DC=local", ParentId = "ou-root", Description = "Security and distribution groups", Created = created },
                new OrganizationalUnit { Id = "ou-dc", Name = "Domain Controllers", Dn = "OU=Domain Controllers,DC=contoso,DC=local", ParentId = "ou-root", Description = "Domain controllers", Created = created },
                new OrganizationalUnit { Id = "ou-servers", Name = "Servers", Dn = "OU=Servers,DC=contoso,DC=local", ParentId = "ou-root", Description = "Member servers", Created = created },
                new OrganizationalUnit { Id = "ou-wks", Name = "Workstations", Dn = "OU=Workstations,DC=contoso,DC=local", ParentId = "ou-root", Description = "Desktops", Created = created },
                new OrganizationalUnit { Id = "ou-laptops", Name = "Laptops", Dn = "OU=Laptops,DC=contoso,DC=local", ParentId = "ou-root", Description = "Mobile devices", Created = created },
                new OrganizationalUnit { Id = "ou-computers", Name = "Computers", Dn = "OU=Computers,DC=contoso,DC=local", ParentId = "ou-root", Description = "Default computers", Created = created }
            },
            PasswordPolicies =
            {
                new PasswordSettingsObject
                {
                    Id = "pso-domain", Name = "Default Domain Policy", Precedence = 999, MinLength = 12, HistoryCount = 24,
                    MaxAgeDays = 90, MinAgeDays = 1, Complexity = true, LockoutThreshold = 5, LockoutMinutes = 30,
                    IsDomainDefault = true, AppliesTo = { "g-domain-users" }
                },
                new PasswordSettingsObject
                {
                    Id = "pso-admins", Name = "Privileged Accounts PSO", Precedence = 10, MinLength = 16, HistoryCount = 24,
                    MaxAgeDays = 30, MinAgeDays = 1, Complexity = true, LockoutThreshold = 3, LockoutMinutes = 60,
                    AppliesTo = { "g-da", "g-ea" }
                },
                new PasswordSettingsObject
                {
                    Id = "pso-svc", Name = "Service Accounts PSO", Precedence = 50, MinLength = 24, HistoryCount = 12,
                    MaxAgeDays = 365, MinAgeDays = 0, Complexity = true, LockoutThreshold = 0, LockoutMinutes = 0,
                    AppliesTo = { }
                }
            },
            Operators =
            {
                new OperatorAccount { Id = "op-admin", Name = "Directory Admin", Upn = "admplus-admin@contoso.local", Role = "domainAdmin", Permissions = { "*" } },
                new OperatorAccount { Id = "op-helpdesk", Name = "Helpdesk Operator", Upn = "helpdesk@contoso.local", Role = "helpdesk", Permissions = { "users.resetPassword", "users.unlock", "laps.view.jit", "bitlocker.view" } },
                new OperatorAccount { Id = "op-audit", Name = "Auditor", Upn = "auditor@contoso.local", Role = "auditor", Enabled = true, Permissions = { "audit.read", "reports.read", "laps.deny" } }
            },
            BitLockerKeys =
            {
                new BitLockerKey { Id = "bl-wks-042", ComputerId = "c-wks-042", ComputerName = "WKS-042", RecoveryGuid = "a1b2c3d4-e5f6-7890-abcd-ef1234567890", RecoveryPassword = "123456-789012-345678-901234-567890-123456-789012-345678", Volume = "C:", Created = created.AddDays(-40) },
                new BitLockerKey { Id = "bl-lap-018", ComputerId = "c-lap-018", ComputerName = "LAP-018", RecoveryGuid = "f0e1d2c3-b4a5-9687-5432-10fedcba9876", RecoveryPassword = "654321-098765-432109-876543-210987-654321-098765-432109", Volume = "C:", Created = created.AddDays(-12) }
            },
            Spns =
            {
                new SpnRecord { Id = "spn-1", Principal = "DC01", PrincipalId = "c-dc01", Spn = "HOST/dc01.contoso.local", Kind = "computer" },
                new SpnRecord { Id = "spn-2", Principal = "DC01", PrincipalId = "c-dc01", Spn = "ldap/dc01.contoso.local", Kind = "computer" },
                new SpnRecord { Id = "spn-3", Principal = "FS01", PrincipalId = "c-fs01", Spn = "HOST/fs01.contoso.local", Kind = "computer" },
                new SpnRecord { Id = "spn-4", Principal = "FS01", PrincipalId = "c-fs01", Spn = "RestrictedKrbHost/FS01", Kind = "computer" },
                new SpnRecord { Id = "spn-5", Principal = "WKS-042", PrincipalId = "c-wks-042", Spn = "RestrictedKrbHost/FS01", Kind = "computer" },
                new SpnRecord { Id = "spn-6", Principal = "svc-sql", PrincipalId = "u-svc-sql", Spn = "MSSQLSvc/fs01.contoso.local:1433", Kind = "user" },
                new SpnRecord { Id = "spn-7", Principal = "svc-sql", PrincipalId = "u-svc-sql", Spn = "MSSQLSvc/fs01.contoso.local", Kind = "user" }
            },
            Hybrid =
            {
                new HybridIdentity { UserId = "u-admin", DisplayName = "Domain Administrator", OnPremSid = "S-1-5-21-1000-2000-3000-500", ImmutableId = "YWRtaW5pc3RyYXRvcg==", SourceAnchor = "YWRtaW5pc3RyYXRvcg==", SyncStatus = "synced", PasswordHashSync = true, LastSync = created, CloudUpn = "administrator@contoso.onmicrosoft.com" },
                new HybridIdentity { UserId = "u-jsmith", DisplayName = "Jane Smith", OnPremSid = "S-1-5-21-1000-2000-3000-1104", ImmutableId = "anNtaXRo", SourceAnchor = "anNtaXRo", SyncStatus = "synced", PasswordHashSync = true, LastSync = created, CloudUpn = "jsmith@contoso.onmicrosoft.com" },
                new HybridIdentity { UserId = "u-mjones", DisplayName = "Marcus Jones", OnPremSid = "S-1-5-21-1000-2000-3000-1105", ImmutableId = "bWpvbmVz", SourceAnchor = "bWpvbmVz", SyncStatus = "pending", PasswordHashSync = true, LastSync = created.AddHours(-6), CloudUpn = "mjones@contoso.onmicrosoft.com" },
                new HybridIdentity { UserId = "u-inet", DisplayName = "Kai Lee (InetOrg)", OnPremSid = "S-1-5-21-1000-2000-3000-1108", ImmutableId = "a2xlZQ==", SourceAnchor = "a2xlZQ==", SyncStatus = "synced", PasswordHashSync = true, LastSync = created, CloudUpn = "klee@contoso.onmicrosoft.com" },
                new HybridIdentity { UserId = "u-svc-sql", DisplayName = "SQL Service Account", OnPremSid = "S-1-5-21-1000-2000-3000-1106", ImmutableId = "", SourceAnchor = "", SyncStatus = "onPrem", PasswordHashSync = false, CloudUpn = "" }
            },
            JitGrants =
            {
                new JitGrant { Id = "jit-1", UserId = "u-mjones", GroupId = "g-da", Reason = "Emergency patch window", Status = "active", StartsAt = created.AddHours(-1), ExpiresAt = created.AddHours(3), RequestedBy = "op-admin", Ticket = "CHG-1042" }
            },
            LapsGrants =
            {
                new LapsJitGrant { Id = "lj-1", OperatorId = "op-helpdesk", ComputerId = "c-wks-042", Reason = "Local admin unlock", ExpiresAt = created.AddHours(2), Status = "active" }
            },
            GpoBackups =
            {
                new GpoBackup
                {
                    Id = "gb-1", GpoId = "gpo-default-domain", Name = "Default Domain Policy", Created = created.AddDays(-7),
                    Snapshot = new Dictionary<string, object> { ["status"] = "enabled", ["passwordMinLength"] = 12 }
                }
            },
            SecurityHealth = new SecurityHealth
            {
                LdapSigningRequired = true,
                LdapChannelBinding = true,
                KerberosAes = true,
                NtlmV1Disabled = false,
                SmbSigning = true,
                CheckedAt = created
            }
        };
    }
}
