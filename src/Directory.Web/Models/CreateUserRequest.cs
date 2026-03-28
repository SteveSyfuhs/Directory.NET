using System.ComponentModel.DataAnnotations;

namespace Directory.Web.Models;

public record CreateUserRequest(
    [Required][MaxLength(ValidationHelper.MaxDnLength)] string ContainerDn,
    [Required][MaxLength(64)] string Cn,
    [Required][MaxLength(20)] string SAMAccountName,
    [MaxLength(ValidationHelper.MaxStringLength)] string UserPrincipalName,
    [MaxLength(ValidationHelper.MaxPasswordLength)] string Password,
    [MaxLength(ValidationHelper.MaxStringLength)] string GivenName,
    [MaxLength(ValidationHelper.MaxStringLength)] string Sn,
    [MaxLength(ValidationHelper.MaxStringLength)] string DisplayName,
    [MaxLength(ValidationHelper.MaxDescriptionLength)] string Description,
    [MaxLength(ValidationHelper.MaxStringLength)] string Mail,
    [MaxLength(ValidationHelper.MaxStringLength)] string Title,
    [MaxLength(ValidationHelper.MaxStringLength)] string Department,
    [MaxLength(ValidationHelper.MaxStringLength)] string Company,
    bool Enabled = true
);
