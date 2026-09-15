using Admplus.Api.Models;

namespace Admplus.Api.Services;

public static class DirectoryConnection
{
    public static bool HasConnectionFields(DomainControllerSettings? body) =>
        body != null && (
            !string.IsNullOrWhiteSpace(body.Host) ||
            !string.IsNullOrWhiteSpace(body.BindDn) ||
            !string.IsNullOrWhiteSpace(body.Domain) ||
            !string.IsNullOrWhiteSpace(body.BaseDn) ||
            (!string.IsNullOrEmpty(body.Password) && body.Password != "********"));

    public static void Apply(DomainControllerSettings cur, DomainControllerSettings body)
    {
        if (!string.IsNullOrWhiteSpace(body.Host)) cur.Host = body.Host.Trim();
        if (body.Port != 0) cur.Port = body.Port;
        cur.UseSsl = body.UseSsl;
        if (!string.IsNullOrWhiteSpace(body.BindDn)) cur.BindDn = body.BindDn.Trim();
        if (!string.IsNullOrEmpty(body.Password) && body.Password != "********")
            cur.Password = body.Password;
        if (!string.IsNullOrWhiteSpace(body.BaseDn)) cur.BaseDn = body.BaseDn.Trim();
        if (!string.IsNullOrWhiteSpace(body.Domain)) cur.Domain = body.Domain.Trim();
        if (body.SearchPageSize > 0) cur.SearchPageSize = Math.Clamp(body.SearchPageSize, 100, 5000);
        Normalize(cur);
    }

    public static void Normalize(DomainControllerSettings dc)
    {
        dc.Host = (dc.Host ?? "").Trim();
        dc.BindDn = (dc.BindDn ?? "").Trim();
        dc.BaseDn = (dc.BaseDn ?? "").Trim();
        dc.Domain = (dc.Domain ?? "").Trim();
        if (dc.Password == "********") dc.Password = "";

        if (string.IsNullOrWhiteSpace(dc.Host) && !string.IsNullOrWhiteSpace(dc.Domain))
            dc.Host = dc.Domain;

        if (string.IsNullOrWhiteSpace(dc.BaseDn) && !string.IsNullOrWhiteSpace(dc.Domain))
            dc.BaseDn = ToBaseDn(dc.Domain);

        if (string.IsNullOrWhiteSpace(dc.Domain) && !string.IsNullOrWhiteSpace(dc.BaseDn))
            dc.Domain = FromBaseDn(dc.BaseDn);

        if (dc.Port <= 0)
            dc.Port = dc.UseSsl ? 636 : 389;

        if (dc.SearchPageSize <= 0)
            dc.SearchPageSize = 1000;
        dc.SearchPageSize = Math.Clamp(dc.SearchPageSize, 100, 5000);

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
