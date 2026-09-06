using System.Buffers;
using System.Text;
using System.Text.Json;
using GameOfLife.Core;
using GameOfLife.Protocol;

namespace GameOfLife.Protocol.Tests;

public class MessageTests
{
    private static string Json(ProtocolMessage message) =>
        Encoding.UTF8.GetString(ControlCodec.Serialise(message));

    private static T RoundTrip<T>(T message) where T : ProtocolMessage =>
        Assert.IsType<T>(ControlCodec.Read(ControlCodec.Serialise(message)));

    // ------------------------------------------------- the precision guard

    /// <summary>
    /// The regression guard for the defect this protocol is most likely to have.
    /// </summary>
    /// <remarks>
    /// JSON numbers are IEEE-754 doubles, exact only to 2^53. A coordinate
    /// written as a number would round to a different cell, silently, and the
    /// symptom would appear as cells drawn in the wrong place far from the
    /// cause.
    /// </remarks>
    [Fact]
    public void Coordinates_Above_Two_To_The_Fifty_Three_Survive_A_Round_Trip()
    {
        ulong[] hostile =
        [
            ulong.MaxValue,
            ulong.MaxValue - 1,
            (1UL << 53) + 1,
            (1UL << 63) + 12345,
            9_007_199_254_740_993,
        ];

        foreach (ulong value in hostile)
        {
            ToggleMessage read = RoundTrip(new ToggleMessage { X = value, Y = value });

            Assert.Equal(value, read.X);
            Assert.Equal(value, read.Y);
        }
    }

    [Fact]
    public void Coordinates_Are_Written_As_Strings_Not_Numbers()
    {
        string json = Json(new ToggleMessage { X = ulong.MaxValue, Y = 0 });

        Assert.Contains("\"x\":\"18446744073709551615\"", json);
        Assert.Contains("\"y\":\"0\"", json);
        Assert.DoesNotContain("\"x\":18446744073709551615", json);
    }

    [Fact]
    public void A_Number_Would_Have_Lost_Precision()
    {
        // Demonstrates the bug being prevented rather than merely asserting the
        // fix. Read from an array so the compiler cannot fold it away — and
        // note it already refuses to compile the ulong.MaxValue case as a
        // constant, which is the same complaint at compile time.
        ulong[] values = [(1UL << 53) + 1];
        ulong original = values[0];

        ulong throughDouble = (ulong)(double)original;

        Assert.NotEqual(original, throughDouble);
        Assert.Equal(1UL << 53, throughDouble);   // rounded down to the nearest representable double

        // The string path keeps it exactly.
        Assert.Equal(original, RoundTrip(new ToggleMessage { X = original }).X);
    }

    [Fact]
    public void Reader_Still_Accepts_A_Hand_Written_Numeric_Coordinate()
    {
        ProtocolMessage message = ControlCodec.Read(
            Encoding.UTF8.GetBytes("""{"op":"toggle","x":42,"y":7}"""));

        ToggleMessage toggle = Assert.IsType<ToggleMessage>(message);
        Assert.Equal(42UL, toggle.X);
        Assert.Equal(7UL, toggle.Y);
    }

    [Fact]
    public void Reader_Rejects_A_Non_Numeric_Coordinate_String()
    {
        Assert.Throws<ProtocolException>(() => ControlCodec.Read(
            Encoding.UTF8.GetBytes("""{"op":"toggle","x":"not a number","y":"1"}""")));
    }

    // ------------------------------------------------------ message shapes

    [Fact]
    public void Round_Trips_Every_Client_Message()
    {
        Assert.Equal(1UL << 63, RoundTrip(new SubscribeMessage
        {
            OriginX = 1UL << 63,
            OriginY = 99,
            Width = 100,
            Height = 100,
        }).OriginX);

        Assert.Equal(-5, RoundTrip(new PanMessage { Dx = -5, Dy = 3 }).Dx);
        Assert.Equal(ControlAction.Speed, RoundTrip(new ControlMessage
        {
            Action = ControlAction.Speed,
            Value = 50,
        }).Action);

        Assert.Equal("patterns/glider.rle", RoundTrip(new LoadMessage
        {
            File = "patterns/glider.rle",
        }).File);

        Assert.Equal("out.rle", RoundTrip(new SaveMessage { File = "out.rle" }).File);
    }

    [Fact]
    public void Round_Trips_Every_Server_Message()
    {
        HelloMessage hello = RoundTrip(new HelloMessage
        {
            Generation = ulong.MaxValue,
            Running = true,
            TickMilliseconds = 100,
            Population = 36,
        });

        Assert.Equal(ulong.MaxValue, hello.Generation);
        Assert.Equal(ProtocolConstants.Version, hello.ProtocolVersion);

        Assert.Equal(36, RoundTrip(new StatusMessage { Population = 36 }).Population);

        ErrorMessage error = RoundTrip(new ErrorMessage
        {
            Code = ErrorCode.EditWhileRunning,
            Message = "Pause before editing.",
        });

        Assert.Equal(ErrorCode.EditWhileRunning, error.Code);
    }

    [Fact]
    public void Discriminator_Is_The_Op_Field()
    {
        Assert.Contains("\"op\":\"toggle\"", Json(new ToggleMessage()));
        Assert.Contains("\"op\":\"hello\"", Json(new HelloMessage()));
    }

    /// <summary>
    /// Enums travel as names, so reordering the enum cannot silently change
    /// what a message means to an older peer.
    /// </summary>
    [Fact]
    public void Enums_Are_Written_As_Names_Not_Ordinals()
    {
        Assert.Contains("\"action\":\"pause\"",
            Json(new ControlMessage { Action = ControlAction.Pause }));

        Assert.Contains("\"code\":\"editWhileRunning\"",
            Json(new ErrorMessage { Code = ErrorCode.EditWhileRunning, Message = "no" }));
    }

    [Fact]
    public void Field_Names_Are_Read_Case_Insensitively()
    {
        ProtocolMessage message = ControlCodec.Read(
            Encoding.UTF8.GetBytes("""{"op":"subscribe","OriginX":"5","originy":"6","WIDTH":10,"height":20}"""));

        SubscribeMessage subscribe = Assert.IsType<SubscribeMessage>(message);
        Assert.Equal(5UL, subscribe.OriginX);
        Assert.Equal(6UL, subscribe.OriginY);
        Assert.Equal(10, subscribe.Width);
    }

    // --------------------------------------------------------- malformed in

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("""{"op":"nonexistent"}""")]
    [InlineData("""{"x":1,"y":2}""")]
    [InlineData("null")]
    public void Malformed_Input_Raises_A_Protocol_Error_Not_A_Serializer_Exception(string text)
    {
        // The connection layer answers a ProtocolException with an ErrorMessage.
        // A raw JsonException escaping would read as an internal fault instead
        // of a peer's mistake.
        Assert.Throws<ProtocolException>(() => ControlCodec.Read(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public void Control_Messages_Fit_In_A_Frame_And_Come_Back_Out()
    {
        var buffer = new ArrayBufferWriter<byte>();
        ControlCodec.WriteFrame(buffer, new ToggleMessage { X = ulong.MaxValue, Y = 1 });

        var sequence = new ReadOnlySequence<byte>(buffer.WrittenSpan.ToArray());

        Assert.True(FrameCodec.TryRead(ref sequence, out FrameType type, out byte[] payload));
        Assert.Equal(FrameType.Control, type);

        ToggleMessage toggle = Assert.IsType<ToggleMessage>(ControlCodec.Read(payload));
        Assert.Equal(ulong.MaxValue, toggle.X);
    }
}
