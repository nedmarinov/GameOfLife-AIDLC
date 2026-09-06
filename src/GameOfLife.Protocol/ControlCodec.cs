using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOfLife.Protocol;

/// <summary>Serialises control messages to and from UTF-8 JSON.</summary>
public static class ControlCodec
{
    /// <summary>
    /// Shared options.
    /// </summary>
    /// <remarks>
    /// camelCase on the wire, enums as names rather than ordinals — so
    /// reordering an enum cannot silently change a message's meaning — and
    /// case-insensitive reading, so a hand-typed message still parses.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Write(IBufferWriter<byte> writer, ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(message);

        using var json = new Utf8JsonWriter(writer);
        JsonSerializer.Serialize(json, message, Options);
    }

    /// <summary>Serialises a message into a whole <see cref="FrameType.Control"/> frame.</summary>
    public static void WriteFrame(IBufferWriter<byte> writer, ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var payload = new ArrayBufferWriter<byte>(256);
        Write(payload, message);

        FrameCodec.Write(writer, FrameType.Control, payload.WrittenSpan);
    }

    public static byte[] Serialise(ProtocolMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        Write(buffer, message);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Parses a control payload.</summary>
    /// <exception cref="ProtocolException">The payload is not a message this peer understands.</exception>
    public static ProtocolMessage Read(ReadOnlySpan<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<ProtocolMessage>(payload, Options)
                ?? throw new ProtocolException("Control frame contained a JSON null.");
        }
        catch (JsonException error)
        {
            // A malformed message is the peer's fault, not an internal one, so
            // it surfaces as a protocol error the connection can answer with an
            // ErrorMessage rather than as a serializer exception.
            throw new ProtocolException($"Malformed control message: {error.Message}");
        }
        catch (NotSupportedException error)
        {
            throw new ProtocolException($"Unknown control message: {error.Message}");
        }
    }
}
