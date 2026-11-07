using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DiscImageStudio.Core;
using DiscImageStudio.Dvd;
using DiscImageStudio.Linux.Services;
using DiscImageStudio.Linux.ViewModels;
using DvdImageSolver;
using DvdImageSolver.Solver;
using DvdImageSolver.Encoding;
using SkiaSharp;

namespace DiscImageStudio.LinuxTests;

internal static partial class Program
{
    private static string SolidImage(string work, string name, SKColor colour)
    {
        string path = Path.Combine(work, name);
        using SKBitmap image = new(128, 128);
        image.Erase(colour);
        using FileStream file = File.Create(path);
        image.Encode(file, SKEncodedImageFormat.Png, 100);
        return path;
    }

    private static async Task ColourGratings(string work)
    {
        string input = SolidImage(work, "grating-red.png", new SKColor(255, 80, 80));
        var settings = Settings(OpticalDiscFamily.Dvd, 321, 24, 58) with { DvdGrating = true, UsableSectors = 160 };
        string iso = Path.Combine(work, "grating.iso");
        await Generate(settings, input, iso);
        Equal(160L * 2048, new FileInfo(iso).Length, "grating obeys usable capacity");
        var metadata = DvdImageMetadata.TryLoad(iso) ?? throw new Exception("missing grating sidecar");
        Assert(metadata.Grating && metadata.TotalSectors == 321 && metadata.OutputSectors == 160, "saved grating geometry");
        Assert(metadata.SourceImageOuterRadiusMm < metadata.OuterRadiusMm, "saved source radius accounts for shortened capacity");
        string preview = Path.Combine(work, "colour-readback.png");
        await PreviewRenderer.RenderReadbackAsync(settings with { DvdGrating = false, OuterRadiusMm = 59 }, iso, preview, 128, _ => { }, CancellationToken.None);
        using SKBitmap decoded = SKBitmap.Decode(preview);
        Assert(Enumerable.Range(0, decoded.Height).Any(y => Enumerable.Range(0, decoded.Width).Any(x =>
        {
            SKColor c = decoded.GetPixel(x, y);
            return c.Red > c.Green + 10 && c.Red > c.Blue + 10;
        })), "saved grating mode produces coloured readback even after project edits");
        string projection = Path.Combine(work, "colour-projection.png");
        PreviewRenderer.RenderProjection(settings, input, projection, 128, 4, null, null, CancellationToken.None);
        AssertPng(projection, 128);
        // The portable CLI must dispatch colour readback too.
        DiscJobResult simulated = await EngineJobRunner.RunAsync(["simulate", "--iso", iso, "--output", preview,
            "--inner-radius-mm", "24", "--outer-radius-mm", "58", "--preview-size", "128"], _ => { }, _ => { }, CancellationToken.None);
        Assert(simulated.Succeeded, "CLI grating readback");
        bool rejected = false;
        try { (settings with { DataDirectory = work }).BuildGenerateCommand(input, iso); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "unsupported grating/files combination rejected");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try
        {
            GratingReadbackRenderer.Render(iso, preview, new(321, metadata.DrawingStartLba,
                metadata.DrawingEndLbaExclusive - metadata.DrawingStartLba, 0x30000, 24, 58, 133.3, true, 128, 4), cancel.Token);
            Assert(false, "readback must honor cancellation");
        }
        catch (OperationCanceledException) { }
    }

