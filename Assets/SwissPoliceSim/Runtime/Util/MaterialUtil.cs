using UnityEngine;

namespace SwissPoliceSim.Util
{
    public static class MaterialUtil
    {
        /// <summary>Creates a simple coloured material for URP (falls back to Standard / Sprites if URP shaders are missing).</summary>
        public static Material Create(Color color, float smoothness = 0.1f)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { color = color };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            return m;
        }

        public static Texture2D SolidTexture(Color c, int size = 4)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px); t.Apply();
            t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }
    }
}
