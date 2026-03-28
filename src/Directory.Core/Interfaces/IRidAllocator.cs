namespace Directory.Core.Interfaces;

public interface IRidAllocator
{
    Task<long> AllocateRidAsync(string tenantId, string domainDn, CancellationToken ct = default);
    Task<string> GenerateObjectSidAsync(string tenantId, string domainDn, CancellationToken ct = default);
    string GetDomainSid(string tenantId, string domainDn);
}
