using System.Collections.Concurrent;
using Directory.Rpc.Dispatch;
using Directory.Rpc.Ndr;
using Microsoft.Extensions.Logging;

namespace Directory.Rpc.Lsa;

/// <summary>
/// Implements MS-LSAD secret object operations (opnums 16, 28, 29, 30).
/// Secrets store machine account passwords, trust credentials, and DPAPI keys.
/// Each secret has a CurrentValue and OldValue to support credential rotation.
/// </summary>
public class LsaSecretOperations
{
    private readonly ILogger<LsaSecretOperations> _logger;

    /// <summary>
    /// In-memory secret store: key = "tenantId:secretName", value = secret data.
    /// In production this would be backed by CosmosDB with encryption at rest.
    /// </summary>
    private static readonly ConcurrentDictionary<string, LsaSecretData> Secrets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Secret handle table: GUID -> secret name (for resolving handles opened via LsarOpenSecret).
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, (string TenantId, string SecretName, uint Access)> SecretHandles = new();

    public LsaSecretOperations(ILogger<LsaSecretOperations> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 16: LsarCreateSecret
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarCreateSecretAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // SecretName: RPC_UNICODE_STRING
        var nameHeader = reader.ReadRpcUnicodeString();
        string secretName = "";
        if (nameHeader.ReferentId != 0)
            secretName = reader.ReadConformantVaryingString();

        // DesiredAccess (uint32)
        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("LsarCreateSecret: Name={Name}, Access=0x{Access:X8}", secretName, desiredAccess);

        var secretKey = $"{handle.TenantId}:{secretName}";

        // Check if secret already exists
        if (Secrets.ContainsKey(secretKey))
        {
            var errWriter = new NdrWriter();
            errWriter.WriteContextHandle(0, Guid.Empty);
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameCollision);
            return Task.FromResult(errWriter.ToArray());
        }

        // Create the secret entry
        var secretData = new LsaSecretData
        {
            Name = secretName,
            TenantId = handle.TenantId,
            CreatedAt = DateTimeOffset.UtcNow,
            ModifiedAt = DateTimeOffset.UtcNow,
        };
        Secrets[secretKey] = secretData;

        // Create a handle for the new secret
        var secretHandleGuid = Guid.NewGuid();
        SecretHandles[secretHandleGuid] = (handle.TenantId, secretName, desiredAccess);

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, secretHandleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 28: LsarOpenSecret
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarOpenSecretAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Policy handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();
        var handle = GetPolicyHandle(context, attr, uuid);

        // SecretName: RPC_UNICODE_STRING
        var nameHeader = reader.ReadRpcUnicodeString();
        string secretName = "";
        if (nameHeader.ReferentId != 0)
            secretName = reader.ReadConformantVaryingString();

        // DesiredAccess (uint32)
        var desiredAccess = reader.ReadUInt32();

        _logger.LogDebug("LsarOpenSecret: Name={Name}", secretName);

        var secretKey = $"{handle.TenantId}:{secretName}";

        if (!Secrets.ContainsKey(secretKey))
        {
            var errWriter = new NdrWriter();
            errWriter.WriteContextHandle(0, Guid.Empty);
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return Task.FromResult(errWriter.ToArray());
        }

        // Create a handle for the opened secret
        var secretHandleGuid = Guid.NewGuid();
        SecretHandles[secretHandleGuid] = (handle.TenantId, secretName, desiredAccess);

        var writer = new NdrWriter();
        writer.WriteContextHandle(0, secretHandleGuid);
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 29: LsarQuerySecret
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarQuerySecretAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Secret handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();

        _logger.LogDebug("LsarQuerySecret: handle={Handle}", uuid);

        if (!SecretHandles.TryGetValue(uuid, out var secretRef))
        {
            var errWriter = new NdrWriter();
            // EncryptedCurrentValue (null pointer)
            errWriter.WritePointer(true);
            // CurrentValueSetTime (LARGE_INTEGER)
            errWriter.WriteInt64(0);
            // EncryptedOldValue (null pointer)
            errWriter.WritePointer(true);
            // OldValueSetTime (LARGE_INTEGER)
            errWriter.WriteInt64(0);
            errWriter.WriteUInt32(LsaConstants.StatusInvalidHandle);
            return Task.FromResult(errWriter.ToArray());
        }

        var secretKey = $"{secretRef.TenantId}:{secretRef.SecretName}";
        Secrets.TryGetValue(secretKey, out var secret);

        var writer = new NdrWriter();

        // EncryptedCurrentValue: [out] pointer to LSAPR_CR_CIPHER_VALUE
        if (secret?.CurrentValue != null && secret.CurrentValue.Length > 0)
        {
            writer.WritePointer(false);
            WriteCipherValue(writer, secret.CurrentValue);
        }
        else
        {
            writer.WritePointer(true);
        }

        // CurrentValueSetTime: LARGE_INTEGER (FILETIME)
        writer.WriteInt64(secret?.CurrentValueSetTime.ToFileTime() ?? 0);

