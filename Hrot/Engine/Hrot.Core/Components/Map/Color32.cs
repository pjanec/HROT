using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hrot.IG.Components;

[StructLayout(LayoutKind.Sequential, Size = 4)]
[JsonConverter(typeof(Color32ArrayConverter))]
public struct Color32
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;

    public Color32(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}

public sealed class Color32ArrayConverter : JsonConverter<Color32>
{
    public override Color32 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read(); byte r = reader.GetByte();
        reader.Read(); byte g = reader.GetByte();
        reader.Read(); byte b = reader.GetByte();
        reader.Read(); byte a = reader.GetByte();
        reader.Read();
        return new Color32(r, g, b, a);
    }

    public override void Write(Utf8JsonWriter writer, Color32 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.R);
        writer.WriteNumberValue(value.G);
        writer.WriteNumberValue(value.B);
        writer.WriteNumberValue(value.A);
        writer.WriteEndArray();
    }
}
