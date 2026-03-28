using System.Diagnostics;
using Directory.Ldap.Handlers;
using Directory.Ldap.Protocol;
using Directory.Ldap.Protocol.Messages;
using Directory.Ldap.Server;
using Microsoft.Extensions.Logging;

namespace Directory.Ldap.Auditing;

/// <summary>
/// Decorates the standard LdapOperationDispatcher to capture timing and audit information
/// for every LDAP operation. Delegates to the inner dispatcher for actual operation handling.
/// </summary>
public class AuditingOperationDispatcher : ILdapOperationDispatcher
{
    private readonly ILdapOperationDispatcher _inner;
    private readonly LdapAuditService _auditService;
    private readonly ILogger<AuditingOperationDispatcher> _logger;

    public AuditingOperationDispatcher(
        ILdapOperationDispatcher inner,
        LdapAuditService auditService,
        ILogger<AuditingOperationDispatcher> logger)
    {
        _inner = inner;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task DispatchAsync(LdapMessage message, ILdapResponseWriter writer, LdapConnectionState state, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string resultCode = "Unknown";

        // Create an intercepting writer to capture the result code from the response
        var interceptor = new AuditResponseInterceptor(writer);

        try
        {
            await _inner.DispatchAsync(message, interceptor, state, ct);
            resultCode = interceptor.CapturedResultCode ?? "Success";
        }
        catch (OperationCanceledException)
        {
            resultCode = "Cancelled";
            throw;
        }
        catch (Exception)
        {
            resultCode = "Error";
            throw;
        }
        finally
        {
            sw.Stop();

            try
            {
                var entry = BuildAuditEntry(message, state, resultCode, sw.ElapsedMilliseconds);
                if (entry != null)
                {
                    _auditService.Record(entry);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to record LDAP audit entry");
            }
        }
    }

    private static LdapAuditEntry BuildAuditEntry(LdapMessage message, LdapConnectionState state, string resultCode, long durationMs)
    {
        var (clientIp, clientPort) = ParseEndpoint(state.RemoteEndPoint);
        var operationName = MapOperationName(message.Operation);

        // Skip unbind — it's just a disconnect notification
        if (message.Operation == LdapOperation.UnbindRequest)
            return null;

        var entry = new LdapAuditEntry
        {
            Operation = operationName,
            ClientIp = clientIp,
            ClientPort = clientPort,
            BoundDn = state.BoundDn ?? "(anonymous)",
            ResultCode = resultCode,
            DurationMs = durationMs,
        };

        // Extract operation-specific details
        try
        {
            switch (message.Operation)
            {
                case LdapOperation.BindRequest:
                    var bind = BindRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = bind.Name ?? string.Empty;
                    entry.Details["mechanism"] = bind.IsSimple ? "Simple" : (bind.SaslMechanism ?? "SASL");
                    // Never log passwords
                    break;

                case LdapOperation.SearchRequest:
                    var search = SearchRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = search.BaseObject ?? string.Empty;
                    entry.Details["scope"] = search.Scope.ToString();
                    entry.Details["filter"] = search.Filter?.ToString() ?? "(unknown)";
                    if (search.Attributes?.Count > 0)
                        entry.Details["attributes"] = string.Join(", ", search.Attributes);
                    entry.Details["sizeLimit"] = search.SizeLimit.ToString();
                    break;

                case LdapOperation.AddRequest:
                    var add = AddRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = add.Entry ?? string.Empty;
                    if (add.Attributes?.Count > 0)
                        entry.Details["attributes"] = string.Join(", ", add.Attributes.Select(a => a.Name));
                    break;

                case LdapOperation.ModifyRequest:
                    var modify = ModifyRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = modify.Object ?? string.Empty;
                    if (modify.Changes?.Count > 0)
                    {
                        // Log attribute names only, not values (sensitive data protection)
                        var attrNames = modify.Changes.Select(m => $"{m.Operation}:{m.AttributeName}");
                        entry.Details["modifications"] = string.Join(", ", attrNames);
                    }
                    break;

                case LdapOperation.DelRequest:
                    var del = DeleteRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = del.Entry ?? string.Empty;
                    break;

                case LdapOperation.ModifyDNRequest:
                    var modDn = ModifyDNRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = modDn.Entry ?? string.Empty;
                    entry.Details["newRdn"] = modDn.NewRdn ?? string.Empty;
                    if (!string.IsNullOrEmpty(modDn.NewSuperior))
                        entry.Details["newSuperior"] = modDn.NewSuperior;
                    entry.Details["deleteOldRdn"] = modDn.DeleteOldRdn.ToString();
                    break;

                case LdapOperation.CompareRequest:
                    var compare = CompareRequest.Decode(message.ProtocolOpData);
                    entry.TargetDn = compare.Entry ?? string.Empty;
                    entry.Details["attribute"] = compare.AttributeDesc ?? string.Empty;
                    // Don't log comparison values
                    break;

                case LdapOperation.ExtendedRequest:
                    var ext = ExtendedRequest.Decode(message.ProtocolOpData);
                    entry.Details["oid"] = ext.RequestName ?? string.Empty;
                    break;

                case LdapOperation.AbandonRequest:
                    entry.Details["abandonedMessageId"] = message.MessageId.ToString();
                    break;
            }
        }
        catch
        {
            // Best-effort detail extraction — don't fail auditing if decode fails
        }

        return entry;
    }

    private static string MapOperationName(LdapOperation op) => op switch
    {
        LdapOperation.BindRequest => "Bind",
        LdapOperation.SearchRequest => "Search",
        LdapOperation.AddRequest => "Add",
        LdapOperation.ModifyRequest => "Modify",
        LdapOperation.DelRequest => "Delete",
        LdapOperation.ModifyDNRequest => "ModifyDN",
        LdapOperation.CompareRequest => "Compare",
        LdapOperation.ExtendedRequest => "Extended",
        LdapOperation.AbandonRequest => "Abandon",
        LdapOperation.UnbindRequest => "Unbind",
        _ => op.ToString(),
    };

    private static (string ip, int port) ParseEndpoint(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint))
            return ("unknown", 0);

        // Handle IPv6 [addr]:port and IPv4 addr:port
        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(endpoint.AsSpan(lastColon + 1), out var port))
        {
            var ip = endpoint[..lastColon].Trim('[', ']');
            return (ip, port);
        }

