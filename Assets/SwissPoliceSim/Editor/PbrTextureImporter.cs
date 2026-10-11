using UnityEditor;
using UnityEngine;

namespace SwissPoliceSim.Editor
{
    /// <summary>Import settings for the photo PBR sets in Resources/PBR and the HDRI sky: *_Color sRGB, *_Normal as normal map, *_Rough / *_AO linear single channel.</summary>
    public sealed class PbrTextureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter; string p = assetPath.Replace('\\', '/');
            if (p.Contains("/Resources/PBR/"))
            {
                ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.wrapMode = TextureWrapMode.Repeat; ti.filterMode = FilterMode.Trilinear; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.Compressed; ti.streamingMipmaps = false;
                if (p.EndsWith("_Color.jpg")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; }
                else if (p.EndsWith("_Normal.jpg")) { ti.textureType = TextureImporterType.NormalMap; ti.convertToNormalmap = false; }
                else { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; }
            }
            else if (p.Contains("/Resources/Sky/"))
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.textureShape = TextureImporterShape.Texture2D; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Repeat; ti.filterMode = FilterMode.Bilinear; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            }
        }
    }
}
