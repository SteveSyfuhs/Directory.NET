using System.Text.Json.Serialization;

namespace Directory.Security.Radius;

public class RadiusSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; } = 1812;

    [JsonPropertyName("accountingPort")]
    public int AccountingPort { get; set; } = 1813;

    [JsonPropertyName("clients")]
    public List<RadiusClient> Clients { get; set; } = new();
}

public class RadiusClient
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonPropertyName("sharedSecret")]
    public string SharedSecret { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

public class RadiusLogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("clientIp")]
    public string ClientIp { get; set; } = string.Empty;

    [JsonPropertyName("clientName")]
    public string ClientName { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; }
}

/// <summary>
/// Stored on the domain root as msDS-RadiusData.
/// </summary>
public class RadiusData
{
    [JsonPropertyName("settings")]
    public RadiusSettings Settings { get; set; } = new();

    [JsonPropertyName("log")]
    public List<RadiusLogEntry> Log { get; set; } = new();
}

// ── RADIUS Protocol Constants (RFC 2865) ─────────────────────

public static class RadiusConstants
{
    // Packet codes
    public const byte AccessRequest = 1;
    public const byte AccessAccept = 2;
    public const byte AccessReject = 3;

    // Attribute types
    public const byte AttrUserName = 1;
    public const byte AttrUserPassword = 2;
    public const byte AttrNasIpAddress = 4;
    public const byte AttrNasPort = 5;
    public const byte AttrReplyMessage = 18;
    public const byte AttrSessionTimeout = 27;
    public const byte AttrFramedIpAddress = 8;

    public const int MaxLogEntries = 500;
    public const int HeaderLength = 20;
    public const int AuthenticatorLength = 16;
    public const int MinPacketLength = 20;
    public const int MaxPacketLength = 4096;
}
