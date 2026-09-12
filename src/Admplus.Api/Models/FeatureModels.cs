namespace Admplus.Api.Models;

public class OrganizationalUnit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Dn { get; set; } = "";
    public string ParentId { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset Created { get; set; }
}

public class RecycleBinItem
{
    public string Id { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string OriginalOu { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset DeletedAt { get; set; }
    public string DeletedBy { get; set; } = "operator";
}

public class JitGrant
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "active";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public string Ticket { get; set; } = "";
}

public class PasswordSettingsObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Precedence { get; set; }
    public int MinLength { get; set; } = 12;
    public int HistoryCount { get; set; } = 24;
    public int MaxAgeDays { get; set; } = 90;
    public int MinAgeDays { get; set; } = 1;
    public bool Complexity { get; set; } = true;
    public int LockoutThreshold { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 30;
    public List<string> AppliesTo { get; set; } = new();
    public bool IsDomainDefault { get; set; }
}

public class GpoBackup
{
    public string Id { get; set; } = "";
    public string GpoId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public Dictionary<string, object> Snapshot { get; set; } = new();
}

public class BitLockerKey
{
    public string Id { get; set; } = "";
    public string ComputerId { get; set; } = "";
    public string ComputerName { get; set; } = "";
    public string RecoveryGuid { get; set; } = "";
    public string RecoveryPassword { get; set; } = "";
    public string Volume { get; set; } = "C:";
    public DateTimeOffset Created { get; set; }
}

public class SpnRecord
{
    public string Id { get; set; } = "";
    public string Principal { get; set; } = "";
    public string PrincipalId { get; set; } = "";
    public string Spn { get; set; } = "";
    public string Kind { get; set; } = "computer";
}

public class OperatorAccount
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Upn { get; set; } = "";
    public string Role { get; set; } = "helpdesk";
    public bool Enabled { get; set; } = true;
    public List<string> Permissions { get; set; } = new();
}

public class LapsJitGrant
{
    public string Id { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string ComputerId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "active";
}

public class HybridIdentity
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OnPremSid { get; set; } = "";
    public string ImmutableId { get; set; } = "";
    public string SourceAnchor { get; set; } = "";
    public string SyncStatus { get; set; } = "synced";
    public bool PasswordHashSync { get; set; } = true;
    public bool PassThroughAuth { get; set; }
    public DateTimeOffset? LastSync { get; set; }
    public string CloudUpn { get; set; } = "";
}

public class SecurityHealth
{
    public bool LdapSigningRequired { get; set; }
    public bool LdapChannelBinding { get; set; }
    public bool KerberosAes { get; set; }
    public bool NtlmV1Disabled { get; set; }
    public bool SmbSigning { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}
