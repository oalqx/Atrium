namespace Atrium.Core.Models;

public readonly record struct ArtworkColor(byte R, byte G, byte B)
{
    public double Luminance
    {
        get
        {
            static double Linear(byte channel)
            {
                var value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);
        }
    }
}

public sealed record ArtworkPalette(ArtworkColor Accent, ArtworkColor Surface)
{
    public static ArtworkPalette Default { get; } = FromAccent(new(126, 208, 193));
    public ArtworkColor ButtonText => Accent.Luminance > 0.179 ? new(0, 0, 0) : new(255, 255, 255);

    private static ArtworkPalette FromAccent(ArtworkColor accent) => new(accent,
        new((byte)(14 + accent.R * 0.055), (byte)(14 + accent.G * 0.055), (byte)(18 + accent.B * 0.055)));

    public static ArtworkPalette FromBgra(ReadOnlySpan<byte> pixels)
    {
        var bins = new (double Weight, double R, double G, double B)[512];
        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            var alpha = pixels[offset + 3];
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (alpha < 128 || max < 40 || max - min < 18) continue;
            var weight = (max - min) / 255.0 * alpha / 255.0;
            var index = (r >> 5) * 64 + (g >> 5) * 8 + (b >> 5);
            ref var bin = ref bins[index];
            bin.Weight += weight;
            bin.R += r * weight;
            bin.G += g * weight;
            bin.B += b * weight;
        }
        var dominant = bins.MaxBy(bin => bin.Weight);
        if (dominant.Weight == 0) return Default;
        // A modest lift keeps dark cover colors usable as a visible accent.
        static byte Lift(double value) => (byte)Math.Clamp(value * 0.8 + 48, 0, 255);
        return FromAccent(new(Lift(dominant.R / dominant.Weight), Lift(dominant.G / dominant.Weight),
            Lift(dominant.B / dominant.Weight)));
    }
}
