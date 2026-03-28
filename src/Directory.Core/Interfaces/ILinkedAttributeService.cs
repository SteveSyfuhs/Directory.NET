using Directory.Core.Models;

namespace Directory.Core.Interfaces;

public interface ILinkedAttributeService
{
    LinkedAttributeDefinition GetLinkDefinition(string attributeName);
    bool IsForwardLink(string attributeName);
    bool IsBackLink(string attributeName);
    Task UpdateForwardLinkAsync(string tenantId, DirectoryObject source, string forwardLinkAttr, string targetDn, bool add, CancellationToken ct = default);
    IReadOnlyList<LinkedAttributeDefinition> GetAllLinks();
}