    private static async Task BlankAreaFiles(string work)
    {
        string input = SolidImage(work, "blank.png", SKColors.White);
        string folder = Path.Combine(work, "blank-files"); Directory.CreateDirectory(folder);
        byte[] original = Enumerable.Range(0, 3001).Select(i => (byte)(i * 31)).ToArray();
        File.WriteAllBytes(Path.Combine(folder, "payload.bin"), original);
        var settings = Settings(OpticalDiscFamily.Dvd, 321, 24, 58, folder) with { FilesInTransparency = true, UsableSectors = 256 };
        var capacity = DvdStreamingGenerator.EstimateTransparentCapacity(folder, input, settings.ToDvd());
        Assert(capacity.Fits, "white space has capacity");
        string estimatedPreview = Path.Combine(work, "blank-projection.png");
        PreviewRenderer.RenderProjection(settings, input, estimatedPreview, 128, 4, null, null, CancellationToken.None);
        AssertPng(estimatedPreview, 128);
        string iso = Path.Combine(work, "blank-files.iso");
        await Generate(settings, input, iso);
        Equal(256L * 2048, new FileInfo(iso).Length, "blank placement uses writable limit");
        var metadata = DvdImageMetadata.TryLoad(iso) ?? throw new Exception("missing blank-area sidecar");
        Assert(metadata.FilesInTransparentRegion && metadata.OutputSectors == 256 && metadata.TotalSectors == 321,
            "sidecar keeps file segments and physical/output geometry");
        Assert(metadata.FileSegments!.All(s => s.StartLba >= metadata.DrawingStartLba && s.EndLbaExclusive <= 256), "segments stay within writable disc");
        using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(iso + ".json")))
        {
            Assert(json.RootElement.GetProperty("exactEccEfmReplay").GetBoolean(), "blank placement replays physical boundaries");
            byte[] generated = File.ReadAllBytes(iso);
            ModulationBoundary boundary = new(1, 0, IsLand: true);
            for (uint lba = 0; lba < 256; lba += 16)
                boundary = new DvdEccBlockEncoder().Encode(generated.AsSpan((int)lba * 2048, 16 * 2048).ToArray(), lba, 0x30000, boundary).FinalBoundary;
            var saved = json.RootElement.GetProperty("finalBoundary");
            Equal(boundary.State, saved.GetProperty("state").GetInt32(), "whole-disc final state includes filesystem prefix");
            Equal(boundary.Dsv, saved.GetProperty("dsv").GetInt32(), "whole-disc DSV includes filesystem prefix");
            Equal(boundary.LevelName, saved.GetProperty("level").GetString()!, "whole-disc final level");
        }
        Assert(ReadJolietFile(File.ReadAllBytes(iso), "payload.bin;1").SequenceEqual(original), "CLI embedded file bytes survive");
        var plan = DvdStreamingGenerator.PrepareHybridInTransparency(folder, input, settings.ToDvd());
        using MemoryStream stream = new();
        DvdStreamingGenerator.GenerateHybrid(input, stream, settings.ToDvd(), plan);
        Equal(256L * 2048, stream.Length, "typed stream capacity");
        Assert(ReadJolietFile(stream.ToArray(), "payload.bin;1").SequenceEqual(original), "typed stream embedded file bytes survive");
        string dark = SolidImage(work, "no-space.png", SKColors.Black);
        var none = DvdStreamingGenerator.EstimateTransparentCapacity(folder, dark, settings.ToDvd(), backgroundIsBlank: false);
        Assert(!none.Fits && none.BlankSectors == 0, "opaque content cannot be overwritten when background detection is disabled");
    }

    private static byte[] ReadJolietFile(byte[] iso, string name)
    {
        static uint Number(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        int root = 17 * 2048 + 156;
        int begin = checked((int)Number(iso, root + 2) * 2048);
        int end = checked(begin + (int)Number(iso, root + 10));
        using MemoryStream result = new();
        for (int offset = begin; offset < end;)
        {
            int length = iso[offset];
            if (length == 0) { offset = (offset / 2048 + 1) * 2048; continue; }
            int chars = iso[offset + 32];
            if (chars > 1 && Encoding.BigEndianUnicode.GetString(iso, offset + 33, chars) == name)
            {
                int position = checked((int)Number(iso, offset + 2) * 2048);
                result.Write(iso, position, checked((int)Number(iso, offset + 10)));
            }
            offset += length;
        }
        return result.ToArray();
    }

    private static void CommunitySettingsRoundTrip(string work)
    {
        var project = new ProjectViewModel(ProjectViewModel.CreateDocument("Features", OpticalDiscFamily.Dvd,
            24, 58, 321, 0, 0, 0, false, "out.iso", 256), null);
        project.FilesInTransparency = true;
        string path = Path.Combine(work, "features.discproj"); project.Save(path);
        var restored = new ProjectViewModel(ProjectJsonStore.Load(path), path);
        Assert(restored.Snapshot().FilesInTransparency, "blank placement survives reopening");
        restored.FilesInTransparency = false; restored.DvdGrating = true; restored.Save(path);
        var colour = new ProjectViewModel(ProjectJsonStore.Load(path), path);
        Assert(colour.Snapshot().DvdGrating && !colour.Snapshot().FilesInTransparency, "colour mode survives reopening independently");
    }
}
