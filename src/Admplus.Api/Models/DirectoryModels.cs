namespace Admplus.Api.Models;

public class DomainControllerSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string BindDn { get; set; } = "";
    public string Password { get; set; } = "";
    public string BaseDn { get; set; } = "";
    public string Domain { get; set; } = "";
    public bool Connected { get; set; }
    public DateTimeOffset? LastTest { get; set; }
    public string? LastError { get; set; }
}

public class AzureAdSettings
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool Connected { get; set; }
    public DateTimeOffset? LastTest { get; set; }
    public string? LastError { get; set; }
}

public class AppSettings
{
    public DomainControllerSettings DomainController { get; set; } = new();
    public AzureAdSettings AzureAd { get; set; } = new();
    public string CurrentOperatorId { get; set; } = "op-admin";
}

public class DirectoryUser
{
    public string Id { get; set; } = "";
    public string SamAccountName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string GivenName { get; set; } = "";
    public string Surname { get; set; } = "";
    public string UserPrincipalName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Type { get; set; } = "user";
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; }
    public bool PasswordNeverExpires { get; set; }
    public bool CannotChangePassword { get; set; }
    public bool MustChangePassword { get; set; }
    public string Department { get; set; } = "";
    public string Title { get; set; } = "";
    public string Office { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Manager { get; set; } = "";
    public string Ou { get; set; } = "";
    public List<string> Groups { get; set; } = new();
    public DateTimeOffset? LastLogon { get; set; }
    public DateTimeOffset Created { get; set; }
    public string Source { get; set; } = "ad";
    public string ImmutableId { get; set; } = "";
    public string SyncStatus { get; set; } = "onPrem";
    public bool PasswordHashSync { get; set; }
}

public class DirectoryComputer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DnsHostName { get; set; } = "";
    public string Os { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string Type { get; set; } = "workstation";
    public bool Enabled { get; set; } = true;
    public string Ou { get; set; } = "";
    public string Description { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public DateTimeOffset? LastLogon { get; set; }
    public DateTimeOffset Created { get; set; }
    public string ManagedBy { get; set; } = "";
    public string Source { get; set; } = "ad";
    public List<string> ServicePrincipalNames { get; set; } = new();
}

public class DirectoryGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SamAccountName { get; set; } = "";
    public string Type { get; set; } = "security";
    public string Scope { get; set; } = "global";
    public string Description { get; set; } = "";
    public string Ou { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public List<string> MemberOf { get; set; } = new();
    public string Mail { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public string Source { get; set; } = "ad";
}

public class DirectoryGpo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "enabled";
    public List<string> LinkedOus { get; set; } = new();
    public bool Enforced { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
    public Dictionary<string, object> Settings { get; set; } = new();
}

public class SharePermission
{
    public string Principal { get; set; } = "";
    public string Access { get; set; } = "read";
}

public class DirectoryShare
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Server { get; set; } = "";
    public string Description { get; set; } = "";
    public List<SharePermission> Permissions { get; set; } = new();
    public bool Hidden { get; set; }
    public DateTimeOffset Created { get; set; }
}

public class LapsRecord
{
    public string Id { get; set; } = "";
    public string ComputerId { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string Account { get; set; } = "Administrator";
    public string Password { get; set; } = "";
    public DateTimeOffset Expiration { get; set; }
    public DateTimeOffset LastRotated { get; set; }
}

public class AuditEntry
{
    public string Id { get; set; } = "";
    public DateTimeOffset Time { get; set; }
    public string Actor { get; set; } = "operator";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string Target { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Result { get; set; } = "success";
}

public class DirectoryState
{
    public AppSettings Settings { get; set; } = new();
    public List<DirectoryUser> Users { get; set; } = new();
    public List<DirectoryComputer> Computers { get; set; } = new();
    public List<DirectoryGroup> Groups { get; set; } = new();
    public List<DirectoryGpo> Gpos { get; set; } = new();
    public List<DirectoryShare> Shares { get; set; } = new();
    public List<LapsRecord> Laps { get; set; } = new();
    public List<AuditEntry> Audit { get; set; } = new();
    public List<OrganizationalUnit> Ous { get; set; } = new();
    public List<RecycleBinItem> RecycleBin { get; set; } = new();
    public List<JitGrant> JitGrants { get; set; } = new();
    public List<PasswordSettingsObject> PasswordPolicies { get; set; } = new();
    public List<GpoBackup> GpoBackups { get; set; } = new();
    public List<BitLockerKey> BitLockerKeys { get; set; } = new();
    public List<SpnRecord> Spns { get; set; } = new();
    public List<OperatorAccount> Operators { get; set; } = new();
    public List<LapsJitGrant> LapsGrants { get; set; } = new();
    public List<HybridIdentity> Hybrid { get; set; } = new();
    public SecurityHealth SecurityHealth { get; set; } = new();
}
