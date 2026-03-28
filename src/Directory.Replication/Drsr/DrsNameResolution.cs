using Directory.Core.Interfaces;
using Directory.Core.Models;
using Directory.Rpc.Drsr;
using Microsoft.Extensions.Logging;

namespace Directory.Replication.Drsr;

/// <summary>
/// Implements DS_NAME_FORMAT conversion for IDL_DRSCrackNames per [MS-DRSR] section 4.1.4.
/// Delegates to <see cref="DsCrackNamesService"/> for the actual name resolution logic,
/// and adapts results to the DRS wire types.
/// </summary>
public class DrsNameResolution
{
    private readonly DsCrackNamesService _crackNamesService;
    private readonly ILogger<DrsNameResolution> _logger;

    public DrsNameResolution(DsCrackNamesService crackNamesService, ILogger<DrsNameResolution> logger)
    {
        _crackNamesService = crackNamesService;
        _logger = logger;
    }

    /// <summary>
    /// Processes an IDL_DRSCrackNames request, resolving each name from the offered
    /// format to the desired format. Adapts the DRS wire types to the core service.
    /// </summary>
    public async Task<DRS_MSG_CRACKREPLY_V1> CrackNamesAsync(
        DRS_MSG_CRACKREQ_V1 request,
        string tenantId,
        string domainDn,
        CancellationToken ct = default)
    {
        var names = request.RpNames.Take((int)request.CNames).ToArray();

        var crackResults = await _crackNamesService.CrackNamesAsync(
            tenantId,
            (uint)request.FormatOffered,
            (uint)request.FormatDesired,
            (uint)request.DwFlags,
            names,
            ct);

        var result = new DS_NAME_RESULTW
        {
            CItems = (uint)crackResults.Length,
            RItems = crackResults.Select(r => new DS_NAME_RESULT_ITEMW
            {
                Status = (DsNameStatus)(uint)r.Status,
                PDomain = r.DnsDomainName,
                PName = r.Name,
            }).ToList(),
        };

        return new DRS_MSG_CRACKREPLY_V1 { PResult = result };
    }
}
