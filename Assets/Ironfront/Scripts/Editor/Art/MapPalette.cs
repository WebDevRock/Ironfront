using UnityEngine;

namespace Ironfront.EditorTools.Art
{
    /// <summary>
    /// Milestone 008: the look of one battlefield — ground colours, rock tint, vegetation, props, lighting, haze and
    /// colour grading. <see cref="Steppe"/> reproduces the Milestone 006 look exactly (RTS_Match, sandbox, menu).
    /// Everything here is presentation; terrain gameplay comes only from obstacle colliders.
    /// </summary>
    public sealed class MapPalette
    {
        public enum Biome { Steppe, Desert, Mountain, Forest, Industrial }

        public string Name = "Steppe";
        public Biome Style = Biome.Steppe;

        // Ground painting.
        public Color Grass = new Color(0.50f, 0.50f, 0.34f);
        public Color Dirt = new Color(0.58f, 0.50f, 0.37f);
        public Color Road = new Color(0.68f, 0.60f, 0.46f);
        public Color Pad = new Color(0.60f, 0.58f, 0.52f);
        public Color OreStain = new Color(0.38f, 0.29f, 0.22f);
        public Color Edge = new Color(0.36f, 0.34f, 0.27f);
        /// <summary>Extra large-scale patches (sand drifts, moss, oil stains); alpha = strength.</summary>
        public Color Patch = new Color(0f, 0f, 0f, 0f);

        // Materials tints (null-equivalent: alpha 0 keeps the shared Milestone 006 material).
        public Color Rock = new Color(0, 0, 0, 0);
        public Color RockDark = new Color(0, 0, 0, 0);
        public Color Shrub = new Color(0, 0, 0, 0);
        public Color Foliage = new Color(0.20f, 0.33f, 0.16f);
        public Color FoliageDark = new Color(0.13f, 0.24f, 0.12f);
        public Color Bark = new Color(0.30f, 0.23f, 0.17f);

        // Scatter.
        public int OpenProps = 260;
        public float ShrubChance = 0.6f;

        // Lighting and haze.
        public Color Sun = new Color(1f, 0.9f, 0.76f);
        public float SunIntensity = 1.5f;
        public Vector2 SunAngles = new Vector2(47f, -38f);
        public Color AmbientSky = new Color(0.50f, 0.56f, 0.66f);
        public Color AmbientEquator = new Color(0.44f, 0.42f, 0.38f);
        public Color AmbientGround = new Color(0.24f, 0.21f, 0.18f);
        public Color FogColor = new Color(0.66f, 0.64f, 0.6f);
        public float FogDensity = 0.0032f;

        // Colour grading (null profile name = shared Milestone 006 profile).
        public float Temperature = 6f;
        public float Saturation = 4f;
        public float Exposure = 0.35f;

        // Minimap.
        public Color32 MinimapGround = new Color32(52, 66, 44, 255);
        public Color32 MinimapObstacle = new Color32(105, 108, 115, 255);

        public bool IsDefault => Style == Biome.Steppe;

        public static MapPalette Steppe => new MapPalette();

        public static MapPalette Desert => new MapPalette
        {
            Name = "Desert", Style = Biome.Desert,
            Grass = new Color(0.79f, 0.66f, 0.46f), Dirt = new Color(0.70f, 0.55f, 0.38f), Road = new Color(0.86f, 0.76f, 0.58f),
            Pad = new Color(0.70f, 0.66f, 0.58f), OreStain = new Color(0.45f, 0.30f, 0.20f), Edge = new Color(0.62f, 0.49f, 0.34f),
            Patch = new Color(0.88f, 0.76f, 0.56f, 0.55f),
            Rock = new Color(0.74f, 0.52f, 0.36f), RockDark = new Color(0.58f, 0.39f, 0.27f), Shrub = new Color(0.52f, 0.47f, 0.28f),
            OpenProps = 150, ShrubChance = 0.35f,
            Sun = new Color(1f, 0.88f, 0.70f), SunIntensity = 1.7f, SunAngles = new Vector2(52f, -30f),
            AmbientSky = new Color(0.58f, 0.60f, 0.66f), AmbientEquator = new Color(0.56f, 0.48f, 0.38f), AmbientGround = new Color(0.32f, 0.25f, 0.18f),
            FogColor = new Color(0.80f, 0.71f, 0.58f), FogDensity = 0.0030f,
            Temperature = 14f, Saturation = 2f, Exposure = 0.3f,
            MinimapGround = new Color32(120, 98, 66, 255), MinimapObstacle = new Color32(150, 104, 76, 255),
        };

