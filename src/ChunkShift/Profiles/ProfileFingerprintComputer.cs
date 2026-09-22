using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using ChunkShift.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Profiles;

/// <summary>
/// Computes a profile fingerprint from canonical semantic parameters rather than authoring-artifact bytes.
/// </summary>
internal static class ProfileFingerprintComputer
{
    private static ReadOnlySpan<byte> Domain => "chunkshift.profile-fingerprint.v1\0"u8;

    public static ProfileFingerprint Compute(HashSuiteId hashSuite, ReadOnlySpan<byte> profileArtifactUtf8)
    {
        using var document = JsonDocument.Parse(profileArtifactUtf8.ToArray());

        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("semantics", out var semantics) ||
            semantics.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Profile artifact must contain a top-level 'semantics' object.");
        }

        var writer = new ArrayBufferWriter<byte>();
        WriteBytes(writer, Domain);
        WriteElement(writer, semantics);

        return new ProfileFingerprint(HashSuiteHasher.Hash(hashSuite, writer.WrittenSpan));
    }

    private static void WriteElement(ArrayBufferWriter<byte> writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                WriteByte(writer, 0x00);
                return;

            case JsonValueKind.False:
                WriteByte(writer, 0x01);
                return;

            case JsonValueKind.True:
                WriteByte(writer, 0x02);
                return;

            case JsonValueKind.Number:
                WriteByte(writer, 0x03);
                WriteString(writer, CanonicalizeInteger(element));
                return;

            case JsonValueKind.String:
                WriteByte(writer, 0x04);
                WriteString(writer, element.GetString() ?? string.Empty);
                return;

            case JsonValueKind.Array:
                WriteByte(writer, 0x05);
                WriteArray(writer, element);
                return;

            case JsonValueKind.Object:
                WriteByte(writer, 0x06);
                WriteObject(writer, element);
                return;

            default:
                throw new FormatException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private static void WriteArray(ArrayBufferWriter<byte> writer, JsonElement element)
    {
        var items = element.EnumerateArray().ToArray();
        WriteUInt32(writer, checked((uint)items.Length));

        foreach (var item in items)
        {
            WriteElement(writer, item);
        }
    }

    private static void WriteObject(ArrayBufferWriter<byte> writer, JsonElement element)
    {
        var properties = element.EnumerateObject().ToArray();
        Array.Sort(properties, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        for (int i = 1; i < properties.Length; i++)
        {
            if (string.Equals(properties[i - 1].Name, properties[i].Name, StringComparison.Ordinal))
            {
                throw new FormatException($"Duplicate semantic property '{properties[i].Name}'.");
            }
        }

        WriteUInt32(writer, checked((uint)properties.Length));

        foreach (var property in properties)
        {
            WriteString(writer, property.Name);
            WriteElement(writer, property.Value);
        }
    }

    private static string CanonicalizeInteger(JsonElement element)
    {
        string raw = element.GetRawText();

        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ||
            decimal.Truncate(value) != value)
        {
            throw new FormatException($"Profile semantic number '{raw}' must be an exact integer.");
        }

        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteUInt32(writer, checked((uint)byteCount));

        Span<byte> destination = writer.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value.AsSpan(), destination);
        writer.Advance(written);
    }

    private static void WriteUInt32(ArrayBufferWriter<byte> writer, uint value)
    {
        Span<byte> destination = writer.GetSpan(sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        writer.Advance(sizeof(uint));
    }

    private static void WriteByte(ArrayBufferWriter<byte> writer, byte value)
    {
        Span<byte> destination = writer.GetSpan(1);
        destination[0] = value;
        writer.Advance(1);
    }

    private static void WriteBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        value.CopyTo(writer.GetSpan(value.Length));
        writer.Advance(value.Length);
    }
}
