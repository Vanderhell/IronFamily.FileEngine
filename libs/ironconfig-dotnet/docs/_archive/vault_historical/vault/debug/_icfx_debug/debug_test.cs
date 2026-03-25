using System;
using System.Collections.Generic;

// Read the ICFX file and print the payload structure
byte[] data = System.IO.File.ReadAllBytes("array_of_objects.icfx");

// Skip to payload offset
uint payloadOffset = BitConverter.ToUInt32(data, 24);
byte[] payload = data[(int)payloadOffset..];

Console.WriteLine("Payload bytes:");
for (int i = 0; i < Math.Min(50, payload.Length); i++)
{
    Console.Write($"{payload[i]:02x} ");
    if ((i + 1) % 16 == 0)
        Console.WriteLine($"(offset {i + 1})");
}
Console.WriteLine();

// Parse array structure
int pos = 0;
Console.WriteLine($"\nOffset {pos}: {payload[pos]:02x} = array type");
pos++;

Console.WriteLine($"Offset {pos}: {payload[pos]:02x} = array count");
pos++;

// Parse varuint offsets
for (int i = 0; i < 2; i++)
{
    uint offset = 0;
    int shift = 0;
    while (payload[pos] >= 0x80)
    {
        offset |= (uint)(payload[pos] & 0x7F) << shift;
        shift += 7;
        pos++;
    }
    offset |= (uint)payload[pos] << shift;
    pos++;
    Console.WriteLine($"Offset {i}: {offset} (bytes used: check below)");
}

Console.WriteLine($"\nFirst element starts at payload offset {payload[2]}:");
for (int i = payload[2]; i < Math.Min(payload[2] + 20, payload.Length); i++)
{
    Console.Write($"{payload[i]:02x} ");
}
Console.WriteLine();
