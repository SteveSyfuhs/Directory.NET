using System.ComponentModel.DataAnnotations;

namespace Directory.Web.Models;

public record CreateGroupRequest(
    [Required][MaxLength(ValidationHelper.MaxDnLength)] string ContainerDn,
    [Required][MaxLength(ValidationHelper.MaxStringLength)] string Cn,
    [Required][MaxLength(ValidationHelper.MaxStringLength)] string SAMAccountName,
    [MaxLength(ValidationHelper.MaxDescriptionLength)] string Description,
    int GroupType = -2147483646  // Global Security
);
