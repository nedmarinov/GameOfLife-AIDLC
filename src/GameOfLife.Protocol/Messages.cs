using System.Text.Json.Serialization;

namespace GameOfLife.Protocol;

/// <summary>
/// A control message. Carried as UTF-8 JSON in a
/// <see cref="FrameType.Control"/> frame.
/// </summary>
/// <remarks>
/// Control traffic stays JSON rather than binary so the wire is readable: a
/// reviewer can follow a whole session with <c>tcpdump</c> and no decoder, and
/// adding a field later does not break an older peer. Only the per-generation
/// frame — the hot path — is binary. See ADR 0002.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "op", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(SubscribeMessage), "subscribe")]
[JsonDerivedType(typeof(ToggleMessage), "toggle")]
[JsonDerivedType(typeof(PanMessage), "pan")]
[JsonDerivedType(typeof(ZoomMessage), "zoom")]
[JsonDerivedType(typeof(ControlMessage), "control")]
[JsonDerivedType(typeof(LoadMessage), "load")]
[JsonDerivedType(typeof(SaveMessage), "save")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(StatusMessage), "status")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record ProtocolMessage;

// ------------------------------------------------------------ client -> server

/// <summary>Sets the window of the universe this client wants to receive.</summary>
public sealed record SubscribeMessage : ProtocolMessage
{
    [JsonConverter(typeof(UInt64StringConverter))]
    public ulong OriginX { get; init; }

    [JsonConverter(typeof(UInt64StringConverter))]
    public ulong OriginY { get; init; }

    public int Width { get; init; } = 100;

    public int Height { get; init; } = 100;

    /// <summary>Universe cells per displayed cell, as a power of two. 0 is 1:1.</summary>
    public int Zoom { get; init; }
}

/// <summary>
/// Changes how much of the universe the window covers, keeping its centre fixed.
/// </summary>
/// <remarks>
/// Positive zooms out, negative zooms in. The server clamps to the range the
/// window can express, so a client may send a large delta to jump straight to
/// either extreme without knowing the limit.
/// </remarks>
public sealed record ZoomMessage : ProtocolMessage
{
    public int Delta { get; init; }
}

/// <summary>Flips one cell. Absolute coordinates, so it is viewport-independent.</summary>
public sealed record ToggleMessage : ProtocolMessage
{
    [JsonConverter(typeof(UInt64StringConverter))]
    public ulong X { get; init; }

    [JsonConverter(typeof(UInt64StringConverter))]
    public ulong Y { get; init; }
}

/// <summary>Moves this client's viewport by a signed delta, wrapping.</summary>
public sealed record PanMessage : ProtocolMessage
{
    public long Dx { get; init; }

    public long Dy { get; init; }
}

public enum ControlAction
{
    Start,
    Pause,
    Step,
    Clear,
    Speed,
}

/// <summary>Starts, pauses, single-steps, clears, or re-speeds the simulation.</summary>
public sealed record ControlMessage : ProtocolMessage
{
    public ControlAction Action { get; init; }

    /// <summary>Tick interval in milliseconds. Only read for <see cref="ControlAction.Speed"/>.</summary>
    public int Value { get; init; }
}

public sealed record LoadMessage : ProtocolMessage
{
    public required string File { get; init; }
}

public sealed record SaveMessage : ProtocolMessage
{
    public required string File { get; init; }
}

// ------------------------------------------------------------ server -> client

/// <summary>Sent once on connect, so a client can render before the first tick.</summary>
public sealed record HelloMessage : ProtocolMessage
{
    public int ProtocolVersion { get; init; } = ProtocolConstants.Version;

    [JsonConverter(typeof(UInt64StringConverter))]
    public ulong Generation { get; init; }

    public bool Running { get; init; }

    public int TickMilliseconds { get; init; }

    public int Population { get; init; }
}

/// <summary>Sent when simulation state changes in a way a frame does not convey.</summary>
public sealed record StatusMessage : ProtocolMessage
{
    [JsonConverter(typeof(UInt64StringConverter))]
    public ulong Generation { get; init; }

    public bool Running { get; init; }

    public int TickMilliseconds { get; init; }

    public int Population { get; init; }
}

/// <summary>
/// A rejected request.
/// </summary>
/// <remarks>
/// Rejections are explicit rather than silent. A toggle sent while the
/// simulation is running comes back as <see cref="ErrorCode.EditWhileRunning"/>
/// so the user sees why nothing happened, instead of concluding the edit was
/// lost.
/// </remarks>
public sealed record ErrorMessage : ProtocolMessage
{
    public ErrorCode Code { get; init; }

    public required string Message { get; init; }
}

public enum ErrorCode
{
    Unknown,
    MalformedMessage,
    EditWhileRunning,
    InvalidViewport,
    FileNotFound,
    FileError,
}

public static class ProtocolConstants
{
    /// <summary>Bumped whenever a change would confuse an older peer.</summary>
    public const int Version = 2;

    public const int DefaultPort = 5150;

    public const int DefaultViewportSize = 100;

    public const int DefaultTickMilliseconds = 100;
}
