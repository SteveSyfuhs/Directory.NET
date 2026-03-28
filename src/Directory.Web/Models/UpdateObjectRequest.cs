using System.ComponentModel.DataAnnotations;

namespace Directory.Web.Models;

public record UpdateObjectRequest(
    [Required] Dictionary<string, object> Attributes,
    string ETag
);
