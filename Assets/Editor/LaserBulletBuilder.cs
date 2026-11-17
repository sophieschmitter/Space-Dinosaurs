using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Gives the dino's gun the laser: slices "Laser bullet.PNG" (a 16 x 16 art px canvas at 125 px per art pixel,
/// the streak is 4 x 1 art px, bright tip on the right) and sets the DinoBullet prefab to show it.
/// The laser isn't animated and doesn't explode, so DinoBullet has no animations and vanishes the moment it
/// hits (Explode Time 0). The tall crab's bullet is untouched.
/// Run from: Tools > Space Dinosaurs > Build Laser Bullet. Safe to re-run.
/// </summary>
public static class LaserBulletBuilder
{
    const string SheetPath = "Assets/Sprites/Laser bullet.PNG";
    const string BulletPath = "Assets/Prefabs/DinoBullet.prefab";
    const int Scale = 125;

    [MenuItem("Tools/Space Dinosaurs/Build Laser Bullet")]
    public static void Build()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        if (importer == null) { Debug.LogError($"Laser: no sprite at {SheetPath}"); return; }
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = Scale * 16;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var old = provider.GetSpriteRects().FirstOrDefault(r => r.name == "Laser");
        var rect = new SpriteRect
        {
            name = "Laser",
            spriteID = old != null ? old.spriteID : GUID.Generate(),
            // art px x 6-9, row 7 from the top of the 16 x 16 canvas (texture is 2000 px tall)
            rect = new Rect(6 * Scale, 2000 - 8 * Scale, 4 * Scale, Scale),
            alignment = SpriteAlignment.Custom,
            pivot = new Vector2(1f, 0.5f), // on the bright tip, like the other bullets' noses
        };
        provider.SetSpriteRects(new[] { rect });
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(new[] { new SpriteNameFileIdPair("Laser", rect.spriteID) });
        provider.Apply();
        importer.SaveAndReimport();
        var laser = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().FirstOrDefault(s => s.name == "Laser");

        var root = PrefabUtility.LoadPrefabContents(BulletPath);
        try
        {
            var bullet = root.GetComponent<CrabBullet>();
            var art = root.transform.Find("Art");
            art.GetComponent<SpriteRenderer>().sprite = laser;
            var anim = art.GetComponent<Animator>();
            if (anim != null) anim.runtimeAnimatorController = null; // no Launch / Fly / Explode
            bullet.animator = null;
            bullet.artFaces = Vector2.right;
            bullet.explodeTime = 0f;
            PrefabUtility.SaveAsPrefabAsset(root, BulletPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("Laser bullet: DinoBullet now shows the laser");
    }
}
