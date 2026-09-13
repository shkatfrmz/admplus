using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Admplus.Api.Models;

namespace Admplus.Api.Services;

public class ActiveDirectoryClient
{
    private readonly AppLog _log;

    public ActiveDirectoryClient(AppLog log)
    {
        _log = log;
    }

    public const int AdsUfAccountDisable = 0x2;
    public const int AdsUfLockout = 0x10;
    public const int AdsUfPasswdCantChange = 0x40;
    public const int AdsUfNormalAccount = 0x200;
    public const int AdsUfDontExpirePasswd = 0x10000;
    public const int AdsUfWorkstationTrust = 0x1000;
    public const int AdsUfServerTrust = 0x2000;
    public const int AdsGroupTypeSecurity = unchecked((int)0x80000000);
    public const int AdsGroupTypeGlobal = 0x2;
    public const int AdsGroupTypeDomainLocal = 0x4;
    public const int AdsGroupTypeUniversal = 0x8;

    public Task TestConnectionAsync(DomainControllerSettings dc, CancellationToken ct = default)
    {
        DirectoryConnection.Normalize(dc);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(dc.Host)) missing.Add("host");
        if (string.IsNullOrWhiteSpace(dc.BindDn)) missing.Add("bind DN (or user@domain)");
        if (string.IsNullOrWhiteSpace(dc.Password)) missing.Add("password");
        if (missing.Count > 0)
        {
            var msg = $"Cannot bind: missing {string.Join(", ", missing)}. Fill Host (or Domain), Bind DN, and Password, then Save or Test.";
            _log.Write("warn", "ldap", msg, DirectoryConnection.Describe(dc));
            throw new InvalidOperationException(msg);
        }

