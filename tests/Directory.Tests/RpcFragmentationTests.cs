using System.Buffers.Binary;
using Directory.Rpc.Protocol;
using Directory.Rpc.Transport;
using Xunit;

namespace Directory.Tests;

/// <summary>
/// Tests for RPC PDU fragmentation: fragment flags, reassembly buffer,
/// BuildResponseFragment splitting, and round-trip correctness.
/// </summary>
public class RpcFragmentationTests
{
    // ────────────────────────────────────────────────────────────────
    // Fragment flag constants
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleFragment_HasBothFirstAndLastFlags()
    {
        byte bothFlags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag;
        Assert.Equal(0x03, bothFlags);
    }

    // ────────────────────────────────────────────────────────────────
    // FragmentReassemblyBuffer
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FragmentReassemblyBuffer_AccumulatesData()
    {
        var buffer = new FragmentReassemblyBuffer();
        var data1 = new byte[] { 0x01, 0x02, 0x03 };
        var data2 = new byte[] { 0x04, 0x05 };

        buffer.StubData.Write(data1);
        buffer.StubData.Write(data2);

        Assert.Equal(5, buffer.StubData.Length);
    }

    [Fact]
    public void FragmentReassemblyBuffer_StoresOpnumAndContextId()
    {
        var buffer = new FragmentReassemblyBuffer
        {
            Opnum = 42,
            ContextId = 7,
        };

        Assert.Equal((ushort)42, buffer.Opnum);
        Assert.Equal((ushort)7, buffer.ContextId);
    }

    // ────────────────────────────────────────────────────────────────
    // BuildResponseFragment — single chunk
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildResponseFragment_SmallResponse_ReturnsSingleFragment()
    {
        // A response smaller than MaxXmitFrag should fit in one fragment.
        // BuildResponse produces a single PDU with both FIRST and LAST flags.
        var stubData = new byte[100];
        byte flags = RpcConstants.PfcFirstFrag | RpcConstants.PfcLastFrag;

        var fragment = PduBuilder.BuildResponseFragment(callId: 1, contextId: 0, stubData, flags, allocHint: (uint)stubData.Length);

        Assert.NotNull(fragment);
        // The fragment should be header (16) + response fields (8) + stub (100) = 124 bytes
        Assert.Equal(124, fragment.Length);

        // Verify the flags in the PDU header (byte offset 3)
        var header = RpcPduHeader.Read(fragment);
        Assert.Equal(flags, header.PacketFlags);
    }

    // ────────────────────────────────────────────────────────────────
    // BuildResponseFragment — multi-fragment splitting
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildResponseFragment_LargeResponse_ProducesMultipleFragments()
    {
        // Simulate BuildFragmentedResponse logic: if stub > maxStubPerFragment, split
        ushort maxXmitFrag = 100;
        const int responseHeaderOverhead = 16 + 8; // RpcPduHeader.HeaderSize + 8
        int maxStubPerFragment = maxXmitFrag - responseHeaderOverhead; // 76 bytes

        var responseStub = new byte[200]; // larger than 76

        var fragments = BuildFragmentedResponseHelper(callId: 1, contextId: 0, responseStub, maxXmitFrag);

        Assert.True(fragments.Length > 1, "Large response should produce multiple fragments");
    }

    [Fact]
    public void FirstFragment_HasPfcFirstFragOnly()
    {
        ushort maxXmitFrag = 100;
        var responseStub = new byte[200];

        var fragments = BuildFragmentedResponseHelper(callId: 1, contextId: 0, responseStub, maxXmitFrag);

        var firstHeader = RpcPduHeader.Read(fragments[0]);
        Assert.Equal(RpcConstants.PfcFirstFrag, firstHeader.PacketFlags);
    }

    [Fact]
    public void LastFragment_HasPfcLastFragOnly()
    {
        ushort maxXmitFrag = 100;
        var responseStub = new byte[200];

        var fragments = BuildFragmentedResponseHelper(callId: 1, contextId: 0, responseStub, maxXmitFrag);

        var lastHeader = RpcPduHeader.Read(fragments[^1]);
        Assert.Equal(RpcConstants.PfcLastFrag, lastHeader.PacketFlags);
    }

    [Fact]
    public void MiddleFragments_HaveNoFragFlags()
    {
        ushort maxXmitFrag = 50;
        const int responseHeaderOverhead = 16 + 8;
        int maxStubPerFragment = maxXmitFrag - responseHeaderOverhead; // 26 bytes
        // Need at least 3 fragments: stub size > 2 * maxStubPerFragment
        var responseStub = new byte[maxStubPerFragment * 3 + 1]; // 79 bytes -> 4 fragments

        var fragments = BuildFragmentedResponseHelper(callId: 1, contextId: 0, responseStub, maxXmitFrag);

        Assert.True(fragments.Length >= 3, $"Expected at least 3 fragments, got {fragments.Length}");

        // Check middle fragments (not first, not last)
        for (int i = 1; i < fragments.Length - 1; i++)
        {
            var header = RpcPduHeader.Read(fragments[i]);
            Assert.Equal(0, header.PacketFlags);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Fragment reassembly round-trip
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FragmentStubData_Concatenated_EqualsOriginal()
    {
        ushort maxXmitFrag = 80;
        var original = new byte[250];
        Random.Shared.NextBytes(original);

        var fragments = BuildFragmentedResponseHelper(callId: 1, contextId: 0, original, maxXmitFrag);

        // Reassemble: each fragment has 24-byte header+response fields overhead, rest is stub
        const int overhead = 16 + 8;
        using var reassembled = new MemoryStream();
        foreach (var frag in fragments)
        {
            reassembled.Write(frag, overhead, frag.Length - overhead);
        }

        Assert.Equal(original, reassembled.ToArray());
    }

    // ────────────────────────────────────────────────────────────────
    // PDU header size
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void PduHeader_Is16Bytes()
    {
        Assert.Equal(16, RpcPduHeader.HeaderSize);
    }

    [Fact]
    public void ResponseHeaderOverhead_Is24Bytes()
    {
        // Response PDU = 16 (header) + 8 (allocHint + contextId + cancelCount + reserved) = 24
        const int responseHeaderOverhead = RpcPduHeader.HeaderSize + 8;
        Assert.Equal(24, responseHeaderOverhead);
    }

    // ────────────────────────────────────────────────────────────────
    // Helper: replicates BuildFragmentedResponse from RpcConnectionHandler
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replicates the BuildFragmentedResponse logic from RpcConnectionHandler
    /// so we can test fragmentation without needing a full RPC connection.
    /// </summary>
    private static byte[][] BuildFragmentedResponseHelper(
        uint callId, ushort contextId, byte[] responseStub, ushort maxXmitFrag)
    {
        const int responseHeaderOverhead = RpcPduHeader.HeaderSize + 8;
        int maxStubPerFragment = maxXmitFrag - responseHeaderOverhead;

        if (maxStubPerFragment <= 0)
            maxStubPerFragment = 1;

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
            bool isFirst = (offset == 0);
            bool isLast = (offset + chunkSize >= responseStub.Length);

            byte flags = 0;
            if (isFirst) flags |= RpcConstants.PfcFirstFrag;
            if (isLast) flags |= RpcConstants.PfcLastFrag;

            var chunk = new byte[chunkSize];
            Array.Copy(responseStub, offset, chunk, 0, chunkSize);

            fragments.Add(PduBuilder.BuildResponseFragment(
                callId, contextId, chunk, flags, (uint)responseStub.Length));
            offset += chunkSize;
        }

        return fragments.ToArray();
    }
}
