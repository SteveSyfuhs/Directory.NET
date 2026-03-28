namespace Directory.Web.Models;

public record PasswordPolicyDto(
    int MinPwdLength,
    int PwdHistoryLength,
    long MaxPwdAge,
    long MinPwdAge,
    int PwdProperties,
    int LockoutThreshold,
    long LockoutDuration,
    long LockoutObservationWindow
);
