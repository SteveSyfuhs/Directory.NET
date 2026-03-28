using Directory.Core.Models;

namespace Directory.Core.Interfaces;

public interface IConstructedAttributeService
{
    bool IsConstructedAttribute(string attributeName);
    Task<DirectoryAttribute> ComputeAttributeAsync(string attributeName, DirectoryObject obj, string tenantId, CancellationToken ct = default);
    IReadOnlyList<string> GetConstructedAttributeNames();
}