        // EncryptedOldValue: [out] pointer to LSAPR_CR_CIPHER_VALUE
        if (secret?.OldValue != null && secret.OldValue.Length > 0)
        {
            writer.WritePointer(false);
            WriteCipherValue(writer, secret.OldValue);
        }
        else
        {
            writer.WritePointer(true);
        }

        // OldValueSetTime: LARGE_INTEGER (FILETIME)
        writer.WriteInt64(secret?.OldValueSetTime.ToFileTime() ?? 0);

        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Opnum 30: LsarSetSecret
    // ─────────────────────────────────────────────────────────────────────

    public Task<byte[]> LsarSetSecretAsync(ReadOnlyMemory<byte> stubData, RpcCallContext context, CancellationToken ct)
    {
        var reader = new NdrReader(stubData);

        // Secret handle (20 bytes)
        var (attr, uuid) = reader.ReadContextHandle();

        _logger.LogDebug("LsarSetSecret: handle={Handle}", uuid);

        if (!SecretHandles.TryGetValue(uuid, out var secretRef))
        {
            var errWriter = new NdrWriter();
            errWriter.WriteUInt32(LsaConstants.StatusInvalidHandle);
            return Task.FromResult(errWriter.ToArray());
        }

        // EncryptedCurrentValue: [in, unique] PLSAPR_CR_CIPHER_VALUE
        var currentValuePtr = reader.ReadPointer();
        byte[] currentValue = null;
        if (currentValuePtr != 0)
        {
            currentValue = ReadCipherValue(reader);
        }

        // EncryptedOldValue: [in, unique] PLSAPR_CR_CIPHER_VALUE
        var oldValuePtr = reader.ReadPointer();
        byte[] oldValue = null;
        if (oldValuePtr != 0)
        {
            oldValue = ReadCipherValue(reader);
        }

        var secretKey = $"{secretRef.TenantId}:{secretRef.SecretName}";

        if (!Secrets.TryGetValue(secretKey, out var secret))
        {
            var errWriter = new NdrWriter();
            errWriter.WriteUInt32(LsaConstants.StatusObjectNameNotFound);
            return Task.FromResult(errWriter.ToArray());
        }

        // If new current value is provided, rotate: old current -> OldValue, new -> CurrentValue
        if (currentValue != null)
        {
            secret.OldValue = secret.CurrentValue;
            secret.OldValueSetTime = secret.CurrentValueSetTime;
            secret.CurrentValue = currentValue;
            secret.CurrentValueSetTime = DateTimeOffset.UtcNow;
        }

        if (oldValue != null)
        {
            secret.OldValue = oldValue;
            secret.OldValueSetTime = DateTimeOffset.UtcNow;
        }

        secret.ModifiedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation("LsarSetSecret: updated secret {Name}", secretRef.SecretName);

        var writer = new NdrWriter();
        writer.WriteUInt32(LsaConstants.StatusSuccess);

        return Task.FromResult(writer.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static void WriteCipherValue(NdrWriter writer, byte[] data)
    {
        // LSAPR_CR_CIPHER_VALUE:
        //   Length (uint32) — actual byte count
        //   MaximumLength (uint32) — buffer capacity
        //   pointer to conformant byte array
        writer.WriteUInt32((uint)data.Length);
        writer.WriteUInt32((uint)data.Length);
        writer.WritePointer(false); // pointer to buffer

        // Conformant byte array: MaxCount + bytes
        writer.WriteUInt32((uint)data.Length);
        writer.WriteBytes(data);
    }

    private static byte[] ReadCipherValue(NdrReader reader)
    {
        // LSAPR_CR_CIPHER_VALUE:
        //   Length (uint32)
        //   MaximumLength (uint32)
        //   pointer to conformant byte array
        var length = reader.ReadUInt32();
        var maxLength = reader.ReadUInt32();
        var bufferPtr = reader.ReadPointer();

        if (bufferPtr == 0 || length == 0)
            return [];

        // Conformant byte array: MaxCount + bytes
        var maxCount = reader.ReadUInt32();
        var data = reader.ReadBytes((int)length);
        return data.ToArray();
    }

    private LsaPolicyHandle GetPolicyHandle(RpcCallContext context, uint attr, Guid uuid)
    {
        var handleBytes = new byte[20];
        BitConverter.GetBytes(attr).CopyTo(handleBytes, 0);
        uuid.TryWriteBytes(handleBytes.AsSpan(4));

        var handle = context.ContextHandles.GetHandle<LsaPolicyHandle>(handleBytes);
        if (handle == null)
            throw new RpcFaultException(LsaConstants.StatusInvalidHandle, "Invalid LSA policy handle");

        return handle;
    }
}

/// <summary>
/// In-memory representation of an LSA secret.
/// Stores CurrentValue/OldValue pairs for credential rotation.
/// </summary>
internal class LsaSecretData
{
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "";
    public byte[] CurrentValue { get; set; }
    public DateTimeOffset CurrentValueSetTime { get; set; }
    public byte[] OldValue { get; set; }
    public DateTimeOffset OldValueSetTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