        return (endpoint, 0);
    }
}

/// <summary>
/// Wraps an ILdapResponseWriter to intercept the result code from LDAP responses.
/// </summary>
internal class AuditResponseInterceptor : ILdapResponseWriter
{
    private readonly ILdapResponseWriter _inner;

    public string CapturedResultCode { get; private set; }

    public AuditResponseInterceptor(ILdapResponseWriter inner)
    {
        _inner = inner;
    }

    public async Task WriteMessageAsync(int messageId, byte[] protocolOpData, List<LdapControl> controls = null, CancellationToken ct = default)
    {
        // Try to extract result code from the response envelope
        // LDAP result messages have the result code as the first element in the SEQUENCE
        if (protocolOpData is { Length: > 2 } && CapturedResultCode == null)
        {
            try
            {
                // BER: protocolOpData starts with the APPLICATION tag + length,
                // then the first element is the resultCode (ENUMERATED)
                // We do a simple extraction: skip the app tag+length, read the first INTEGER/ENUM
                var span = protocolOpData.AsSpan();
                // Skip the APPLICATION tag byte
                int idx = 1;
                // Skip the length bytes
                if (idx < span.Length)
                {
                    if ((span[idx] & 0x80) != 0)
                    {
                        var lenBytes = span[idx] & 0x7F;
                        idx += 1 + lenBytes;
                    }
                    else
                    {
                        idx += 1;
                    }
                }
                // Now we should be at the resultCode ENUMERATED (tag 0x0A) or INTEGER (tag 0x02)
                if (idx < span.Length - 2 && (span[idx] == 0x0A || span[idx] == 0x02))
                {
                    var valLen = span[idx + 1];
                    if (valLen >= 1 && valLen <= 4 && idx + 2 + valLen <= span.Length)
                    {
                        int resultCode = 0;
                        for (int i = 0; i < valLen; i++)
                            resultCode = (resultCode << 8) | span[idx + 2 + i];
                        CapturedResultCode = resultCode.ToString();
                    }
                }
            }
            catch
            {
                // Best-effort extraction
            }
        }

        await _inner.WriteMessageAsync(messageId, protocolOpData, controls, ct);
    }

    public Task WriteBytesAsync(byte[] data, CancellationToken ct = default)
    {
        return _inner.WriteBytesAsync(data, ct);
    }
}
