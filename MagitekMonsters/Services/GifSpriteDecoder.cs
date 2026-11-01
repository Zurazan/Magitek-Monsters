using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using ImageConfiguration = SixLabors.ImageSharp.Configuration;

namespace MagitekMonsters.Services;

internal static class GifSpriteDecoder
{
    private static readonly ImageConfiguration Configuration = new(new GifConfigurationModule())
    {
        MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            MaximumPoolSizeMegabytes = 16,
            AllocationLimitMegabytes = 32,
            AccumulativeAllocationLimitMegabytes = 128
        })
    };

    internal sealed record Frame(byte[] Pixels, int Delay);
    internal sealed record Animation(int Width, int Height, IReadOnlyList<Frame> Frames);

    public static Animation Decode(byte[] data, int maximumDimension, int maximumFrames, CancellationToken token = default, long maximumPixelBytes = long.MaxValue)
    {
        token.ThrowIfCancellationRequested();
        if (maximumDimension <= 0 || maximumFrames <= 0 || maximumPixelBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        if (data.Length < 14 || (!data.AsSpan(0, 6).SequenceEqual("GIF89a"u8) && !data.AsSpan(0, 6).SequenceEqual("GIF87a"u8)))
            throw new InvalidDataException("Invalid sprite GIF header.");
        if (data[^1] != 0x3b)
            throw new InvalidDataException("Truncated sprite GIF.");

        var options = new DecoderOptions
        {
            Configuration = Configuration,
            MaxFrames = checked((uint)maximumFrames + 1),
            SegmentIntegrityHandling = SegmentIntegrityHandling.Strict
        };
        using var input = new MemoryStream(data, false);
        var info = GifDecoder.Instance.IdentifyAsync(options, input, token).GetAwaiter().GetResult();
        if (info.Width <= 0 || info.Height <= 0 || info.Width > maximumDimension || info.Height > maximumDimension)
            throw new InvalidDataException("The animated sprite dimensions are invalid.");
        if (info.FrameCount <= 0 || info.FrameCount > maximumFrames)
            throw new InvalidDataException("The animated sprite frame count is invalid.");
        var frameBytes = checked(info.Width * info.Height * 4);
        if ((long)frameBytes * info.FrameCount > maximumPixelBytes)
            throw new InvalidDataException("The animated sprite exceeded the memory limit.");

        // ImageSharp 4.1.3 restores stale pixels for previous-disposal GIFs.
        if (info.FrameMetadataCollection.Any(frame => frame.GetGifMetadata().DisposalMode == FrameDisposalMode.RestoreToPrevious))
            return GifDisposalDecoder.Decode(data, Configuration, maximumDimension, maximumFrames, token, maximumPixelBytes);

        input.Position = 0;
        using var image = GifDecoder.Instance.DecodeAsync<Rgba32>(options, input, token).GetAwaiter().GetResult();
        var frames = new List<Frame>(image.Frames.Count);
        var width = image.Width;
        var height = image.Height;
        while (image.Frames.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            if (image.Frames.Count == 1)
            {
                var lastPixels = new byte[frameBytes];
                image.CopyPixelDataTo(lastPixels);
                frames.Add(new Frame(lastPixels, image.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay));
                break;
            }
            using var frame = image.Frames.ExportFrame(0);
            var pixels = new byte[frameBytes];
            frame.CopyPixelDataTo(pixels);
            frames.Add(new Frame(pixels, frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay));
        }
        return new Animation(width, height, frames);
    }
}
