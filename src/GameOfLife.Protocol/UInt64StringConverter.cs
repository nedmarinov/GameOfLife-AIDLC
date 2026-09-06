using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOfLife.Protocol;

/// <summary>
/// Serialises <see cref="ulong"/> as a JSON string, and accepts either form on
/// the way in.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a correctness guard, not a style choice.</b> JSON numbers are
/// IEEE-754 doubles in most implementations, which hold integers exactly only
/// up to 2^53. A universe coordinate can be anything up to 2^64 - 1, so writing
/// one as a number silently rounds it:
/// </para>
/// <code>
/// 18446744073709551615  ->  18446744073709552000   (a different cell)
/// </code>
/// <para>
/// The corruption is silent, survives a round trip through a permissive parser,
/// and only appears as cells drawn in the wrong place — far from the code that
/// caused it. Emitting a string removes the failure mode entirely. Reading
/// accepts numbers too, so a hand-written message still parses, but anything
/// above 2^53 has already lost precision before it reaches us and there is no
/// way to recover it here.
/// </para>
/// </remarks>
public sealed class UInt64StringConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();

            if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                throw new JsonException($"Expected an unsigned 64-bit integer, got '{text}'.");

            return parsed;
        }

        if (reader.TokenType == JsonTokenType.Number) return reader.GetUInt64();

        throw new JsonException($"Expected a string or number for a coordinate, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
