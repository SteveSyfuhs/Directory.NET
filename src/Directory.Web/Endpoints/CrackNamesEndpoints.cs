using Directory.Core;
using Directory.Rpc.Drsr;
using Microsoft.AspNetCore.Mvc;

namespace Directory.Web.Endpoints;

public static class CrackNamesEndpoints
{
    public static RouteGroupBuilder MapCrackNamesEndpoints(this RouteGroupBuilder group)
    {
        // Translates Active Directory names between different formats (DsCrackNames).
        //
        // Format codes:
        //   0 = DS_UNKNOWN_NAME (auto-detect input format)
        //   1 = DS_FQDN_1779_NAME (DN, e.g., "CN=John,CN=Users,DC=corp,DC=com")
        //   2 = DS_NT4_ACCOUNT_NAME (e.g., "CORP\jdoe")
        //   3 = DS_DISPLAY_NAME (e.g., "John Doe")
        //   6 = DS_UNIQUE_ID_NAME (e.g., "{guid}")
        //   7 = DS_CANONICAL_NAME (e.g., "corp.com/Users/John Doe")
        //   8 = DS_USER_PRINCIPAL_NAME (e.g., "jdoe@corp.com")
        //   9 = DS_CANONICAL_NAME_EX (e.g., "corp.com/Users\nJohn Doe")
        //  10 = DS_SERVICE_PRINCIPAL_NAME (e.g., "HTTP/web.corp.com")
        //  11 = DS_SID_OR_SID_HISTORY_NAME (e.g., "S-1-5-21-...")
        //  12 = DS_DNS_DOMAIN_NAME (e.g., "corp.com")
        //
        // Flags:
        //   0 = default (perform directory lookups)
        //   1 = DS_NAME_FLAG_SYNTACTICAL_ONLY (no directory lookup, format conversion only)
        group.MapPost("/", async (
            [FromBody] CrackNamesRequest request,
            DsCrackNamesService crackNamesService,
            CancellationToken ct) =>
        {
            if (request.Names == null || request.Names.Length == 0)
                return Results.Problem(statusCode: 400, detail: "At least one name is required.");

            var results = await crackNamesService.CrackNamesAsync(
                DirectoryConstants.DefaultTenantId,
                request.FormatOffered,
                request.FormatDesired,
                request.Flags,
                request.Names,
                ct);

            var response = new CrackNamesResponse
            {
                Results = results.Select(r => new CrackNamesResponseItem
                {
                    Status = (uint)r.Status,
                    StatusDescription = r.Status.ToString(),
                    Name = r.Name,
                    DnsDomainName = r.DnsDomainName,
                }).ToArray(),
            };

            return Results.Ok(response);
        })
        .WithName("CrackNames")
        .WithTags("Name Resolution");

        return group;
    }
}

public class CrackNamesRequest
{
    /// <summary>
    /// Format of the input names (0 = auto-detect).
    /// </summary>
    public uint FormatOffered { get; set; }

    /// <summary>
    /// Desired output format.
    /// </summary>
    public uint FormatDesired { get; set; }

    /// <summary>
    /// Flags controlling behavior (1 = syntactical only).
    /// </summary>
    public uint Flags { get; set; }

    /// <summary>
    /// The names to translate.
    /// </summary>
    public string[] Names { get; set; } = [];
}

public class CrackNamesResponse
{
    public CrackNamesResponseItem[] Results { get; set; } = [];
}

public class CrackNamesResponseItem
{
    public uint Status { get; set; }
    public string StatusDescription { get; set; }
    public string Name { get; set; }
    public string DnsDomainName { get; set; }
}
