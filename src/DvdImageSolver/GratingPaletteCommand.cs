using DiscImageStudio.Iso;
using DvdImageSolver.Encoding;
using DvdImageSolver.Solver;

namespace DvdImageSolver;

/// <summary>
/// Writes a full DVD image whose payload words are grating motifs selected from a
/// flat source image. This intentionally uses the same dispersion writer and ISO
/// prefix as the proven monochrome path; only the payload-word callback changes.
/// </summary>
public static class GratingPaletteCommand
{
    internal static int Run(CliArguments args)
    {
        string imagePath = args.Require("image");
        string outputPath = args.Require("output");
        uint total = args.GetUInt("total-sectors", 2_298_496);
        uint usable = args.GetUInt("usable-sectors", total / 16 * 16);
        double inner = args.GetDouble("inner-radius-mm", 24), outer = args.GetDouble("outer-radius-mm", 58);
        double linear = args.GetDouble("pitch-linear", 0), quadratic = args.GetDouble("pitch-quadratic", 0), cubic = args.GetDouble("pitch-cubic", 0);
        var geometry = DiscImageStudio.Core.DvdTrackGeometry.Create(inner, outer, total * 38688.0 * 133.3e-6, linear, quadratic, cubic);
        double sourceRadius = geometry.RadiusAtArcLength(usable * 38688.0 * 133.3e-6);
        Write(imagePath, outputPath, args.Get("volume-label", "DISC_GRATING"),
            new ImageMappingOptions(total, inner, outer, 133.3, 0, true, 128, 1,
                PitchLinear: linear, PitchQuadratic: quadratic, PitchCubic: cubic,
                UsableSectors: usable, ImageOuterRadiusMm: sourceRadius), args.GetDouble("cell-mm", 0.068));
        return 0;
    }

    /// <summary>Writes a complete ISO to a file, or to stdout when outputPath is "-".</summary>
    public static void Write(
        string imagePath, string outputPath, string volumeLabel,
        ImageMappingOptions disc, double cellMm = 0.068,
        CancellationToken cancellationToken = default)
    {
        Validate(disc);
        if (!double.IsFinite(cellMm) || cellMm < 0.02 || cellMm > 5)
            throw new ArgumentOutOfRangeException(nameof(cellMm), "Cell size must be 0.02..5 mm.");
        cancellationToken.ThrowIfCancellationRequested();
        uint totalSectors = disc.OutputSectors;
        double inner = disc.InnerRadiusMm, outer = disc.OuterRadiusMm;
        double sourceRadius = disc.SamplingOuterRadiusMm;
        var image = RasterImage.Load(imagePath);
        var cells = new CellPlan(image, inner, sourceRadius, cellMm);

        Iso9660ImageWriter.LayoutPlan isoPlan = Iso9660ImageWriter.Plan(
            sourceDirectory: null, totalSectors, volumeLabel,
            timestamp: new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));
        uint drawingStart = Align16(isoPlan.MetadataEndLbaExclusive);
        if (drawingStart >= totalSectors)
            throw new ArgumentException("Drawing area is empty.");

        ImageMappingOptions mapping = disc;

        MultiBlockSolveOptions options = new(
            StartLba: drawingStart,
            FillSectors: totalSectors - drawingStart,
            PsnOffset: 0x30000,
            IterationsPerBlock: 0,
            MutationBytes: 0,
            RandomSeed: 1,
            InitialTemperature: 0,
            FinalTemperature: 0,
            ZeroInitialPayload: true,
            ImageMapping: mapping,
            Algorithm: SolverAlgorithm.Dispersion,
            FastParallelism: 0)
        {
            UseSampleReuse = false,
            UseSimdSampling = false,
        };

