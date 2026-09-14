using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        if (string.IsNullOrWhiteSpace(dc.Password) || dc.Password == "********") missing.Add("password");
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

    private List<SearchResultEntry> Search(DomainControllerSettings dc, string baseDn, string filter, SearchScope scope, params string[] attributes)
    {
        using var connection = Bind(dc);
        var results = new List<SearchResultEntry>();
        var request = new SearchRequest(baseDn, filter, scope, attributes);
        var pageRequest = new PageResultRequestControl(500);
        request.Controls.Add(pageRequest);
        var pages = 0;
        while (true)
        {
            var response = (SearchResponse)connection.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries) results.Add(entry);
            pages++;
            var pageResponse = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
            if (pageResponse?.Cookie is null || pageResponse.Cookie.Length == 0) break;
            pageRequest.Cookie = pageResponse.Cookie;
        }
        _log.Write("debug", "ldap", $"Search {scope} '{baseDn}' {filter} -> {results.Count} entries ({pages} page(s))");
        return results;
    }

    public List<DirectoryUser> SearchUsers(DomainControllerSettings dc)
    {
        var baseDn = ResolveBase(dc);
        var entries = Search(dc, baseDn,
            "(&(|(objectClass=user)(objectClass=inetOrgPerson))(!(objectClass=computer)))",
            SearchScope.Subtree,
            "sAMAccountName", "displayName", "givenName", "sn", "userPrincipalName", "mail",
            "department", "title", "physicalDeliveryOfficeName", "telephoneNumber", "manager",
            "userAccountControl", "distinguishedName", "objectClass", "pwdLastSet", "lockoutTime",
            "whenCreated", "lastLogonTimestamp");

        var users = new List<DirectoryUser>();
        foreach (var entry in entries)
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
                Manager = Attr(entry, "manager"),
                Ou = Attr(entry, "distinguishedName"),
                Source = "ad-live",
                Created = ParseGeneralized(Attr(entry, "whenCreated")),
                LastLogon = ParseFileTime(Attr(entry, "lastLogonTimestamp"))
            });
        }
        _log.Write("info", "ldap", $"SearchUsers returned {users.Count} users");
        return users;
    }

    public List<DirectoryComputer> SearchComputers(DomainControllerSettings dc)
    {
        var baseDn = ResolveBase(dc);
        var entries = Search(dc, baseDn, "(objectClass=computer)", SearchScope.Subtree,
            "sAMAccountName", "name", "dNSHostName", "operatingSystem", "operatingSystemVersion",
            "userAccountControl", "distinguishedName", "description", "managedBy",
            "servicePrincipalName", "whenCreated", "lastLogonTimestamp");

        var computers = new List<DirectoryComputer>();
        foreach (var entry in entries)
        {
            var sam = Attr(entry, "sAMAccountName", Attr(entry, "name")).TrimEnd('$');
            if (string.IsNullOrEmpty(sam)) continue;
            var uac = ParseInt(Attr(entry, "userAccountControl"));
            var os = Attr(entry, "operatingSystem");
            var type = os.Contains("Server", StringComparison.OrdinalIgnoreCase) ? "server" : "workstation";
            if ((uac & AdsUfServerTrust) != 0) type = "server";
            if ((uac & AdsUfWorkstationTrust) != 0 && !string.Equals(type, "server", StringComparison.OrdinalIgnoreCase) && os.Length == 0)
                type = "workstation";

            computers.Add(new DirectoryComputer
            {
                Name = sam,
                DnsHostName = Attr(entry, "dNSHostName"),
                Os = os,
                OsVersion = Attr(entry, "operatingSystemVersion"),
                Type = type,
                Enabled = (uac & AdsUfAccountDisable) == 0,
                Ou = Attr(entry, "distinguishedName"),
                Description = Attr(entry, "description"),
                ManagedBy = Attr(entry, "managedBy"),
                ServicePrincipalNames = Attrs(entry, "servicePrincipalName").ToList(),
                LastLogon = ParseFileTime(Attr(entry, "lastLogonTimestamp")),
                Created = ParseGeneralized(Attr(entry, "whenCreated")),
                Source = "ad-live"
            });
        }
        _log.Write("info", "ldap", $"SearchComputers returned {computers.Count} computers");
        return computers;
    }

    public List<DirectoryGroup> SearchGroups(DomainControllerSettings dc)
    {
        var baseDn = ResolveBase(dc);
        var entries = Search(dc, baseDn, "(objectClass=group)", SearchScope.Subtree,
            "sAMAccountName", "name", "displayName", "description", "groupType",
            "distinguishedName", "mail", "member", "whenCreated");

        var groups = new List<DirectoryGroup>();
        foreach (var entry in entries)
        {
            var sam = Attr(entry, "sAMAccountName", Attr(entry, "name"));
            if (string.IsNullOrEmpty(sam)) continue;
            var gt = ParseInt(Attr(entry, "groupType"));
            var security = (gt & AdsGroupTypeSecurity) != 0;
            var scope = (gt & AdsGroupTypeUniversal) != 0 ? "universal"
                : (gt & AdsGroupTypeDomainLocal) != 0 ? "domainLocal" : "global";

            groups.Add(new DirectoryGroup
            {
                Name = Attr(entry, "displayName", Attr(entry, "name", sam)),
                SamAccountName = sam,
                Type = security ? "security" : "distribution",
                Scope = scope,
                Description = Attr(entry, "description"),
                Ou = Attr(entry, "distinguishedName"),
                Mail = Attr(entry, "mail"),
                Members = Attrs(entry, "member").ToList(),
                Created = ParseGeneralized(Attr(entry, "whenCreated")),
                Source = "ad-live"
            });
        }
        _log.Write("info", "ldap", $"SearchGroups returned {groups.Count} groups");
        return groups;
    }

    public List<OrganizationalUnit> SearchOus(DomainControllerSettings dc)
    {
        var baseDn = ResolveBase(dc);
        var entries = Search(dc, baseDn, "(objectClass=organizationalUnit)", SearchScope.Subtree,
            "ou", "name", "distinguishedName", "description", "whenCreated");

        var ous = new List<OrganizationalUnit>();
        foreach (var entry in entries)
        {
            var dn = Attr(entry, "distinguishedName");
            if (string.IsNullOrEmpty(dn)) continue;
            ous.Add(new OrganizationalUnit
            {
                Id = "ou-ad-" + Guid.NewGuid().ToString("n")[..8],
                Name = Attr(entry, "ou", Attr(entry, "name")),
                Dn = dn,
                Description = Attr(entry, "description"),
                Created = ParseGeneralized(Attr(entry, "whenCreated")),
                Source = "ad-live"
            });
        }
        _log.Write("info", "ldap", $"SearchOus returned {ous.Count} OUs");
        return ous;
    }

    public List<DirectoryGpo> SearchGpos(DomainControllerSettings dc)
    {
        var baseDn = ResolveBase(dc);
        var policiesDn = $"CN=Policies,CN=System,{baseDn}";
        List<SearchResultEntry> entries;
        try
        {
            entries = Search(dc, policiesDn, "(objectClass=groupPolicyContainer)", SearchScope.OneLevel,
                "displayName", "name", "cn", "flags", "gPCFileSysPath", "versionNumber", "distinguishedName", "description");
        }
        catch (Exception ex)
        {
            _log.Write("warn", "ldap", $"GPO container query failed at {policiesDn}", null, ex);
            entries = new List<SearchResultEntry>();
        }

        var gpos = new List<DirectoryGpo>();
        foreach (var entry in entries)
        {
            var dn = Attr(entry, "distinguishedName");
            var guid = CnFromDn(dn);
            if (string.IsNullOrEmpty(guid)) continue;
            var flags = ParseInt(Attr(entry, "flags"));
            var status = flags switch { 0 => "enabled", 1 => "userDisabled", 2 => "computerDisabled", _ => "disabled" };
            gpos.Add(new DirectoryGpo
            {
                Id = "gpo-" + guid.Trim('{', '}').ToLowerInvariant(),
                Name = Attr(entry, "displayName", guid),
                Status = status,
                Description = Attr(entry, "description"),
                Created = DateTimeOffset.UtcNow,
                Modified = DateTimeOffset.UtcNow,
                LinkedOus = new List<string>(),
                Settings = new Dictionary<string, object>
                {
                    ["guid"] = guid,
                    ["versionNumber"] = Attr(entry, "versionNumber"),
                    ["gPCFileSysPath"] = Attr(entry, "gPCFileSysPath")
                },
                Source = "ad-live"
            });
        }
        ApplyGpoLinks(dc, baseDn, gpos);
        _log.Write("info", "ldap", $"SearchGpos returned {gpos.Count} GPOs");
        return gpos;
    }

    private void ApplyGpoLinks(DomainControllerSettings dc, string baseDn, List<DirectoryGpo> gpos)
    {
        var byGuid = new Dictionary<string, DirectoryGpo>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in gpos)
            if (g.Settings.TryGetValue("guid", out var v) && v is string guid)
                byGuid[guid.Trim('{', '}')] = g;

        List<SearchResultEntry> linked;
        try
        {
            linked = Search(dc, baseDn, "(gPLink=*)", SearchScope.Subtree, "distinguishedName", "gPLink");
        }
        catch (Exception ex)
        {
            _log.Write("warn", "ldap", "GPO link enumeration failed", null, ex);
            return;
        }

        foreach (var entry in linked)
        {
            var containerDn = Attr(entry, "distinguishedName");
            foreach (var link in Attrs(entry, "gPLink"))
            {
                foreach (Match m in Regex.Matches(link, @"LDAP://CN=\{(?<g>[0-9a-fA-F\-]+)\},[^\];]*(?:;(?<opt>\d+))?", RegexOptions.IgnoreCase))
                {
                    if (!byGuid.TryGetValue(m.Groups["g"].Value, out var gpo)) continue;
                    var options = int.TryParse(m.Groups["opt"].Value, out var o) ? o : 0;
                    if (!gpo.LinkedOus.Contains(containerDn, StringComparer.OrdinalIgnoreCase))
                        gpo.LinkedOus.Add(containerDn);
                    if ((options & 2) != 0) gpo.Enforced = true;
                    if ((options & 1) != 0 && gpo.Status == "enabled") gpo.Status = "disabled";
                }
            }
        }
    }

    private static DateTimeOffset ParseGeneralized(string value)
    {
        if (DateTimeOffset.TryParseExact(value, "yyyyMMddHHmmss.0Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
            return exact;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var loose))
            return loose;
        return DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? ParseFileTime(string value)
    {
        if (!long.TryParse(value, out var ticks) || ticks <= 0) return null;
        try { return DateTimeOffset.FromFileTime(ticks); }
        catch { return null; }
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
        var attempts = BindAttempts(dc);
        Exception? last = null;
        foreach (var attempt in attempts)
        {
            var connection = new LdapConnection(identifier)
            {
                Timeout = TimeSpan.FromSeconds(12),
                AuthType = attempt.Auth
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = dc.UseSsl;
            if (dc.UseSsl)
                connection.SessionOptions.VerifyServerCertificate += (_, _) => true;
            try
            {
                _log.Write("info", "ldap", $"Bind attempt auth={attempt.Auth} user={attempt.User} domain={attempt.Domain ?? ""}");
                connection.Credential = new NetworkCredential(attempt.User, dc.Password ?? "", attempt.Domain ?? "");
                connection.Bind();
                _log.Write("info", "ldap", $"Bind ok with auth={attempt.Auth} user={attempt.User}");
                return connection;
            }
            catch (LdapException ex)
            {
                last = ex;
                _log.Write("warn", "ldap", $"Bind attempt failed auth={attempt.Auth} user={attempt.User} code={ex.ErrorCode} server={ex.ServerErrorMessage}", null, ex);
                connection.Dispose();
            }
            catch (Exception ex)
            {
                last = ex;
                _log.Write("warn", "ldap", $"Bind attempt threw {ex.GetType().Name} auth={attempt.Auth} user={attempt.User}", null, ex);
                connection.Dispose();
            }
        }

        var ldap = last as LdapException;
        var hint = ExplainLdap(ldap);
        var detail = ldap is null
            ? last?.Message ?? "bind failed"
            : $"{ldap.Message} (code {ldap.ErrorCode}{(string.IsNullOrEmpty(ldap.ServerErrorMessage) ? "" : $", {ldap.ServerErrorMessage}")})";
        _log.Write("error", "ldap", $"All bind attempts failed to {dc.Host}:{port} as {dc.BindDn}", DirectoryConnection.Describe(dc), last);
        throw new InvalidOperationException($"LDAP bind to {dc.Host}:{port} as {dc.BindDn} failed: {detail}.{hint}", last);
    }

    private static List<(AuthType Auth, string User, string? Domain)> BindAttempts(DomainControllerSettings dc)
    {
        var user = dc.BindDn;
        var domainNetbios = Netbios(dc.Domain);
        var attempts = new List<(AuthType, string, string?)>();

        void add(AuthType auth, string name, string? domain)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (attempts.Any(a => a.Item1 == auth && a.Item2 == name && a.Item3 == domain)) return;
            attempts.Add((auth, name, domain));
        }

        if (user.Contains('=', StringComparison.Ordinal))
        {
            add(AuthType.Basic, user, "");
            var cn = CnFromDn(user);
            if (!string.IsNullOrEmpty(cn) && !string.IsNullOrEmpty(domainNetbios))
            {
                add(AuthType.Basic, $"{domainNetbios}\\{cn}", "");
                add(AuthType.Negotiate, cn, domainNetbios);
            }
            if (!string.IsNullOrEmpty(cn) && !string.IsNullOrWhiteSpace(dc.Domain))
                add(AuthType.Basic, $"{cn}@{dc.Domain}", "");
        }
        else if (user.Contains('\\') && user.Split('\\').Length == 2)
        {
            var parts = user.Split('\\', 2);
            add(AuthType.Basic, user, "");
            add(AuthType.Negotiate, parts[1], parts[0]);
        }
        else if (user.Contains('@'))
        {
            add(AuthType.Basic, user, "");
            add(AuthType.Negotiate, user, "");
        }
        else
        {
            if (!string.IsNullOrEmpty(domainNetbios))
            {
                add(AuthType.Basic, $"{domainNetbios}\\{user}", "");
                add(AuthType.Negotiate, user, domainNetbios);
            }
            if (!string.IsNullOrWhiteSpace(dc.Domain))
                add(AuthType.Basic, $"{user}@{dc.Domain}", "");
            add(AuthType.Basic, user, "");
        }

        return attempts;
    }

    private static string ExplainLdap(LdapException? ex)
    {
        var server = ex?.ServerErrorMessage ?? "";
        if (server.Contains("data 52e", StringComparison.OrdinalIgnoreCase) || server.Contains("data 52E", StringComparison.OrdinalIgnoreCase))
            return " AD data 52e = username/password rejected. Re-type the real bind password (do not leave the masked ********). Bind DN can be CN=..., user@domain, or DOMAIN\\sam.";
        if (server.Contains("data 532", StringComparison.OrdinalIgnoreCase))
            return " AD data 532 = password expired.";
        if (server.Contains("data 533", StringComparison.OrdinalIgnoreCase))
            return " AD data 533 = account disabled.";
        if (server.Contains("data 701", StringComparison.OrdinalIgnoreCase))
            return " AD data 701 = account expired.";
        if (server.Contains("data 775", StringComparison.OrdinalIgnoreCase))
            return " AD data 775 = account locked.";
        if (server.Contains("data 525", StringComparison.OrdinalIgnoreCase))
            return " AD data 525 = user not found.";
        if (ex?.ErrorCode == 81)
            return " LDAP 81 = DC unreachable from this machine (DNS/firewall/port 389).";
        return "";
    }

    private static string CnFromDn(string dn)
    {
        var first = dn.Split(',')[0];
        var idx = first.IndexOf('=');
        return idx >= 0 ? first[(idx + 1)..].Trim() : "";
    }

    private static string Netbios(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return "";
        var i = domain.IndexOf('.');
        return (i > 0 ? domain[..i] : domain).ToUpperInvariant();
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
