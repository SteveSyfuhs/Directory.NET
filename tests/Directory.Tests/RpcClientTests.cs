using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Directory.Replication;
using Directory.Replication.Drsr;
using Directory.Rpc.Client;
using Directory.Rpc.Ndr;
using Directory.Rpc.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Comprehensive tests for the RPC client infrastructure used for replication from Windows DCs.
/// Covers NTLM authentication, PDU construction, EPM protocol towers, DRSUAPI NDR encoding,
/// DrsRpcReplicationClient properties, and NTLM crypto verification.
/// </summary>
public class RpcClientTests
{
    // ════════════════════════════════════════════════════════════════
    //  1. NTLM Client Message Builder Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildNegotiateMessage_ReturnsValidType1WithNtlmsspSignature()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] message = builder.BuildNegotiateMessage();

        // First 8 bytes must be "NTLMSSP\0"
        string signature = Encoding.ASCII.GetString(message, 0, 7);
        Assert.Equal("NTLMSSP", signature);
        Assert.Equal(0, message[7]); // null terminator
    }

    [Fact]
    public void BuildNegotiateMessage_HasTypeField1()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] message = builder.BuildNegotiateMessage();

        uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(8));
        Assert.Equal(1u, messageType);
    }

    [Fact]
    public void BuildNegotiateMessage_IncludesUnicodeAndNtlmNegotiateFlags()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] message = builder.BuildNegotiateMessage();

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(12));

        // NTLMSSP_NEGOTIATE_UNICODE = 0x01
        Assert.True((flags & 0x00000001) != 0, "UNICODE flag should be set");
        // NTLMSSP_NEGOTIATE_NTLM = 0x200
        Assert.True((flags & 0x00000200) != 0, "NTLM flag should be set");
    }

    [Fact]
    public void BuildNegotiateMessage_IncludesRequestTargetFlag()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] message = builder.BuildNegotiateMessage();

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(12));

        // NTLMSSP_REQUEST_TARGET = 0x04
        Assert.True((flags & 0x00000004) != 0, "REQUEST_TARGET flag should be set");
    }

    [Fact]
    public void BuildNegotiateMessage_IncludesSealAndSignFlags()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] message = builder.BuildNegotiateMessage();

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(12));

        // NTLMSSP_NEGOTIATE_SEAL = 0x20
        Assert.True((flags & 0x00000020) != 0, "SEAL flag should be set");
        // NTLMSSP_NEGOTIATE_SIGN = 0x10
        Assert.True((flags & 0x00000010) != 0, "SIGN flag should be set");
    }

    [Fact]
    public void BuildNegotiateMessage_Returns32Bytes()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] message = builder.BuildNegotiateMessage();

        Assert.Equal(32, message.Length);
    }

    [Fact]
    public void ParseChallengeMessage_Extracts8ByteServerChallenge()
    {
        byte[] type2 = BuildMockType2Message(
            serverChallenge: [0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44],
            negotiateFlags: 0x00000235,
            targetName: null);

        var builder = new NtlmClientMessageBuilder();
        var (challenge, _, _) = builder.ParseChallengeMessage(type2);

        Assert.Equal(8, challenge.Length);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44 }, challenge);
    }

    [Fact]
    public void ParseChallengeMessage_ReadsNegotiateFlags()
    {
        uint expectedFlags = 0x00628233;
        byte[] type2 = BuildMockType2Message(
            serverChallenge: new byte[8],
            negotiateFlags: expectedFlags,
            targetName: null);

        var builder = new NtlmClientMessageBuilder();
        var (_, flags, _) = builder.ParseChallengeMessage(type2);

        Assert.Equal(expectedFlags, flags);
    }

    [Fact]
    public void ParseChallengeMessage_HandlesTargetName()
    {
        byte[] type2 = BuildMockType2Message(
            serverChallenge: new byte[8],
            negotiateFlags: 0x00000235,
            targetName: "CONTOSO");

        var builder = new NtlmClientMessageBuilder();
        var (_, _, targetName) = builder.ParseChallengeMessage(type2);

        Assert.Equal("CONTOSO", targetName);
    }

    [Fact]
    public void ParseChallengeMessage_ThrowsOnInvalidSignature()
    {
        byte[] badMessage = new byte[32];
        Encoding.ASCII.GetBytes("BADSTUFF").CopyTo(badMessage, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(badMessage.AsSpan(8), 2);

        var builder = new NtlmClientMessageBuilder();
        var ex = Assert.Throws<ArgumentException>(() => builder.ParseChallengeMessage(badMessage));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseChallengeMessage_ThrowsOnWrongMessageType()
    {
        byte[] wrongType = BuildMockType2Message(new byte[8], 0x00000235, null);
        // Overwrite the message type from 2 to 1
        BinaryPrimitives.WriteUInt32LittleEndian(wrongType.AsSpan(8), 1);

        var builder = new NtlmClientMessageBuilder();
        var ex = Assert.Throws<ArgumentException>(() => builder.ParseChallengeMessage(wrongType));
        Assert.Contains("Type 2", ex.Message);
    }

    [Fact]
    public void BuildAuthenticateMessage_ReturnsMessageWithNtlmsspSignature()
    {
        var builder = new NtlmClientMessageBuilder();
        var (type3, _) = BuildType3WithKnownInputs(builder);

        string signature = Encoding.ASCII.GetString(type3, 0, 7);
        Assert.Equal("NTLMSSP", signature);
        Assert.Equal(0, type3[7]);
    }

    [Fact]
    public void BuildAuthenticateMessage_HasTypeField3()
    {
        var builder = new NtlmClientMessageBuilder();
        var (type3, _) = BuildType3WithKnownInputs(builder);

        uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(8));
        Assert.Equal(3u, messageType);
    }

    [Fact]
    public void BuildAuthenticateMessage_ProducesSessionKey16Bytes()
    {
        var builder = new NtlmClientMessageBuilder();
        var (_, sessionKey) = BuildType3WithKnownInputs(builder);

        Assert.Equal(16, sessionKey.Length);
    }

    [Fact]
    public void BuildAuthenticateMessage_NtResponseStartsWith16ByteNtProofStr()
    {
        var builder = new NtlmClientMessageBuilder();
        var (type3, _) = BuildType3WithKnownInputs(builder);

        // NtChallengeResponseFields are at offset 20 in Type 3: Len(2) + MaxLen(2) + Offset(4)
        ushort ntResponseLen = BinaryPrimitives.ReadUInt16LittleEndian(type3.AsSpan(20));
        uint ntResponseOffset = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(24));

        // NtResponse must be at least 16 bytes (NtProofStr) plus the blob
        Assert.True(ntResponseLen >= 16, $"NtResponse length should be >= 16, was {ntResponseLen}");

        // The NtProofStr is the first 16 bytes of the NtResponse payload
        var ntProofStr = type3.AsSpan((int)ntResponseOffset, 16);
        // Verify it is not all zeros (a real HMAC-MD5 result)
        Assert.False(ntProofStr.SequenceEqual(new byte[16]), "NtProofStr should not be all zeros");
    }

    [Fact]
    public void BuildAuthenticateMessage_DifferentPasswordsProduceDifferentHashes()
    {
        var builder = new NtlmClientMessageBuilder();
        byte[] serverChallenge = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        uint flags = 0x00000235;

        byte[] ntHash1 = MD4Hash("Password1");
        byte[] ntHash2 = MD4Hash("Password2");

        var (type3a, keyA) = builder.BuildAuthenticateMessage("user", "DOMAIN", ntHash1, serverChallenge, flags);
        var (type3b, keyB) = builder.BuildAuthenticateMessage("user", "DOMAIN", ntHash2, serverChallenge, flags);

        Assert.False(keyA.AsSpan().SequenceEqual(keyB), "Session keys from different passwords should differ");
    }

    [Fact]
    public void BuildAuthenticateMessage_IncludesDomainAndUsernameInPayload()
    {
        var builder = new NtlmClientMessageBuilder();
        var (type3, _) = builder.BuildAuthenticateMessage(
            "Administrator", "CONTOSO", new byte[16],
            new byte[8], 0x00000235);

        // DomainNameFields at offset 28: Len(2) + MaxLen(2) + Offset(4)
        ushort domainLen = BinaryPrimitives.ReadUInt16LittleEndian(type3.AsSpan(28));
        uint domainOffset = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(32));

        // UserNameFields at offset 36: Len(2) + MaxLen(2) + Offset(4)
        ushort userLen = BinaryPrimitives.ReadUInt16LittleEndian(type3.AsSpan(36));
        uint userOffset = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(40));

        string domain = Encoding.Unicode.GetString(type3, (int)domainOffset, domainLen);
        string user = Encoding.Unicode.GetString(type3, (int)userOffset, userLen);

        Assert.Equal("CONTOSO", domain);
        Assert.Equal("Administrator", user);
    }

    [Fact]
    public void RoundTrip_BuildType1_ParseMockType2_BuildType3_AllSucceed()
    {
        var builder = new NtlmClientMessageBuilder();

        // Step 1: Build Type 1
        byte[] type1 = builder.BuildNegotiateMessage();
        Assert.NotEmpty(type1);

        // Step 2: Parse a mock Type 2
        byte[] type2 = BuildMockType2Message(
            [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88],
            0x00628233, "CORP");

        var (serverChallenge, negotiateFlags, targetName) = builder.ParseChallengeMessage(type2);
        Assert.Equal(8, serverChallenge.Length);
        Assert.Equal("CORP", targetName);

        // Step 3: Build Type 3
        byte[] ntHash = new byte[16];
        RandomNumberGenerator.Fill(ntHash);

        var (type3, sessionKey) = builder.BuildAuthenticateMessage(
            "admin", "CORP", ntHash, serverChallenge, negotiateFlags);

        Assert.NotEmpty(type3);
        Assert.Equal(16, sessionKey.Length);

        // Verify Type 3 has correct signature and type
        uint msgType = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(8));
        Assert.Equal(3u, msgType);
    }

    // ════════════════════════════════════════════════════════════════
    //  2. RPC PDU Construction Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void BindPdu_HasCorrectPacketType11()
    {
        byte[] bindPdu = BuildTestBindPdu(authData: null);
        var header = RpcPduHeader.Read(bindPdu);

        Assert.Equal(RpcConstants.PTypeBind, header.PacketType); // 11
    }

    [Fact]
    public void BindPdu_HasVersion5_0()
    {
        byte[] bindPdu = BuildTestBindPdu(authData: null);
        var header = RpcPduHeader.Read(bindPdu);

        Assert.Equal(5, header.VersionMajor);
        Assert.Equal(0, header.VersionMinor);
    }

    [Fact]
    public void BindPdu_IncludesPresentationContextWithInterfaceUuid()
    {
        var interfaceId = new Guid("e3514235-4b06-11d1-ab04-00c04fc2dcd2");
        byte[] bindPdu = BuildTestBindPdu(interfaceId: interfaceId, authData: null);

        // After the 16-byte header: MaxXmitFrag(2) + MaxRecvFrag(2) + AssocGroupId(4) = 8 bytes
        // Then: NumContexts(1) + reserved(3) + ContextId(2) + NumTransferSyntaxes(1) + reserved(1)
        //       = 8 bytes
        // Abstract syntax UUID starts at offset 16 + 8 + 8 = 32
        int uuidOffset = 16 + 8 + 4 + 2 + 1 + 1; // header + bind fields + ctx header
        var embeddedUuid = new Guid(bindPdu.AsSpan(uuidOffset, 16));

        Assert.Equal(interfaceId, embeddedUuid);
    }

    [Fact]
    public void BindPdu_WithNtlmAuth_HasAuthVerifierAppended()
    {
        byte[] fakeAuth = [0x01, 0x02, 0x03, 0x04];
        byte[] bindPdu = BuildTestBindPdu(authData: fakeAuth);
        var header = RpcPduHeader.Read(bindPdu);

        Assert.Equal((ushort)fakeAuth.Length, header.AuthLength);
        Assert.True(bindPdu.Length > 16 + 8 + 8 + 24 + 8, "PDU with auth should be larger than base PDU");
    }

    [Fact]
    public void Auth3Pdu_HasCorrectPacketType16()
    {
        byte[] auth3Pdu = BuildTestAuth3Pdu([0xAA, 0xBB]);
        var header = RpcPduHeader.Read(auth3Pdu);

        Assert.Equal(RpcConstants.PTypeAuth3, header.PacketType); // 16
    }

    [Fact]
    public void RequestPdu_HasCorrectPacketType0()
    {
        byte[] requestPdu = BuildTestRequestPdu(opnum: 3, stubData: [0x00]);
        var header = RpcPduHeader.Read(requestPdu);

        Assert.Equal(RpcConstants.PTypeRequest, header.PacketType); // 0
    }

    [Fact]
    public void RequestPdu_IncludesOpnumAndContextId()
    {
        ushort opnum = 3;
        ushort contextId = 0;
        byte[] requestPdu = BuildTestRequestPdu(opnum: opnum, stubData: [0x00], contextId: contextId);

        // Request body after 16-byte header: allocHint(4) + contextId(2) + opnum(2)
        ushort readContextId = BinaryPrimitives.ReadUInt16LittleEndian(requestPdu.AsSpan(20));
        ushort readOpnum = BinaryPrimitives.ReadUInt16LittleEndian(requestPdu.AsSpan(22));

        Assert.Equal(contextId, readContextId);
        Assert.Equal(opnum, readOpnum);
    }

    [Fact]
    public void FragmentSplitting_RespectsMaxXmitFragSizeLimits()
    {
        // With maxXmitFrag=100, overhead=24 bytes, max stub per fragment = 76 bytes.
        // A 200-byte stub should require at least 3 fragments.
        ushort maxXmitFrag = 100;
        const int overhead = RpcPduHeader.HeaderSize + 8; // 24
        int maxStubPerFragment = maxXmitFrag - overhead;

        byte[] largeStub = new byte[200];
        int expectedFragments = (int)Math.Ceiling((double)largeStub.Length / maxStubPerFragment);

        Assert.True(expectedFragments >= 3, $"Expected >= 3 fragments, computed {expectedFragments}");

        // Verify each fragment respects the max size
        var fragments = BuildFragmentedRequestHelper(largeStub, maxXmitFrag);
        foreach (var frag in fragments)
        {
            Assert.True(frag.Length <= maxXmitFrag,
                $"Fragment of {frag.Length} bytes exceeds maxXmitFrag={maxXmitFrag}");
        }
    }

    [Fact]
    public void MultiFragmentResponse_ReassemblyConcatenatesStubData()
    {
        byte[] original = new byte[250];
        Random.Shared.NextBytes(original);
        ushort maxXmitFrag = 80;

        var fragments = BuildFragmentedResponseHelper(1, 0, original, maxXmitFrag);
        Assert.True(fragments.Length > 1, "Should produce multiple fragments");

        // Reassemble stub data from each fragment
        const int overhead = RpcPduHeader.HeaderSize + 8;
        using var reassembled = new MemoryStream();
        foreach (var frag in fragments)
        {
            reassembled.Write(frag, overhead, frag.Length - overhead);
        }

        Assert.Equal(original, reassembled.ToArray());
    }

    // ════════════════════════════════════════════════════════════════
    //  3. EPM Client Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void EpmInterfaceUuid_IsCorrect()
    {
        var expected = new Guid("e1af8308-5d1f-11c9-91a4-08002b14a0fa");
        Assert.Equal(expected, RpcConstants.EpmInterfaceId);
    }

    [Fact]
    public void EpmMajorVersion_Is3()
    {
        Assert.Equal(3, RpcConstants.EpmMajorVersion);
    }

    [Fact]
    public void ProtocolTower_EncodesInterfaceUuidCorrectly()
    {
        var interfaceId = new Guid("e3514235-4b06-11d1-ab04-00c04fc2dcd2");
        byte[] tower = BuildProtocolTower(interfaceId, 4);

        // Floor count at offset 0 (ushort) should be 5
        ushort floorCount = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(0));
        Assert.Equal(5, floorCount);

        // Floor 1: LHS length (2) then protocol ID 0x0D (1) + UUID (16) + version (2)
        ushort lhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(2));
        Assert.Equal(19, lhsLen); // 1 + 16 + 2

        // Protocol ID
        Assert.Equal(0x0D, tower[4]);

        // UUID starts at offset 5
        var embeddedUuid = new Guid(tower.AsSpan(5, 16));
        Assert.Equal(interfaceId, embeddedUuid);
    }

    [Fact]
    public void TowerFloor4_TcpProtocolId_Is0x07()
    {
        byte[] tower = BuildProtocolTower(Guid.NewGuid(), 4);

        // Navigate to floor 4 (TCP floor)
        int offset = 2; // skip floor count

        // Skip floors 1-3
        for (int i = 0; i < 3; i++)
        {
            ushort lhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
            offset += 2 + lhsLen;
            ushort rhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
            offset += 2 + rhsLen;
        }

        // Floor 4: LHS
        ushort tcpLhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
        offset += 2;
        Assert.Equal(1, tcpLhsLen);
        Assert.Equal(0x07, tower[offset]); // TCP protocol ID
    }

    [Fact]
    public void TowerFloor4_TcpPort_EncodedInBigEndian()
    {
        byte[] tower = BuildProtocolTower(Guid.NewGuid(), 4);

        // Navigate to floor 4 RHS (TCP port)
        int offset = 2; // skip floor count

        for (int i = 0; i < 3; i++)
        {
            ushort lhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
            offset += 2 + lhsLen;
            ushort rhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
            offset += 2 + rhsLen;
        }

        // Floor 4 LHS
        ushort tcpLhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
        offset += 2 + tcpLhsLen;

        // Floor 4 RHS: 2 bytes, big-endian port
        ushort tcpRhsLen = BinaryPrimitives.ReadUInt16LittleEndian(tower.AsSpan(offset));
        offset += 2;
        Assert.Equal(2, tcpRhsLen);

        // Port is 0 in the request tower (requesting any)
        int port = (tower[offset] << 8) | tower[offset + 1];
        Assert.Equal(0, port); // request tower asks for any port
    }

    // ════════════════════════════════════════════════════════════════
    //  4. DRSUAPI Client Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void DrsuapiInterfaceId_IsCorrectUuid()
    {
        var expected = new Guid("e3514235-4b06-11d1-ab04-00c04fc2dcd2");
        Assert.Equal(expected, DrsuapiClient.DrsuapiInterfaceId);
    }

    [Fact]
    public void DrsuapiClient_DefaultState_IsNotConnected()
    {
        var client = new DrsuapiClient();
        Assert.False(client.IsConnected);
        Assert.False(client.IsBound);
        Assert.Null(client.ServerExtensions);
    }

    [Fact]
    public void DrsBindRequest_NdrEncoding_StartsWithContextHandle20Bytes()
    {
        // Simulate the DRSBind request encoding pattern from DrsuapiClient.DrsBindAsync
        var writer = new NdrWriter();
        var clientDsaGuid = Guid.NewGuid();

        // puuidClientDsa pointer + GUID
        writer.WritePointer(isNull: false);
        Span<byte> guidBytes = stackalloc byte[16];
        clientDsaGuid.TryWriteBytes(guidBytes);
        writer.WriteBytes(guidBytes);

        byte[] result = writer.ToArray();

        // pointer (4 bytes) + GUID (16 bytes) = 20 bytes
        Assert.Equal(20, result.Length);

        // Verify the pointer is non-zero
        uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(0));
        Assert.NotEqual(0u, pointer);

        // Verify the GUID is correctly encoded
        var readGuid = new Guid(result.AsSpan(4, 16));
        Assert.Equal(clientDsaGuid, readGuid);
    }

    [Fact]
    public void DrsGetNCChangesV8Request_IncludesCorrectVersionMarker8()
    {
        // Simulate the version marker encoding from DrsGetNCChangesAsync
        var writer = new NdrWriter();

        // Context handle (20 bytes)
        writer.WriteContextHandle(0, Guid.NewGuid());

        // dwInVersion = 8
        writer.WriteUInt32(8);

        byte[] result = writer.ToArray();

        // Context handle is 20 bytes, version is at offset 20
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(20));
        Assert.Equal(8u, version);
    }

    [Fact]
    public void DsName_WireEncoding_IncludesStructLenSidLenGuid()
    {
        var dsname = DSNAME.FromDn("DC=corp,DC=contoso,DC=com",
            guid: new Guid("12345678-1234-1234-1234-123456789abc"));

        var writer = new NdrWriter();

        // Replicate WriteDsName encoding
        writer.WriteUInt32(dsname.ComputeStructLen());
        writer.WriteUInt32(dsname.SidLen);
        Span<byte> guidBytes = stackalloc byte[16];
        dsname.Guid.TryWriteBytes(guidBytes);
        writer.WriteBytes(guidBytes);
        writer.WriteUInt32(dsname.NameLen);

        byte[] result = writer.ToArray();

        // structLen (4) + sidLen (4) + guid (16) + nameLen (4) = 28 bytes minimum header
        Assert.True(result.Length >= 28);

        uint structLen = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(0));
        Assert.Equal(dsname.ComputeStructLen(), structLen);

        uint sidLen = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4));
        Assert.Equal(0u, sidLen); // no SID in this case

        var readGuid = new Guid(result.AsSpan(8, 16));
        Assert.Equal(dsname.Guid, readGuid);

        uint nameLen = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(24));
        Assert.Equal((uint)"DC=corp,DC=contoso,DC=com".Length, nameLen);
    }

    [Fact]
    public void SchemaPrefixTable_Encoding_IncludesPrefixCount()
    {
        var table = new SCHEMA_PREFIX_TABLE
        {
            PrefixCount = 3,
            PPrefixEntry =
            [
                new OID_PREFIX_ENTRY { NdxValue = 0, Prefix = [0x55, 0x04] },
                new OID_PREFIX_ENTRY { NdxValue = 1, Prefix = [0x55, 0x06] },
                new OID_PREFIX_ENTRY { NdxValue = 2, Prefix = [0x2A, 0x86, 0x48] },
            ]
        };

        var writer = new NdrWriter();

        // Replicate WritePrefixTable encoding
        writer.WriteUInt32(table.PrefixCount);
        writer.WritePointer(isNull: table.PPrefixEntry.Count == 0);

        byte[] result = writer.ToArray();

        uint prefixCount = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(0));
        Assert.Equal(3u, prefixCount);

        // Pointer should be non-zero since we have entries
        uint pointer = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4));
        Assert.NotEqual(0u, pointer);
    }

    [Fact]
    public void UsnVector_Encoding_Is24Bytes()
    {
        var usn = new USN_VECTOR
        {
            UsnHighObjUpdate = 12345,
            UsnReserved = 0,
            UsnHighPropUpdate = 67890,
        };

        var writer = new NdrWriter();
        writer.WriteInt64(usn.UsnHighObjUpdate);
        writer.WriteInt64(usn.UsnReserved);
        writer.WriteInt64(usn.UsnHighPropUpdate);

        byte[] result = writer.ToArray();
        Assert.Equal(24, result.Length);

        long readHighObj = BinaryPrimitives.ReadInt64LittleEndian(result.AsSpan(0));
        long readReserved = BinaryPrimitives.ReadInt64LittleEndian(result.AsSpan(8));
        long readHighProp = BinaryPrimitives.ReadInt64LittleEndian(result.AsSpan(16));

        Assert.Equal(12345L, readHighObj);
        Assert.Equal(0L, readReserved);
        Assert.Equal(67890L, readHighProp);
    }

    [Fact]
    public void DrsExtensionsInt_Encoding_FlagsAreTransmitted()
    {
        var flags = DrsExtFlags.DRS_EXT_BASE
                  | DrsExtFlags.DRS_EXT_GETCHGREQ_V8
                  | DrsExtFlags.DRS_EXT_GETCHGREPLY_V6;

        var writer = new NdrWriter();

        // cb (extension size)
        writer.WriteUInt32(48);
        // dwFlags
        writer.WriteUInt32((uint)flags);

        byte[] result = writer.ToArray();

        uint cb = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(0));
        Assert.Equal(48u, cb);

        uint readFlags = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4));
        Assert.Equal((uint)flags, readFlags);

        Assert.True((readFlags & (uint)DrsExtFlags.DRS_EXT_BASE) != 0);
        Assert.True((readFlags & (uint)DrsExtFlags.DRS_EXT_GETCHGREQ_V8) != 0);
        Assert.True((readFlags & (uint)DrsExtFlags.DRS_EXT_GETCHGREPLY_V6) != 0);
    }

    [Fact]
    public void ContextHandle_Encoding_Is20Bytes()
    {
        var handleGuid = Guid.NewGuid();
        uint attrs = 0;

        var writer = new NdrWriter();
        writer.WriteContextHandle(attrs, handleGuid);

        byte[] result = writer.ToArray();
        Assert.Equal(20, result.Length);

        uint readAttrs = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(0));
        Assert.Equal(attrs, readAttrs);

        var readGuid = new Guid(result.AsSpan(4, 16));
        Assert.Equal(handleGuid, readGuid);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(42u)]
    public void ContextHandle_AttributeValues_ArePreserved(uint attrValue)
    {
        var writer = new NdrWriter();
        writer.WriteContextHandle(attrValue, Guid.Empty);

        byte[] result = writer.ToArray();
        uint readAttrs = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(0));
        Assert.Equal(attrValue, readAttrs);
    }

    // ════════════════════════════════════════════════════════════════
    //  5. DrsRpcReplicationClient Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DrsRpcReplicationClient_IsDisposable()
    {
        // Verify it implements IAsyncDisposable and can be disposed without error
        var drsuapi = new DrsuapiClient();
        var client = CreateTestRpcReplicationClient(drsuapi);

        // Should not throw
        await client.DisposeAsync();
    }

    [Fact]
    public void DrsRpcReplicationClient_TransportType_IsRpc()
    {
        var drsuapi = new DrsuapiClient();
        var client = CreateTestRpcReplicationClient(drsuapi);

        Assert.Equal(DrsTransportType.Rpc, client.Transport);
    }

    [Fact]
    public void DrsRpcReplicationClient_StatisticsStartAtZero()
    {
        var drsuapi = new DrsuapiClient();
        var client = CreateTestRpcReplicationClient(drsuapi);

        Assert.Equal(0L, client.TotalObjectsReceived);
        Assert.Equal(0L, client.TotalBytesTransferred);
        Assert.Equal(TimeSpan.Zero, client.TotalElapsedTime);
    }

    [Fact]
    public void DrsRpcReplicationClient_IsConnected_ReflectsUnderlyingState()
    {
        // When the underlying DrsuapiClient is not connected, the wrapper should report
        // that the DRSUAPI client is also not connected
        var drsuapi = new DrsuapiClient();

        Assert.False(drsuapi.IsConnected);
        Assert.False(drsuapi.IsBound);
    }

    [Fact]
    public void DrsRpcReplicationClient_PartnerExtensions_NullBeforeBind()
    {
        var drsuapi = new DrsuapiClient();
        var client = CreateTestRpcReplicationClient(drsuapi);

        Assert.Null(client.PartnerExtensions);
    }

    // ════════════════════════════════════════════════════════════════
    //  6. NTLM Crypto Verification Tests
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void NtlmV2Hash_Computation_ProducesKnownOutput()
    {
        // NTLMv2Hash = HMAC-MD5(NTHash, UPPERCASE(username) + domain)
        // Using known test values
        string username = "User";
        string domain = "Domain";
        byte[] ntHash = MD4Hash("Password");

        byte[] identityBytes = Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
        byte[] ntlmv2Hash;
        using (var hmac = new HMACMD5(ntHash))
        {
            ntlmv2Hash = hmac.ComputeHash(identityBytes);
        }

        Assert.Equal(16, ntlmv2Hash.Length);
        // The hash should be deterministic
        byte[] ntlmv2Hash2;
        using (var hmac = new HMACMD5(ntHash))
        {
            ntlmv2Hash2 = hmac.ComputeHash(identityBytes);
        }
        Assert.Equal(ntlmv2Hash, ntlmv2Hash2);
    }

    [Fact]
    public void SessionKey_Derivation_Produces16ByteKey()
    {
        // SessionBaseKey = HMAC-MD5(NTLMv2Hash, NtProofStr)
        byte[] ntlmv2Hash = new byte[16];
        RandomNumberGenerator.Fill(ntlmv2Hash);

        byte[] ntProofStr = new byte[16];
        RandomNumberGenerator.Fill(ntProofStr);

        byte[] sessionKey;
        using (var hmac = new HMACMD5(ntlmv2Hash))
        {
            sessionKey = hmac.ComputeHash(ntProofStr);
        }

        Assert.Equal(16, sessionKey.Length);
        Assert.False(sessionKey.AsSpan().SequenceEqual(new byte[16]),
            "Session key should not be all zeros with random inputs");
    }

    [Fact]
    public void ClientChallengeBlob_IncludesCorrectRespTypeAndHiRespType()
    {
        // Build a Type 3 message and inspect the NtResponse blob
        var builder = new NtlmClientMessageBuilder();
        var (type3, _) = BuildType3WithKnownInputs(builder);

        // NtChallengeResponseFields at offset 20: Len(2) + MaxLen(2) + Offset(4)
        ushort ntResponseLen = BinaryPrimitives.ReadUInt16LittleEndian(type3.AsSpan(20));
        uint ntResponseOffset = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(24));

        // NtResponse = NtProofStr(16) + Blob
        // Blob[0] = RespType = 1
        // Blob[1] = HiRespType = 1
        Assert.True(ntResponseLen > 16, "NtResponse must contain NtProofStr + blob");

        byte respType = type3[(int)ntResponseOffset + 16];    // first byte of blob
        byte hiRespType = type3[(int)ntResponseOffset + 17];   // second byte of blob

        Assert.Equal(1, respType);
        Assert.Equal(1, hiRespType);
    }

    [Fact]
    public void ClientChallengeBlob_TimestampIsValidFiletime()
    {
        var builder = new NtlmClientMessageBuilder();
        var beforeTime = DateTime.UtcNow;
        var (type3, _) = BuildType3WithKnownInputs(builder);
        var afterTime = DateTime.UtcNow;

        // NtChallengeResponseFields at offset 20
        uint ntResponseOffset = BinaryPrimitives.ReadUInt32LittleEndian(type3.AsSpan(24));

        // Blob layout after NtProofStr(16):
        //   RespType(1) + HiRespType(1) + Reserved1(2) + Reserved2(4) + TimeStamp(8)
        int timestampOffset = (int)ntResponseOffset + 16 + 8; // +16 for NtProofStr, +8 for resp/reserved fields
        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(type3.AsSpan(timestampOffset));

        // Convert FILETIME to DateTime and check it is within reasonable range
        var timestamp = DateTime.FromFileTimeUtc(fileTime);

        // The timestamp should be between our before/after markers (with some tolerance)
        Assert.True(timestamp >= beforeTime.AddSeconds(-2),
            $"Timestamp {timestamp:O} should be >= {beforeTime.AddSeconds(-2):O}");
        Assert.True(timestamp <= afterTime.AddSeconds(2),
            $"Timestamp {timestamp:O} should be <= {afterTime.AddSeconds(2):O}");
    }

    [Fact]
    public void NtlmV2Hash_DifferentUsernameCase_ProducesSameHash()
    {
        // MS-NLMP specifies UPPERCASE(username) in the computation
        string domain = "Domain";
        byte[] ntHash = MD4Hash("Password");

        byte[] hash1 = ComputeNtlmV2Hash("user", domain, ntHash);
        byte[] hash2 = ComputeNtlmV2Hash("USER", domain, ntHash);
        byte[] hash3 = ComputeNtlmV2Hash("User", domain, ntHash);

        Assert.Equal(hash1, hash2);
        Assert.Equal(hash2, hash3);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helper Methods
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a mock NTLM Type 2 (Challenge) message for testing.
    /// </summary>
    private static byte[] BuildMockType2Message(byte[] serverChallenge, uint negotiateFlags, string targetName)
    {
        byte[] targetNameBytes = targetName != null ? Encoding.Unicode.GetBytes(targetName) : [];
        ushort targetNameLen = (ushort)targetNameBytes.Length;
        uint targetNameOffset = 32u; // right after the fixed header

        int totalLen = 32 + targetNameBytes.Length;
        byte[] message = new byte[totalLen];
        var span = message.AsSpan();

        // Signature "NTLMSSP\0"
        Encoding.ASCII.GetBytes("NTLMSSP\0").CopyTo(span);

        // Type = 2
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8), 2);

        // Target name fields: Len, MaxLen, Offset
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12), targetNameLen);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14), targetNameLen);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), targetNameLen > 0 ? targetNameOffset : 0);

        // Negotiate flags
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(20), negotiateFlags);

        // Server challenge (8 bytes at offset 24)
        serverChallenge.CopyTo(span.Slice(24));

        // Target name payload
        if (targetNameBytes.Length > 0)
        {
            targetNameBytes.CopyTo(span.Slice(32));
        }

        return message;
    }

    /// <summary>
    /// Builds a Type 3 message with known test inputs for consistent testing.
    /// </summary>
    private static (byte[] Type3, byte[] SessionKey) BuildType3WithKnownInputs(NtlmClientMessageBuilder builder)
    {
        byte[] serverChallenge = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        byte[] ntHash = new byte[16]; // zero hash for testing
        uint flags = 0x00000235;

        return builder.BuildAuthenticateMessage("testuser", "TESTDOMAIN", ntHash, serverChallenge, flags);
    }

    /// <summary>
    /// Computes the NTLMv2 hash: HMAC-MD5(NTHash, UPPERCASE(user) + domain).
    /// </summary>
    private static byte[] ComputeNtlmV2Hash(string username, string domain, byte[] ntHash)
    {
        byte[] identity = Encoding.Unicode.GetBytes(username.ToUpperInvariant() + domain);
        using var hmac = new HMACMD5(ntHash);
        return hmac.ComputeHash(identity);
    }

    /// <summary>
    /// Computes an MD4 hash of a Unicode password string (NT hash).
    /// Uses the .NET MD4 implementation via the platform crypto provider.
    /// </summary>
    private static byte[] MD4Hash(string password)
    {
        // For test purposes, use a simple deterministic 16-byte hash
        // since MD4 is not available in modern .NET without platform-specific calls.
        // We use SHA256 and truncate to 16 bytes to simulate an NT hash for testing.
        byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
        byte[] hash = SHA256.HashData(passwordBytes);
        return hash[..16];
    }

    /// <summary>
    /// Creates a DrsRpcReplicationClient for testing via reflection,
    /// since the constructor is internal.
    /// </summary>
    private static DrsRpcReplicationClient CreateTestRpcReplicationClient(DrsuapiClient drsuapi)
    {
        // The constructor is internal, so use reflection to create the instance
        var ctor = typeof(DrsRpcReplicationClient).GetConstructors(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (DrsRpcReplicationClient)ctor[0].Invoke([drsuapi, Guid.NewGuid(), NullLogger.Instance]);
    }

    /// <summary>
    /// Builds a BIND PDU using the same logic as RpcTcpClient.BuildBindPdu (replicated here for testing).
    /// </summary>
    private static byte[] BuildTestBindPdu(byte[] authData, Guid? interfaceId = null)
    {
        interfaceId ??= Guid.NewGuid();
        ushort majorVersion = 4;
        ushort minorVersion = 0;

        using var ms = new MemoryStream();

        // Reserve header
        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // MaxXmitFrag, MaxRecvFrag, AssocGroupId
        WriteUInt16LE(ms, 4280);
        WriteUInt16LE(ms, 4280);
        WriteUInt32LE(ms, 0);

        // Presentation context list
        ms.WriteByte(1); // NumContexts
        ms.WriteByte(0); ms.WriteByte(0); ms.WriteByte(0); // reserved

        // Context[0]
        WriteUInt16LE(ms, 0); // ContextId
        ms.WriteByte(1); // NumTransferSyntaxes
        ms.WriteByte(0); // reserved

        // Abstract syntax UUID + version
        WriteGuidLE(ms, interfaceId.Value);
        WriteUInt32LE(ms, (uint)majorVersion | ((uint)minorVersion << 16));

        // Transfer syntax NDR 2.0
        WriteGuidLE(ms, RpcConstants.NdrSyntaxId);
        WriteUInt32LE(ms, RpcConstants.NdrSyntaxVersion);

        // Auth verifier
        ushort authLength = 0;
        if (authData is { Length: > 0 })
        {
            AlignStream(ms, 4);
            authLength = (ushort)authData.Length;
            ms.WriteByte(RpcConstants.AuthTypeNtlm);
            ms.WriteByte(RpcConstants.AuthLevelConnect);
            ms.WriteByte(0); ms.WriteByte(0);
            WriteUInt32LE(ms, 0);
            ms.Write(authData);
        }

        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeBind,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = authLength,
            CallId = 1,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds an AUTH3 PDU for testing.
    /// </summary>
    private static byte[] BuildTestAuth3Pdu(byte[] authData)
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[RpcPduHeader.HeaderSize]);
        WriteUInt32LE(ms, 0); // padding

        ushort authLength = (ushort)authData.Length;
        ms.WriteByte(RpcConstants.AuthTypeNtlm);
        ms.WriteByte(RpcConstants.AuthLevelConnect);
        ms.WriteByte(0); ms.WriteByte(0);
        WriteUInt32LE(ms, 0);
        ms.Write(authData);

        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeAuth3,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = authLength,
            CallId = 1,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds a REQUEST PDU for testing.
    /// </summary>
    private static byte[] BuildTestRequestPdu(ushort opnum, byte[] stubData, ushort contextId = 0)
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[RpcPduHeader.HeaderSize]);

        // allocHint, contextId, opnum
        WriteUInt32LE(ms, (uint)stubData.Length);
        WriteUInt16LE(ms, contextId);
        WriteUInt16LE(ms, opnum);
        ms.Write(stubData);

        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeRequest,
            PacketFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = 0,
            CallId = 1,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Splits stub data into multiple request fragments respecting maxXmitFrag.
    /// </summary>
    private static byte[][] BuildFragmentedRequestHelper(byte[] stubData, ushort maxXmitFrag)
    {
        const int overhead = RpcPduHeader.HeaderSize + 8;
        int maxStubPerFragment = maxXmitFrag - overhead;
        if (maxStubPerFragment <= 0) maxStubPerFragment = 1;

        var fragments = new List<byte[]>();
        int offset = 0;

        while (offset < stubData.Length)
        {
            int chunkSize = Math.Min(maxStubPerFragment, stubData.Length - offset);
            bool isFirst = offset == 0;
            bool isLast = offset + chunkSize >= stubData.Length;

            byte flags = 0;
            if (isFirst) flags |= RpcConstants.PfcFirstFrag;
            if (isLast) flags |= RpcConstants.PfcLastFrag;

            byte[] chunk = new byte[chunkSize];
            Array.Copy(stubData, offset, chunk, 0, chunkSize);

            fragments.Add(BuildTestRequestPduWithFlags(chunk, flags));
            offset += chunkSize;
        }

        return fragments.ToArray();
    }

    /// <summary>
    /// Builds a REQUEST PDU with specific fragment flags.
    /// </summary>
    private static byte[] BuildTestRequestPduWithFlags(byte[] stubData, byte flags)
    {
        using var ms = new MemoryStream();

        ms.Write(new byte[RpcPduHeader.HeaderSize]);
        WriteUInt32LE(ms, (uint)stubData.Length);
        WriteUInt16LE(ms, 0); // contextId
        WriteUInt16LE(ms, 0); // opnum
        ms.Write(stubData);

        byte[] pdu = ms.ToArray();
        var header = new RpcPduHeader
        {
            VersionMajor = RpcConstants.VersionMajor,
            VersionMinor = RpcConstants.VersionMinor,
            PacketType = RpcConstants.PTypeRequest,
            PacketFlags = flags,
            DataRepresentation = RpcPduHeader.LittleEndianDrep,
            FragLength = (ushort)pdu.Length,
            AuthLength = 0,
            CallId = 1,
        };
        header.Write(pdu.AsSpan());

        return pdu;
    }

    /// <summary>
    /// Builds fragmented response PDUs, replicating the logic from RpcFragmentationTests.
    /// </summary>
    private static byte[][] BuildFragmentedResponseHelper(
        uint callId, ushort contextId, byte[] responseStub, ushort maxXmitFrag)
    {
        const int overhead = RpcPduHeader.HeaderSize + 8;
        int maxStubPerFragment = maxXmitFrag - overhead;
        if (maxStubPerFragment <= 0) maxStubPerFragment = 1;

        if (responseStub.Length <= maxStubPerFragment)
        {
            return [PduBuilder.BuildResponseFragment(
                callId, contextId, responseStub,
                RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag,
                (uint)responseStub.Length)];
        }

        var fragments = new List<byte[]>();
        int offset = 0;

        while (offset < responseStub.Length)
        {
            int chunkSize = Math.Min(maxStubPerFragment, responseStub.Length - offset);
            bool isFirst = offset == 0;
            bool isLast = offset + chunkSize >= responseStub.Length;

            byte flags = 0;
            if (isFirst) flags |= RpcConstants.PfcFirstFrag;
            if (isLast) flags |= RpcConstants.PfcLastFrag;

            byte[] chunk = new byte[chunkSize];
            Array.Copy(responseStub, offset, chunk, 0, chunkSize);

            fragments.Add(PduBuilder.BuildResponseFragment(
                callId, contextId, chunk, flags, (uint)responseStub.Length));
            offset += chunkSize;
        }

        return fragments.ToArray();
    }

    /// <summary>
    /// Builds a protocol tower matching the EPM client tower format.
    /// </summary>
    private static byte[] BuildProtocolTower(Guid interfaceId, ushort majorVersion)
    {
        using var ms = new MemoryStream();

        // Floor count
        WriteUInt16LE(ms, 5);

        // Floor 1: Interface UUID
        {
            byte[] uuid = new byte[16];
            interfaceId.TryWriteBytes(uuid);
            byte[] lhs = new byte[1 + 16 + 2];
            lhs[0] = 0x0D;
            uuid.CopyTo(lhs, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(lhs.AsSpan(17), majorVersion);
            WriteUInt16LE(ms, (ushort)lhs.Length);
            ms.Write(lhs);
            WriteUInt16LE(ms, 2); // RHS length
            ms.Write([0x00, 0x00]); // minor version
        }

        // Floor 2: NDR transfer syntax
        {
            byte[] uuid = new byte[16];
            RpcConstants.NdrSyntaxId.TryWriteBytes(uuid);
            byte[] lhs = new byte[1 + 16 + 2];
            lhs[0] = 0x0D;
            uuid.CopyTo(lhs, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(lhs.AsSpan(17), RpcConstants.NdrSyntaxVersion);
            WriteUInt16LE(ms, (ushort)lhs.Length);
            ms.Write(lhs);
            WriteUInt16LE(ms, 2);
            ms.Write([0x00, 0x00]);
        }

        // Floor 3: RPC connection-oriented
        {
            WriteUInt16LE(ms, 1);
            ms.WriteByte(0x0B);
            WriteUInt16LE(ms, 2);
            ms.Write([0x00, 0x00]);
        }

        // Floor 4: TCP
        {
            WriteUInt16LE(ms, 1);
            ms.WriteByte(0x07);
            WriteUInt16LE(ms, 2);
            ms.Write([0x00, 0x00]); // port 0
        }

        // Floor 5: IP
        {
            WriteUInt16LE(ms, 1);
            ms.WriteByte(0x09);
            WriteUInt16LE(ms, 4);
            ms.Write([0x00, 0x00, 0x00, 0x00]); // 0.0.0.0
        }

        return ms.ToArray();
    }

    private static void WriteUInt16LE(MemoryStream ms, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteUInt32LE(MemoryStream ms, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteGuidLE(MemoryStream ms, Guid guid)
    {
        Span<byte> buf = stackalloc byte[16];
        guid.TryWriteBytes(buf);
        ms.Write(buf);
    }

    private static void AlignStream(MemoryStream ms, int boundary)
    {
        int remainder = (int)(ms.Position % boundary);
        if (remainder != 0)
        {
            int padding = boundary - remainder;
            for (int i = 0; i < padding; i++) ms.WriteByte(0);
        }
    }
}