        _log.Write("info", "ldap", $"Binding to {dc.Host}:{dc.Port} ssl={dc.UseSsl} as {dc.BindDn}");
        using var connection = Bind(dc);
        _log.Write("info", "ldap", "Bind succeeded");
        var probeDn = string.IsNullOrWhiteSpace(dc.BaseDn) ? ResolveBase(dc) : dc.BaseDn;
        if (!string.IsNullOrWhiteSpace(probeDn))
        {
            var request = new SearchRequest(probeDn, "(objectClass=*)", SearchScope.Base, "distinguishedName")
            {
                SizeLimit = 1
            };
            connection.SendRequest(request);
            _log.Write("info", "ldap", $"Base DN probe ok: {probeDn}");
        }
        return Task.CompletedTask;
    }

    public List<DirectoryUser> SearchUsers(DomainControllerSettings dc)
    {
        using var connection = Bind(dc);
        var baseDn = ResolveBase(dc);
        var request = new SearchRequest(
            baseDn,
            "(&(|(objectClass=user)(objectClass=inetOrgPerson))(!(objectClass=computer)))",
            SearchScope.Subtree,
            "sAMAccountName", "displayName", "givenName", "sn", "userPrincipalName", "mail",
            "department", "title", "physicalDeliveryOfficeName", "telephoneNumber",
            "userAccountControl", "distinguishedName", "objectClass", "pwdLastSet", "lockoutTime")
        {
            SizeLimit = 500
        };
        var response = (SearchResponse)connection.SendRequest(request);
        var users = new List<DirectoryUser>();
        foreach (SearchResultEntry entry in response.Entries)
        {
            var sam = Attr(entry, "sAMAccountName");
            if (string.IsNullOrEmpty(sam)) continue;
            var uac = ParseInt(Attr(entry, "userAccountControl"));
            var classes = Attrs(entry, "objectClass").Select(c => c.ToLowerInvariant()).ToHashSet();
            var type = "user";
            if (classes.Contains("msds-groupmanagedserviceaccount") || classes.Contains("msds-managedserviceaccount"))
                type = "managedService";
            else if (classes.Contains("inetorgperson") && !classes.Contains("user"))
                type = "inetOrgPerson";
            else if (classes.Contains("contact"))
                type = "contact";

            users.Add(new DirectoryUser
            {
                SamAccountName = sam,
                DisplayName = Attr(entry, "displayName", sam),
                GivenName = Attr(entry, "givenName"),
                Surname = Attr(entry, "sn"),
                UserPrincipalName = Attr(entry, "userPrincipalName"),
                Email = Attr(entry, "mail"),
                Type = type,
                Enabled = (uac & AdsUfAccountDisable) == 0,
                Locked = (uac & AdsUfLockout) != 0 || Attr(entry, "lockoutTime") is { Length: > 0 } and not "0",
                PasswordNeverExpires = (uac & AdsUfDontExpirePasswd) != 0,
                CannotChangePassword = (uac & AdsUfPasswdCantChange) != 0,
                MustChangePassword = Attr(entry, "pwdLastSet") == "0",
                Department = Attr(entry, "department"),
                Title = Attr(entry, "title"),
                Office = Attr(entry, "physicalDeliveryOfficeName"),
                Phone = Attr(entry, "telephoneNumber"),
                Ou = Attr(entry, "distinguishedName"),
                Source = "ad-live",
                Created = DateTimeOffset.UtcNow
            });
        }
        return users;
    }

    public void CreateUser(DomainControllerSettings dc, DirectoryUser user, string? password)
    {
        using var connection = Bind(dc);
        var parent = string.IsNullOrWhiteSpace(user.Ou) ? $"CN=Users,{ResolveBase(dc)}" : ParentDn(user.Ou);
        var dn = $"CN={EscapeCn(user.DisplayName)},{parent}";
        var request = new AddRequest(dn);
        request.Attributes.Add(new DirectoryAttribute("objectClass", "top", "person", "organizationalPerson", "user"));
        request.Attributes.Add(new DirectoryAttribute("sAMAccountName", user.SamAccountName));
        request.Attributes.Add(new DirectoryAttribute("displayName", user.DisplayName));
        if (!string.IsNullOrWhiteSpace(user.GivenName)) request.Attributes.Add(new DirectoryAttribute("givenName", user.GivenName));
        if (!string.IsNullOrWhiteSpace(user.Surname)) request.Attributes.Add(new DirectoryAttribute("sn", user.Surname));
        if (!string.IsNullOrWhiteSpace(user.UserPrincipalName)) request.Attributes.Add(new DirectoryAttribute("userPrincipalName", user.UserPrincipalName));
        if (!string.IsNullOrWhiteSpace(user.Email)) request.Attributes.Add(new DirectoryAttribute("mail", user.Email));
        if (!string.IsNullOrWhiteSpace(user.Department)) request.Attributes.Add(new DirectoryAttribute("department", user.Department));
        if (!string.IsNullOrWhiteSpace(user.Title)) request.Attributes.Add(new DirectoryAttribute("title", user.Title));
        var uac = AdsUfNormalAccount | AdsUfAccountDisable;
        if (user.PasswordNeverExpires) uac |= AdsUfDontExpirePasswd;
        request.Attributes.Add(new DirectoryAttribute("userAccountControl", uac.ToString()));
        connection.SendRequest(request);

        if (!string.IsNullOrEmpty(password))
            SetPassword(connection, dn, password);

        if (user.Enabled)
        {
            var enable = new ModifyRequest(dn, DirectoryAttributeOperation.Replace, "userAccountControl",
                (uac & ~AdsUfAccountDisable).ToString());
            connection.SendRequest(enable);
        }
        user.Ou = dn;
    }

    public void SetUserEnabled(DomainControllerSettings dc, string distinguishedName, bool enabled)
    {
        using var connection = Bind(dc);
        var uac = ReadUac(connection, distinguishedName);
        var next = enabled ? uac & ~AdsUfAccountDisable : uac | AdsUfAccountDisable;
        connection.SendRequest(new ModifyRequest(distinguishedName, DirectoryAttributeOperation.Replace, "userAccountControl", next.ToString()));
    }

    public void UnlockUser(DomainControllerSettings dc, string distinguishedName)
    {
        using var connection = Bind(dc);
        connection.SendRequest(new ModifyRequest(distinguishedName, DirectoryAttributeOperation.Replace, "lockoutTime", "0"));
    }

    public void ResetPassword(DomainControllerSettings dc, string distinguishedName, string password)
    {
        using var connection = Bind(dc);
        SetPassword(connection, distinguishedName, password);
        connection.SendRequest(new ModifyRequest(distinguishedName, DirectoryAttributeOperation.Replace, "pwdLastSet", "0"));
    }

    public void DeleteObject(DomainControllerSettings dc, string distinguishedName)
    {
        using var connection = Bind(dc);
        connection.SendRequest(new DeleteRequest(distinguishedName));
    }

    public void CreateComputer(DomainControllerSettings dc, DirectoryComputer computer)
    {
        using var connection = Bind(dc);
        var parent = string.IsNullOrWhiteSpace(computer.Ou) ? $"CN=Computers,{ResolveBase(dc)}" : ParentDn(computer.Ou);
        var dn = $"CN={EscapeCn(computer.Name)},{parent}";
        var request = new AddRequest(dn);
        request.Attributes.Add(new DirectoryAttribute("objectClass", "top", "person", "organizationalPerson", "user", "computer"));
        request.Attributes.Add(new DirectoryAttribute("sAMAccountName", computer.Name.EndsWith("$") ? computer.Name : computer.Name + "$"));
        request.Attributes.Add(new DirectoryAttribute("userAccountControl", (AdsUfWorkstationTrust | AdsUfAccountDisable).ToString()));
        if (!string.IsNullOrWhiteSpace(computer.DnsHostName))
            request.Attributes.Add(new DirectoryAttribute("dNSHostName", computer.DnsHostName));
        if (!string.IsNullOrWhiteSpace(computer.Description))
            request.Attributes.Add(new DirectoryAttribute("description", computer.Description));
        connection.SendRequest(request);
        if (computer.Enabled)
        {
            connection.SendRequest(new ModifyRequest(dn, DirectoryAttributeOperation.Replace, "userAccountControl", AdsUfWorkstationTrust.ToString()));
        }
        computer.Ou = dn;
    }

    public void CreateGroup(DomainControllerSettings dc, DirectoryGroup group)
    {
        using var connection = Bind(dc);
        var parent = string.IsNullOrWhiteSpace(group.Ou) ? $"CN=Users,{ResolveBase(dc)}" : ParentDn(group.Ou);
        var dn = $"CN={EscapeCn(group.Name)},{parent}";
        var groupType = group.Scope switch
        {
            "universal" => AdsGroupTypeUniversal,
            "domainLocal" => AdsGroupTypeDomainLocal,
            _ => AdsGroupTypeGlobal
        };
        if (group.Type != "distribution")
            groupType |= AdsGroupTypeSecurity;
        var request = new AddRequest(dn);
        request.Attributes.Add(new DirectoryAttribute("objectClass", "top", "group"));
        request.Attributes.Add(new DirectoryAttribute("sAMAccountName", string.IsNullOrWhiteSpace(group.SamAccountName) ? group.Name : group.SamAccountName));
        request.Attributes.Add(new DirectoryAttribute("groupType", groupType.ToString()));
        if (!string.IsNullOrWhiteSpace(group.Description))
            request.Attributes.Add(new DirectoryAttribute("description", group.Description));
        if (!string.IsNullOrWhiteSpace(group.Mail))
            request.Attributes.Add(new DirectoryAttribute("mail", group.Mail));
        connection.SendRequest(request);
        group.Ou = dn;
    }

    public void AddGroupMember(DomainControllerSettings dc, string groupDn, string memberDn)
    {
        using var connection = Bind(dc);
        connection.SendRequest(new ModifyRequest(groupDn, DirectoryAttributeOperation.Add, "member", memberDn));
    }

    public void RemoveGroupMember(DomainControllerSettings dc, string groupDn, string memberDn)
    {
        using var connection = Bind(dc);
        connection.SendRequest(new ModifyRequest(groupDn, DirectoryAttributeOperation.Delete, "member", memberDn));
    }

    public string? ReadMsLapsPassword(DomainControllerSettings dc, string computerDn)
    {
        using var connection = Bind(dc);
        var request = new SearchRequest(computerDn, "(objectClass=computer)", SearchScope.Base,
            "msLAPS-Password", "ms-Mcs-AdmPwd", "msLAPS-PasswordExpirationTime", "ms-Mcs-AdmPwdExpirationTime");
        var response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count == 0) return null;
        var entry = response.Entries[0];
        return Attr(entry, "msLAPS-Password", Attr(entry, "ms-Mcs-AdmPwd"));
    }

    private LdapConnection Bind(DomainControllerSettings dc)
    {
        DirectoryConnection.Normalize(dc);
        var port = dc.Port <= 0 ? (dc.UseSsl ? 636 : 389) : dc.Port;
        var identifier = new LdapDirectoryIdentifier(dc.Host, port, false, false);
        var user = dc.BindDn;
        var domain = dc.Domain;
        NetworkCredential credential;
        if (user.Contains('\\') && user.Split('\\').Length == 2)
        {
            var parts = user.Split('\\', 2);
            credential = new NetworkCredential(parts[1], dc.Password ?? "", parts[0]);
        }
        else if (user.Contains('@'))
        {
            credential = new NetworkCredential(user, dc.Password ?? "");
        }
        else
        {
            credential = new NetworkCredential(user, dc.Password ?? "", domain);
        }
        var connection = new LdapConnection(identifier, credential, AuthType.Basic)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = dc.UseSsl;
        if (dc.UseSsl)
            connection.SessionOptions.VerifyServerCertificate += (_, _) => true;
        try
        {
            connection.Bind();
        }
        catch (LdapException ex)
        {
            _log.Write("error", "ldap", $"LdapException resultCode={ex.ErrorCode} server={ex.ServerErrorMessage}", DirectoryConnection.Describe(dc), ex);
            throw new InvalidOperationException($"LDAP bind to {dc.Host}:{port} as {dc.BindDn} failed: {ex.Message} (code {ex.ErrorCode}{(string.IsNullOrEmpty(ex.ServerErrorMessage) ? "" : $", {ex.ServerErrorMessage}")})", ex);
        }
        catch (Exception ex)
        {
            _log.Write("error", "ldap", $"Bind threw {ex.GetType().Name}", DirectoryConnection.Describe(dc), ex);
            throw;
        }
        return connection;
    }

    private static void SetPassword(LdapConnection connection, string dn, string password)
    {
        var quoted = Encoding.Unicode.GetBytes($"\"{password}\"");
        var attr = new DirectoryAttributeModification
        {
            Name = "unicodePwd",
            Operation = DirectoryAttributeOperation.Replace
        };
        attr.Add(quoted);
        var modify = new ModifyRequest(dn);
        modify.Modifications.Add(attr);
        connection.SendRequest(modify);
    }

    private static int ReadUac(LdapConnection connection, string dn)
    {
        var request = new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, "userAccountControl");
        var response = (SearchResponse)connection.SendRequest(request);
        return ParseInt(Attr(response.Entries[0], "userAccountControl"));
    }

    private static string ResolveBase(DomainControllerSettings dc)
    {
        if (!string.IsNullOrWhiteSpace(dc.BaseDn)) return dc.BaseDn;
        var bind = dc.BindDn ?? "";
        var idx = bind.IndexOf("DC=", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? bind[idx..] : bind;
    }

    private static string ParentDn(string dn)
    {
        var idx = dn.IndexOf(',');
        return idx > 0 && dn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? dn[(idx + 1)..] : dn;
    }

    private static string EscapeCn(string name) => name.Replace(",", "\\,");

    private static string Attr(SearchResultEntry entry, string name, string fallback = "")
    {
        if (!entry.Attributes.Contains(name) || entry.Attributes[name].Count == 0) return fallback;
        return entry.Attributes[name][0]?.ToString() ?? fallback;
    }

    private static IEnumerable<string> Attrs(SearchResultEntry entry, string name)
    {
        if (!entry.Attributes.Contains(name)) yield break;
        foreach (var v in entry.Attributes[name].GetValues(typeof(string)))
            yield return v?.ToString() ?? "";
    }

    private static int ParseInt(string value) => int.TryParse(value, out var n) ? n : 0;

    public static string RandomPassword(int length = 16)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(chars[bytes[i] % chars.Length]);
        return sb.ToString();
    }
}