        FileStream? file = outputPath == "-" ? null : File.Create(outputPath);
        Stream output = file ?? Console.OpenStandardOutput();
        try
        {
            Iso9660ImageWriter.WriteSequentialPrefix(isoPlan, output, drawingStart);
            FastDispersionImageWriter.WriteScrambledCodewordPatternStream(
                cells.Sample,
                output,
                options,
                progress =>
                {
                    if (progress.CompletedBlocks == 1
                        || progress.CompletedBlocks == progress.TotalBlocks
                        || progress.CompletedBlocks % 5000 == 0)
                    {
                        Console.Error.WriteLine(
                            $"block {progress.CompletedBlocks}/{progress.TotalBlocks}, "
                            + $"LBA={progress.BlockLba}, bytes={progress.BytesWritten}, "
                            + $"elapsed={progress.Elapsed.TotalSeconds:F1}s");
                        Console.Error.Flush();
                    }
                }, cancellationToken);
            if (file is not null) file.SetLength((long)totalSectors * 2048);
            output.Flush();
        }
        finally
        {
            if (file is not null) file.Dispose();
        }
        if (file is not null)
        {
            var sidecar = new
            {
                format = "dvd-grating-v1", grating = true, cellMm,
                startLba = drawingStart, fillSectors = totalSectors - drawingStart,
                totalSectors = disc.TotalSectors, imageMapping = mapping,
                iso = new { mountableFilesystem = true, imageSectors = totalSectors },
            };
            File.WriteAllText(outputPath + ".json", System.Text.Json.JsonSerializer.Serialize(sidecar,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        }
        Console.Error.WriteLine(
            $"done: {(outputPath == "-" ? "stdout" : outputPath)}, sectors={totalSectors}, "
            + $"drawingStart={drawingStart}, cell={cellMm * 1000:F1}um, palette={DvdGratingPalette.Motifs.Length}");
    }

    /// <summary>Nominal palette preview; physical diffraction colours depend on lighting and media.</summary>
    public static void RenderProjection(string imagePath, string outputPath,
        ImageMappingOptions disc, int size, CancellationToken cancellationToken = default)
    {
        Validate(disc);
        if (size is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(size));
        double radius = disc.SamplingOuterRadiusMm;
        CellPlan cells = new(RasterImage.Load(imagePath), disc.InnerRadiusMm, radius, 0.068);
        byte[] pixels = new byte[checked(size * size * 4)];
        for (int y = 0; y < size; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < size; x++)
            {
                double xmm = (x - (size - 1) / 2.0) * 2 * radius / (size - 1);
                double ymm = ((size - 1) / 2.0 - y) * 2 * radius / (size - 1);
                double r = Math.Sqrt(xmm * xmm + ymm * ymm);
                if (r < disc.InnerRadiusMm || r > radius) continue;
                var c = cells.ColourAt(xmm, ymm);
                int i = (y * size + x) * 4;
                pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = 255;
            }
        }
        DiscImageStudio.ImageIO.RasterCodec.SavePng(outputPath, size, size, pixels, grayscale: false);
    }

    private static void Validate(ImageMappingOptions mapping)
    {
        if (mapping.TotalSectors == 0 || mapping.OutputSectors == 0 || mapping.OutputSectors > mapping.TotalSectors
            || mapping.OutputSectors % 16 != 0 || !double.IsFinite(mapping.SamplingOuterRadiusMm)
            || mapping.SamplingOuterRadiusMm <= mapping.InnerRadiusMm)
            throw new ArgumentException("Invalid grating geometry or usable capacity.");
        _ = DiscImageStudio.Core.DvdTrackGeometry.Create(mapping.InnerRadiusMm, mapping.OuterRadiusMm,
            mapping.TotalSectors * 38688.0 * mapping.ChannelBitLengthNm * 1e-6,
            mapping.PitchLinear, mapping.PitchQuadratic, mapping.PitchCubic);
    }

    private static uint Align16(uint value) => (value + 15u) & ~15u;

    private sealed class CellPlan
    {
        private readonly int _cols;
        private readonly int _rows;
        private readonly double _origin;
        private readonly double _cell;
        private readonly byte[] _coverage;
        private readonly byte[] _motif;

