namespace Directory.Security.Mfa;

public class MfaEnrollment
{
    public string DistinguishedName { get; set; } = "";
    public string Secret { get; set; } = ""; // Base32 encoded
    public bool IsEnabled { get; set; }
    public DateTimeOffset EnrolledAt { get; set; }
    public List<string> RecoveryCodes { get; set; } = []; // 8 one-time recovery codes
}

public class MfaStatus
{
    public bool IsEnabled { get; set; }
    public bool IsEnrolled { get; set; }
    public DateTimeOffset? EnrolledAt { get; set; }
    public int RecoveryCodesRemaining { get; set; }
}

public class MfaEnrollmentResult
{
    public string Secret { get; set; } = "";
    public string ProvisioningUri { get; set; } = "";
    public string AccountName { get; set; } = "";
}

public class MfaEnrollmentCompleteResult
{
    public bool Success { get; set; }
    public List<string> RecoveryCodes { get; set; } = [];
}

public class MfaValidationResult
{
    public bool IsValid { get; set; }
    public bool UsedRecoveryCode { get; set; }
}

public class MfaRecoveryCodesResult
{
    public List<string> RecoveryCodes { get; set; } = [];
}
