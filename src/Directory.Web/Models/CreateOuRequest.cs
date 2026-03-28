using System.ComponentModel.DataAnnotations;

namespace Directory.Web.Models;

// Note: the canonical definition used by OuEndpoints is in OuEndpoints.cs
public record CreateOuRequest(
    [Required][MaxLength(ValidationHelper.MaxDnLength)] string ParentDn,
    [Required][MaxLength(ValidationHelper.MaxStringLength)] string Name,
    [MaxLength(ValidationHelper.MaxDescriptionLength)] string Description
);
