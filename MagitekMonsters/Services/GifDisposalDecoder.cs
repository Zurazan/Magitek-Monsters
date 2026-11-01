using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using static MagitekMonsters.Services.GifSpriteDecoder;

namespace MagitekMonsters.Services;

internal static class GifDisposalDecoder
{



    public static Animation Decode(byte[] data, SixLabors.ImageSharp.Configuration configuration, int maximumDimension, int maximumFrames, CancellationToken token = default, long maximumPixelBytes = long.MaxValue)
    {
        token.ThrowIfCancellationRequested();
        using var input = new MemoryStream(data, false);
        using var reader = new BinaryReader(input);
        var header = Read(reader, 13);
        if (!header.AsSpan(0, 6).SequenceEqual("GIF89a"u8) && !header.AsSpan(0, 6).SequenceEqual("GIF87a"u8))
            throw new InvalidDataException("Invalid sprite GIF header.");
        var width = header[6] | header[7] << 8;
        var height = header[8] | header[9] << 8;
        if (width <= 0 || height <= 0 || width > maximumDimension || height > maximumDimension)
            throw new InvalidDataException("The animated sprite dimensions are invalid.");
        var palette = Read(reader, (header[10] & 128) == 0 ? 0 : 3 * (1 << ((header[10] & 7) + 1)));
        var canvas = new byte[checked(width * height * 4)];
        byte[]? control = null;
        var frames = new List<Frame>();
        var count = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var marker = reader.ReadByte();
            if (marker == 0x3b) break;
            if (marker == 0x21)
            {
                var label = reader.ReadByte();
                if (label == 0xf9)
                {
                    control = Read(reader, 6);
                    if (control[0] != 4 || control[5] != 0)
                        throw new InvalidDataException("Invalid GIF frame control.");
                }
                else
                {
                    SkipBlocks(reader);
                }
                continue;
            }
            if (marker != 0x2c)
                throw new InvalidDataException("Invalid GIF block.");
            if (++count > maximumFrames)
                throw new InvalidDataException("The animated sprite frame count is invalid.");
            if ((long)width * height * 4 * count > maximumPixelBytes)
                throw new InvalidDataException("The animated sprite exceeded the memory limit.");
            var frameStart = checked((int)input.Position - 1);
            var descriptor = Read(reader, 9);
            var x = descriptor[0] | descriptor[1] << 8;
            var y = descriptor[2] | descriptor[3] << 8;
            var frameWidth = descriptor[4] | descriptor[5] << 8;
            var frameHeight = descriptor[6] | descriptor[7] << 8;
            if (frameWidth <= 0 || frameHeight <= 0 || x + frameWidth > width || y + frameHeight > height)
                throw new InvalidDataException("Invalid GIF frame bounds.");
            Read(reader, (descriptor[8] & 128) == 0 ? 0 : 3 * (1 << ((descriptor[8] & 7) + 1)));
            reader.ReadByte();
            SkipBlocks(reader);
            var frameEnd = checked((int)input.Position);

            // Decode pixels separately so disposal never leaks an old frame.
            using var single = new MemoryStream();
            single.Write(header);
            single.Write(palette);
            if (control is not null)
            {
                single.WriteByte(0x21);
                single.WriteByte(0xf9);
                single.Write(control);
            }
            single.Write(data.AsSpan(frameStart, frameEnd - frameStart));
            single.WriteByte(0x3b);
            single.Position = 0;
            using var decoded = Image.Load<Rgba32>(new DecoderOptions { Configuration = configuration, MaxFrames = 1 }, single);
            var pixels = new byte[canvas.Length];
            decoded.CopyPixelDataTo(pixels);
            if (decoded.Width != width || decoded.Height != height)
                throw new InvalidDataException("Invalid decoded GIF frame dimensions.");
            var disposal = control is null ? 0 : (control[1] >> 2) & 7;
            var previous = disposal == 3 ? (byte[])canvas.Clone() : null;

            for (var row = y; row < y + frameHeight; row++)
                for (var column = x; column < x + frameWidth; column++)
                {
                    var index = (row * width + column) * 4;
                    if (pixels[index + 3] != 0) pixels.AsSpan(index, 4).CopyTo(canvas.AsSpan(index, 4));
                }
            frames.Add(new Frame((byte[])canvas.Clone(), control is null ? 0 : control[2] | control[3] << 8));

            if (disposal == 2)
                for (var row = y; row < y + frameHeight; row++)
                    Array.Clear(canvas, (row * width + x) * 4, frameWidth * 4);
            else if (previous is not null)
                canvas = previous;
            control = null;
        }
        return frames.Count > 0 ? new Animation(width, height, frames) : throw new InvalidDataException("The animated sprite has no frames.");
    }

    private static byte[] Read(BinaryReader reader, int count)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new InvalidDataException("Truncated sprite GIF.");
        return bytes;
    }

    private static void SkipBlocks(BinaryReader reader)
    {
        int count;
        while ((count = reader.ReadByte()) != 0) Read(reader, count);
    }
}
