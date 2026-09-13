using Admplus.Api.Models;

namespace Admplus.Api.Services;

public static class DirectoryConnection
{
    public static void Apply(DomainControllerSettings cur, DomainControllerSettings body)
    {
        if (body.Host != null) cur.Host = body.Host.Trim();
        if (body.Port != 0) cur.Port = body.Port;
        cur.UseSsl = body.UseSsl;
        if (body.BindDn != null) cur.BindDn = body.BindDn.Trim();
        if (!string.IsNullOrEmpty(body.Password) && body.Password != "********")
            cur.Password = body.Password;
        if (body.BaseDn != null) cur.BaseDn = body.BaseDn.Trim();
        if (body.Domain != null) cur.Domain = body.Domain.Trim();
        Normalize(cur);
    }

    public static void Normalize(DomainControllerSettings dc)
    {
        dc.Host = (dc.Host ?? "").Trim();
        dc.BindDn = (dc.BindDn ?? "").Trim();
        dc.BaseDn = (dc.BaseDn ?? "").Trim();
        dc.Domain = (dc.Domain ?? "").Trim();

        if (string.IsNullOrWhiteSpace(dc.Host) && !string.IsNullOrWhiteSpace(dc.Domain))
            dc.Host = dc.Domain;

        if (string.IsNullOrWhiteSpace(dc.BaseDn) && !string.IsNullOrWhiteSpace(dc.Domain))
            dc.BaseDn = ToBaseDn(dc.Domain);

        if (string.IsNullOrWhiteSpace(dc.Domain) && !string.IsNullOrWhiteSpace(dc.BaseDn))
            dc.Domain = FromBaseDn(dc.BaseDn);

        if (dc.Port <= 0)
            dc.Port = dc.UseSsl ? 636 : 389;

        if (!string.IsNullOrWhiteSpace(dc.BindDn) &&
            !dc.BindDn.Contains('=', StringComparison.Ordinal) &&
            !dc.BindDn.Contains('@') &&
            !dc.BindDn.Contains('\\') &&
            !string.IsNullOrWhiteSpace(dc.Domain))
        {
            dc.BindDn = $"{dc.BindDn}@{dc.Domain}";
        }
    }

    public static string Describe(DomainControllerSettings dc) =>
        $"host={dc.Host} port={dc.Port} ssl={dc.UseSsl} bindDn={dc.BindDn} baseDn={dc.BaseDn} domain={dc.Domain} password={(string.IsNullOrEmpty(dc.Password) ? "empty" : "set")}";

    public static string ToBaseDn(string domain) =>
        string.Join(",", domain.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => "DC=" + p));

    public static string FromBaseDn(string baseDn) =>
        string.Join(".", baseDn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]));
}
