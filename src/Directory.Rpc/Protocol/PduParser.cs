using System.Buffers.Binary;

namespace Directory.Rpc.Protocol;

/// <summary>
/// Parses incoming DCE/RPC PDUs into typed structures.
/// All methods expect the full PDU including the 16-byte header.
/// </summary>
public static class PduParser
{
    /// <summary>
    /// Parses a Bind PDU (type 11). After the 16-byte header:
    /// uint16 maxXmitFrag, uint16 maxRecvFrag, uint32 assocGroupId,
    /// then the presentation context list.
    /// </summary>
    public static BindData ParseBind(ReadOnlySpan<byte> pdu)
    {
        int offset = RpcPduHeader.HeaderSize;

        ushort maxXmitFrag = BinaryPrimitives.ReadUInt16LittleEndian(pdu.Slice(offset)); offset += 2;
        ushort maxRecvFrag = BinaryPrimitives.ReadUInt16LittleEndian(pdu.Slice(offset)); offset += 2;
        uint assocGroupId = BinaryPrimitives.ReadUInt32LittleEndian(pdu.Slice(offset)); offset += 4;

        var contexts = ParsePresentationContextList(pdu, ref offset);

        return new BindData(maxXmitFrag, maxRecvFrag, assocGroupId, contexts);
    }

    /// <summary>
    /// Parses an AlterContext PDU (type 14). Same structure as Bind.
    /// </summary>
    public static BindData ParseAlterContext(ReadOnlySpan<byte> pdu)
    {
        return ParseBind(pdu);
    }

    /// <summary>
    /// Parses a Request PDU (type 0). After the 16-byte header:
    /// uint32 allocHint, uint16 contextId, uint16 opnum, then stub data.
    /// Auth verifier is excluded from stub data when auth_length > 0.
    /// </summary>
    public static RequestData ParseRequest(ReadOnlySpan<byte> pdu, ushort authLength)
    {
        int offset = RpcPduHeader.HeaderSize;

        uint allocHint = BinaryPrimitives.ReadUInt32LittleEndian(pdu.Slice(offset)); offset += 4;
        ushort contextId = BinaryPrimitives.ReadUInt16LittleEndian(pdu.Slice(offset)); offset += 2;
        ushort opnum = BinaryPrimitives.ReadUInt16LittleEndian(pdu.Slice(offset)); offset += 2;

        // Stub data starts at offset 24 and extends to the end of the PDU minus auth data
        int stubEnd = pdu.Length;
        if (authLength > 0)
        {
            // Auth verifier is: auth_pad + 8-byte auth header + credentials
            stubEnd -= (authLength + 8);
        }

        var stubData = pdu.Slice(offset, stubEnd - offset).ToArray();

        return new RequestData(allocHint, contextId, opnum, stubData);
    }

    /// <summary>
    /// Parses the auth_verifier from a PDU. The auth verifier is at the end of the PDU:
    /// auth_type(1), auth_level(1), auth_pad_length(1), reserved(1), auth_context_id(4),
    /// then credentials(auth_length).
    /// </summary>
    public static AuthVerifier ParseAuthVerifier(ReadOnlySpan<byte> pdu, ushort authLength)
    {
        if (authLength == 0)
            return null;

        // Auth verifier starts at fragLength - authLength - 8
        int authStart = pdu.Length - authLength - 8;
        if (authStart < RpcPduHeader.HeaderSize)
            return null;

        byte authType = pdu[authStart];
        byte authLevel = pdu[authStart + 1];
        byte authPadLength = pdu[authStart + 2];
        // byte reserved = pdu[authStart + 3];
        uint authContextId = BinaryPrimitives.ReadUInt32LittleEndian(pdu.Slice(authStart + 4));
        var credentials = pdu.Slice(authStart + 8, authLength).ToArray();

        return new AuthVerifier(authType, authLevel, authPadLength, authContextId, credentials);
    }

    /// <summary>
    /// Parses an Auth3 PDU (type 16). Contains only the auth_verifier after the header + padding.
    /// </summary>
    public static AuthVerifier ParseAuth3(ReadOnlySpan<byte> pdu, ushort authLength)
    {
        return ParseAuthVerifier(pdu, authLength);
    }

    private static PresentationContext[] ParsePresentationContextList(ReadOnlySpan<byte> pdu, ref int offset)
    {
        byte numContexts = pdu[offset]; offset += 1;
        offset += 3; // padding (3 reserved bytes)

        var contexts = new PresentationContext[numContexts];

        for (int i = 0; i < numContexts; i++)
        {
            ushort contextId = BinaryPrimitives.ReadUInt16LittleEndian(pdu.Slice(offset)); offset += 2;
            byte numTransferSyntaxes = pdu[offset]; offset += 1;
            offset += 1; // padding

            // Abstract syntax: 16-byte UUID + uint32 version
            var abstractUuid = new Guid(pdu.Slice(offset, 16)); offset += 16;
            uint abstractVersion = BinaryPrimitives.ReadUInt32LittleEndian(pdu.Slice(offset)); offset += 4;
            var abstractSyntax = new RpcSyntaxId(abstractUuid, abstractVersion);

            // Transfer syntaxes
            var transferSyntaxes = new RpcSyntaxId[numTransferSyntaxes];
            for (int t = 0; t < numTransferSyntaxes; t++)
            {
                var transferUuid = new Guid(pdu.Slice(offset, 16)); offset += 16;
                uint transferVersion = BinaryPrimitives.ReadUInt32LittleEndian(pdu.Slice(offset)); offset += 4;
                transferSyntaxes[t] = new RpcSyntaxId(transferUuid, transferVersion);
            }

            contexts[i] = new PresentationContext(contextId, abstractSyntax, transferSyntaxes);
        }

        return contexts;
    }
}
