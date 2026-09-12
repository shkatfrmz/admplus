using Admplus.Api.Models;
using Microsoft.Identity.Client;

namespace Admplus.Api.Services;

public class AzureAdClient
{
    public async Task TestConnectionAsync(AzureAdSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.TenantId) ||
            string.IsNullOrWhiteSpace(settings.ClientId) ||
            string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            throw new InvalidOperationException("Tenant ID, client ID, and client secret are required");
        }

        var app = ConfidentialClientApplicationBuilder
            .Create(settings.ClientId)
            .WithClientSecret(settings.ClientSecret)
            .WithAuthority($"https://login.microsoftonline.com/{settings.TenantId}")
            .Build();

        await app.AcquireTokenForClient(new[] { "https://graph.microsoft.com/.default" })
            .ExecuteAsync(ct);
    }
}