        public static MapPalette Mountain => new MapPalette
        {
            Name = "Mountain", Style = Biome.Mountain,
            Grass = new Color(0.45f, 0.45f, 0.36f), Dirt = new Color(0.43f, 0.39f, 0.34f), Road = new Color(0.57f, 0.53f, 0.47f),
            Pad = new Color(0.56f, 0.55f, 0.52f), OreStain = new Color(0.34f, 0.26f, 0.20f), Edge = new Color(0.33f, 0.32f, 0.30f),
            Patch = new Color(0.36f, 0.36f, 0.35f, 0.5f),
            Rock = new Color(0.50f, 0.49f, 0.47f), RockDark = new Color(0.33f, 0.32f, 0.32f), Shrub = new Color(0.36f, 0.40f, 0.26f),
            OpenProps = 240, ShrubChance = 0.45f,
            Sun = new Color(0.96f, 0.93f, 0.88f), SunIntensity = 1.35f, SunAngles = new Vector2(40f, -50f),
            AmbientSky = new Color(0.48f, 0.55f, 0.68f), AmbientEquator = new Color(0.40f, 0.41f, 0.42f), AmbientGround = new Color(0.20f, 0.20f, 0.20f),
            FogColor = new Color(0.60f, 0.63f, 0.67f), FogDensity = 0.0042f,
            Temperature = -4f, Saturation = 0f, Exposure = 0.4f,
            MinimapGround = new Color32(70, 72, 60, 255), MinimapObstacle = new Color32(128, 126, 122, 255),
        };

        public static MapPalette Forest => new MapPalette
        {
            Name = "Forest", Style = Biome.Forest,
            Grass = new Color(0.34f, 0.46f, 0.22f), Dirt = new Color(0.45f, 0.39f, 0.26f), Road = new Color(0.57f, 0.50f, 0.36f),
            Pad = new Color(0.55f, 0.54f, 0.48f), OreStain = new Color(0.33f, 0.25f, 0.17f), Edge = new Color(0.22f, 0.30f, 0.16f),
            Patch = new Color(0.25f, 0.38f, 0.17f, 0.6f),
            Rock = new Color(0.46f, 0.47f, 0.41f), RockDark = new Color(0.31f, 0.33f, 0.28f), Shrub = new Color(0.25f, 0.40f, 0.17f),
            OpenProps = 420, ShrubChance = 0.8f,
            Sun = new Color(1f, 0.95f, 0.84f), SunIntensity = 1.45f, SunAngles = new Vector2(48f, -42f),
            AmbientSky = new Color(0.50f, 0.58f, 0.66f), AmbientEquator = new Color(0.38f, 0.44f, 0.34f), AmbientGround = new Color(0.20f, 0.22f, 0.15f),
            FogColor = new Color(0.62f, 0.68f, 0.63f), FogDensity = 0.0036f,
            Temperature = 2f, Saturation = 8f, Exposure = 0.35f,
            MinimapGround = new Color32(48, 74, 36, 255), MinimapObstacle = new Color32(28, 52, 26, 255),
        };

        public static MapPalette Industrial => new MapPalette
        {
            Name = "Industrial", Style = Biome.Industrial,
            Grass = new Color(0.27f, 0.26f, 0.24f), Dirt = new Color(0.33f, 0.29f, 0.25f), Road = new Color(0.38f, 0.37f, 0.36f),
            Pad = new Color(0.49f, 0.48f, 0.46f), OreStain = new Color(0.20f, 0.14f, 0.10f), Edge = new Color(0.17f, 0.16f, 0.15f),
            Patch = new Color(0.12f, 0.11f, 0.11f, 0.6f),
            Rock = new Color(0.24f, 0.23f, 0.22f), RockDark = new Color(0.14f, 0.13f, 0.13f), Shrub = new Color(0.30f, 0.30f, 0.20f),
            OpenProps = 220, ShrubChance = 0.2f,
            Sun = new Color(1f, 0.80f, 0.62f), SunIntensity = 1.35f, SunAngles = new Vector2(38f, -28f),
            AmbientSky = new Color(0.46f, 0.47f, 0.50f), AmbientEquator = new Color(0.40f, 0.36f, 0.32f), AmbientGround = new Color(0.18f, 0.16f, 0.14f),
            FogColor = new Color(0.50f, 0.45f, 0.40f), FogDensity = 0.0055f,
            Temperature = 10f, Saturation = -14f, Exposure = 0.45f,
            MinimapGround = new Color32(44, 42, 40, 255), MinimapObstacle = new Color32(110, 100, 92, 255),
        };
    }
}
