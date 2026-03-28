using System.Collections.Concurrent;
using System.Buffers.Binary;

namespace Directory.Rpc.Dispatch;

/// <summary>
/// Thread-safe table for managing RPC context handles.
/// Context handles are 20-byte values (4 bytes attributes + 16 bytes GUID) that
/// allow stateful conversations between client and server across multiple calls.
/// </summary>
public class ContextHandleTable
{
    private readonly ConcurrentDictionary<Guid, ContextHandleEntry> _handles = new();

    /// <summary>
    /// Creates a new context handle storing the given value.
    /// Returns the 20-byte handle (4 bytes attributes=0 + 16 bytes Guid).
    /// </summary>
    public byte[] CreateHandle<T>(T value) where T : notnull
    {
        var guid = Guid.NewGuid();
        var entry = new ContextHandleEntry(value, DateTimeOffset.UtcNow);

        if (!_handles.TryAdd(guid, entry))
        {
            throw new InvalidOperationException("Context handle GUID collision.");
        }

        return EncodeHandle(0, guid);
    }

    /// <summary>
    /// Retrieves the value stored under the given 20-byte context handle.
    /// </summary>
    public T GetHandle<T>(ReadOnlySpan<byte> handle) where T : class
    {
        var guid = DecodeGuid(handle);

        if (_handles.TryGetValue(guid, out var entry))
        {
            return entry.Value as T;
        }

        return null;
    }

    /// <summary>
    /// Removes and returns the context handle entry. Returns true if it existed.
    /// </summary>
    public bool CloseHandle(ReadOnlySpan<byte> handle)
    {
        var guid = DecodeGuid(handle);
        return _handles.TryRemove(guid, out _);
    }

    /// <summary>
    /// Checks if a context handle is all zeros (null handle).
    /// </summary>
    public static bool IsNullHandle(ReadOnlySpan<byte> handle)
    {
        if (handle.Length < 20)
            return true;

        for (int i = 0; i < 20; i++)
        {
            if (handle[i] != 0) return false;
        }

        return true;
    }

    public int Count => _handles.Count;

    private static byte[] EncodeHandle(uint attributes, Guid guid)
    {
        var handle = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(handle.AsSpan(0, 4), attributes);
        guid.TryWriteBytes(handle.AsSpan(4, 16));
        return handle;
    }

    private static Guid DecodeGuid(ReadOnlySpan<byte> handle)
    {
        if (handle.Length < 20)
            throw new ArgumentException("Context handle must be at least 20 bytes.");

        return new Guid(handle.Slice(4, 16));
    }
}

/// <summary>
/// An entry in the context handle table, wrapping the stored value with metadata.
/// </summary>
public record ContextHandleEntry(object Value, DateTimeOffset CreatedAt);
