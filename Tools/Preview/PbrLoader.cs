using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SwissPoliceSim.Core.Textures;
using SImage = SixLabors.ImageSharp.Image;
using CImage = SwissPoliceSim.Core.Textures.Image;

/// <summary>Loads the photo PBR sets from Assets/SwissPoliceSim/Resources/PBR (same files the Unity game uses).</summary>
static class PbrLoader
{
    public static string Dir = "Assets/SwissPoliceSim/Resources/PBR";

    static CImage Load(string file, int maxSize = 512)
    {
        using var im = SImage.Load<Rgba32>(file);
        int w = im.Width, h = im.Height; var c = new CImage(w, h);
        im.CopyPixelDataTo(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Rgba32>(c.Px));
        while (c.W > maxSize) c = c.Half();
        return c;
    }

    public static Mat Make(string set, float repeatsPerMetre, float boost, bool vertexColor = true, bool doubleSided = false, float normalStrength = 1f)
    {
        string col = Path.Combine(Dir, set + "_Color.jpg"); if (!File.Exists(col)) throw new FileNotFoundException(col);
        var m = new Mat { Mips = Mat.MakeMips(Load(col)), TriScale = repeatsPerMetre, Boost = boost, UseVertexColor = vertexColor, DoubleSided = doubleSided, NormalStrength = normalStrength };
        m.NormMips = Mat.MakeMips(Load(Path.Combine(Dir, set + "_Normal.jpg")));
        string r = Path.Combine(Dir, set + "_Rough.jpg"), a = Path.Combine(Dir, set + "_AO.jpg");
        m.RoughMips = Mat.MakeMips(File.Exists(r) ? Load(r) : Flat(0.7f)); m.AoMips = Mat.MakeMips(File.Exists(a) ? Load(a) : Flat(1f));
        return m;
    }

    static CImage Flat(float v) { var im = new CImage(8, 8); for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) im.Set(x, y, v, v, v); return im; }
}
