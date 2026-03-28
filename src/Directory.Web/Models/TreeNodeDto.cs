namespace Directory.Web.Models;

public record TreeNodeDto(
    string Dn,
    string Name,
    string ObjectClass,
    string ObjectGuid,
    bool HasChildren,
    string Icon
);
