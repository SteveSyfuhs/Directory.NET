using System.ComponentModel.DataAnnotations;

namespace Directory.Web.Models;

public record ResetPasswordRequest(
    [Required][MaxLength(ValidationHelper.MaxPasswordLength)] string Password,
    bool MustChangeAtNextLogon = false
);