        internal CellPlan(RasterImage image, double inner, double outer, double cellMm)
        {
            _cell = cellMm;
            _origin = -outer;
            _cols = (int)Math.Ceiling((2 * outer) / cellMm);
            _rows = _cols;
            _coverage = new byte[checked(_cols * _rows)];
            _motif = new byte[checked(_cols * _rows)];
            for (int row = 0; row < _rows; row++)
            {
                double y = _origin + ((row + 0.5) * cellMm);
                for (int col = 0; col < _cols; col++)
                {
                    double x = _origin + ((col + 0.5) * cellMm);
                    double radius = Math.Sqrt((x * x) + (y * y));
                    if (radius < inner || radius > outer) continue;
                    var color = image.SampleDiscColor(x, y, outer);
                    if (color.Alpha == 0) continue;
                    double blue = color.Blue / 255.0;
                    double green = color.Green / 255.0;
                    double red = color.Red / 255.0;
                    double luminance = 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
                    double max = Math.Max(red, Math.Max(green, blue));
                    double min = Math.Min(red, Math.Min(green, blue));
                    double delta = max - min;
                    double hue = delta < 1e-9 ? 0 : max == red
                        ? 60 * (((green - blue) / delta) % 6)
                        : max == green ? 60 * (((blue - red) / delta) + 2)
                        : 60 * (((red - green) / delta) + 4);
                    if (hue < 0) hue += 360;
                    double saturation = max > 1e-9 ? delta / max : 0;
                    // One seamless colour mapping over the whole disc. The old
                    // three 120° sectors were only an A/B comparison layout and
                    // produced visibly hard boundaries; never use them for output.
                    int paletteIndex = saturation < 0.16 ? -1 : PaletteForHue(hue);
                    int index = (row * _cols) + col;
                    _coverage[index] = (byte)Math.Clamp((int)Math.Round(luminance * 255), 0, 255);
                    // 1 = white (92), 2..6 = B/G/C/D/E. Black A5 is the off state.
                    _motif[index] = paletteIndex < 0 ? (byte)1 : (byte)(paletteIndex + 1);
                }
            }
        }

        internal (byte R, byte G, byte B) ColourAt(double x, double y)
        {
            int col = (int)Math.Floor((x - _origin) / _cell);
            int row = (int)Math.Floor((y - _origin) / _cell);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return default;
            int i = row * _cols + col;
            var colour = _motif[i] <= 1 ? ((byte)255, (byte)255, (byte)255)
                : DvdGratingPalette.Colours[_motif[i] - 1];
            double f = _coverage[i] / 255.0;
            return ((byte)(colour.Item1 * f), (byte)(colour.Item2 * f), (byte)(colour.Item3 * f));
        }

        internal byte Sample(DvdCodewordSample sample)
        {
            int col = (int)Math.Floor((sample.Xmm - _origin) / _cell);
            // The callback uses y-down (SamplePoint passes -yMm); the cell plan
            // and RasterImage.SampleDisc use mathematical y-up.
            int row = (int)Math.Floor(((-sample.Ymm) - _origin) / _cell);
            if (col < 0 || col >= _cols || row < 0 || row >= _rows) return 0;
            int index = (row * _cols) + col;
            byte coverage = _coverage[index];
            if (coverage == 0) return 0xA5;
            // Dither at payload-word resolution (~2.1 µm), much finer than the
            // 68 µm colour cell. The cell still determines hue and mean brightness.
            double threshold = WordDither(sample.GlobalCodewordIndex);
            if (threshold >= coverage / 255.0) return 0xA5;  // black motif
            byte value = _motif[index];
            if (value == 1) return 0x92;       // white motif
            byte[] motifs = DvdGratingPalette.Motifs[value - 1];
            return motifs[(int)(sample.GlobalCodewordIndex % (ulong)motifs.Length)];
        }

        private static double Linear(double value)
            => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        private static int PaletteForHue(double hue) => hue switch
        {
            >= 330 or < 20 => 3,   // C red
            >= 20 and < 55 => 2,   // G yellow
            >= 55 and < 150 => 1,  // B green
            >= 150 and < 195 => 4, // D teal
            >= 195 and < 330 => 5, // E blue (violet approximated)
            _ => 3,
        };

        private static double WordDither(ulong index)
        {
            ulong value = index * 0x9E3779B97F4A7C15UL;
            value ^= value >> 29;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 32;
            return (value & 0xFFFFFF) / 16777216.0;
        }
    }
}
