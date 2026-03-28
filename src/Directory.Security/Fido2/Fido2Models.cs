namespace Directory.Security.Fido2;

public class Fido2Credential
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserDn { get; set; } = "";
    public string CredentialId { get; set; } = "";  // Base64url
    public byte[] PublicKey { get; set; } = [];      // COSE public key
    public long SignCount { get; set; }
    public string DeviceName { get; set; } = "";     // User-friendly name
    public string AttestationType { get; set; } = "none"; // none, packed, tpm, android-key
    public List<string> Transports { get; set; } = new(); // usb, nfc, ble, internal
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class Fido2RegistrationData
{
    public List<Fido2Credential> Credentials { get; set; } = new();
    public Dictionary<string, Fido2Challenge> PendingChallenges { get; set; } = new();
}

public class Fido2Challenge
{
    public string Challenge { get; set; } = "";  // Base64url
    public DateTimeOffset CreatedAt { get; set; }
    public string Type { get; set; } = ""; // "registration" or "authentication"
}

/// <summary>
/// Options sent to the browser for navigator.credentials.create()
/// </summary>
public class PublicKeyCredentialCreationOptions
{
    public RpEntity Rp { get; set; } = new();
    public UserEntity User { get; set; } = new();
    public string Challenge { get; set; } = "";  // Base64url
    public List<PubKeyCredParam> PubKeyCredParams { get; set; } = new();
    public int Timeout { get; set; } = 60000;
    public string Attestation { get; set; } = "none";
    public List<PublicKeyCredentialDescriptor> ExcludeCredentials { get; set; } = new();
    public AuthenticatorSelection AuthenticatorSelection { get; set; } = new();
}

/// <summary>
/// Options sent to the browser for navigator.credentials.get()
/// </summary>
public class PublicKeyCredentialRequestOptions
{
    public string Challenge { get; set; } = "";  // Base64url
    public int Timeout { get; set; } = 60000;
    public string RpId { get; set; } = "";
    public List<PublicKeyCredentialDescriptor> AllowCredentials { get; set; } = new();
    public string UserVerification { get; set; } = "preferred";
}

public class RpEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class UserEntity
{
    public string Id { get; set; } = "";   // Base64url of user identifier
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class PubKeyCredParam
{
    public string Type { get; set; } = "public-key";
    public int Alg { get; set; }  // -7 = ES256, -257 = RS256
}

public class PublicKeyCredentialDescriptor
{
    public string Type { get; set; } = "public-key";
    public string Id { get; set; } = "";  // Base64url credential ID
    public List<string> Transports { get; set; }
}

public class AuthenticatorSelection
{
    public string AuthenticatorAttachment { get; set; } = "cross-platform";
    public bool RequireResidentKey { get; set; }
    public string ResidentKey { get; set; } = "discouraged";
    public string UserVerification { get; set; } = "preferred";
}

/// <summary>
/// Attestation response from the browser after navigator.credentials.create()
/// </summary>
public class AttestationResponse
{
    public string Id { get; set; } = "";           // Base64url credential ID
    public string RawId { get; set; } = "";        // Base64url raw ID
    public string Type { get; set; } = "public-key";
    public AuthenticatorAttestationResponse Response { get; set; } = new();
    public string DeviceName { get; set; }
}

public class AuthenticatorAttestationResponse
{
    public string ClientDataJSON { get; set; } = "";    // Base64url
    public string AttestationObject { get; set; } = ""; // Base64url CBOR
}

/// <summary>
/// Assertion response from the browser after navigator.credentials.get()
/// </summary>
public class AssertionResponse
{
    public string Id { get; set; } = "";           // Base64url credential ID
    public string RawId { get; set; } = "";        // Base64url raw ID
    public string Type { get; set; } = "public-key";
    public AuthenticatorAssertionResponse Response { get; set; } = new();
}

public class AuthenticatorAssertionResponse
{
    public string ClientDataJSON { get; set; } = "";     // Base64url
    public string AuthenticatorData { get; set; } = "";  // Base64url
    public string Signature { get; set; } = "";          // Base64url
    public string UserHandle { get; set; }              // Base64url
}

public class Fido2RegistrationResult
{
    public bool Success { get; set; }
    public string CredentialId { get; set; }
    public string Error { get; set; }
}

public class Fido2AuthenticationResult
{
    public bool Success { get; set; }
    public string UserDn { get; set; }
    public string Error { get; set; }
}

public class Fido2CredentialSummary
{
    public string Id { get; set; } = "";
    public string CredentialId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string AttestationType { get; set; } = "";
    public List<string> Transports { get; set; } = new();
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public long SignCount { get; set; }
    public bool IsEnabled { get; set; }
}

public class RenameCredentialRequest
{
    public string Name { get; set; } = "";
}
