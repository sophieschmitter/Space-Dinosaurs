using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps every art pixel the same size in the world.
/// The game's grid: 1 world unit = 1 tile = 16 art pixels.
/// Art is exported scaled up, so each folder states how many texture pixels make one art pixel,
/// and the importer sets PPU = ArtPixelsPerUnit * that scale.
/// </summary>
public class PixelArtImporter : AssetPostprocessor
{
    public const int ArtPixelsPerUnit = 16; // one tile; also the Pixel Perfect Camera's Assets PPU

    // Folder -> texture pixels per art pixel. Add a row for any new art folder.
    static readonly (string folder, int scale)[] Folders =
    {
        ("Assets/Sprites", 10), // characters: 480px box = 48 art px  -> PPU 160
        ("Assets/Tiles",   15), // tiles: 240px tile = 16 art px      -> PPU 240
        ("Assets/Art",     1),  // un-scaled art: 16px tile            -> PPU 16
    };

    void OnPreprocessTexture()
    {
        int scale = 0;
        foreach (var f in Folders)
            if (assetPath.StartsWith(f.folder + "/")) { scale = f.scale; break; }
        if (scale == 0) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spritePixelsPerUnit = ArtPixelsPerUnit * scale;
        importer.filterMode = FilterMode.Point;                         // crisp, no blur
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 8192;                                 // 2048 default downscales big sheets
    }
}
