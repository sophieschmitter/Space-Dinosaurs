using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Builds the training level in the open Training scene: carves the six rooms and their hallways out of the
/// metal walls, and puts in the TVs, the drilling wall, the ore, the building spots, the HUD and the
/// TrainingDirector that runs it all. Tools > Space Dinosaurs > Build Training Level (Training must be open).
///
/// Layout (world tiles, left to right; hallways are 4 tiles wide):
///   entry hallway -> 0 intro room -> 1 wall room (a breakable meteorite wall across it, the ore past it)
///   -> 2 ice cream / crab room (3 ice cream spots) -> 3 turret room (1 spot) -> 4 shield room (1 spot, a
///   4-wide dead-end hallway below it that the tall crabs come up) -> up a hallway to 5, the final room.
/// </summary>
public static class TrainingLevelBuilder
{
    // Open areas: x0, y0, x1, y1 (inclusive world tiles).
    static readonly RectInt[] Open =
    {
        R(-30, 0, -9, 3),     // entry hallway
        R(-8, -4, 20, 7),     // 0 intro (rooms are 12 tall: about one screen)
        R(21, 0, 28, 3),
        R(29, -4, 60, 7),     // 1 wall + ore
        R(61, 0, 68, 3),
        R(69, -4, 98, 7),     // 2 ice cream + crabs
        R(99, 0, 106, 3),
        R(107, -4, 130, 7),   // 3 turret
        R(131, 0, 138, 3),
        R(139, -4, 162, 7),   // 4 shield
        R(145, -20, 148, -5), //   the tall crabs' hallway (4 wide), near the entrance so its spot is in view
        R(149, 8, 152, 12),   //   up to the final room
        R(139, 13, 162, 24),  // 5 final
    };
    static RectInt R(int x0, int y0, int x1, int y1) => new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    static Rect Area(int x0, int y0, int x1, int y1) => new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);

    static readonly RectInt DrillWall = R(43, -4, 45, 7);
    // Room 1 (past the wall): enough for the ice cream stand and the turret (6 copper, 2 iridium).
    // Room 4 (the shield room): enough for the shield generator (3 copper, 1 iridium).
    static readonly Vector2Int[] Copper = { new(48, 5), new(51, -2), new(54, 6), new(57, -3), new(49, -3), new(58, 5),
                                            new(153, 5), new(156, 6), new(159, 4) };
    static readonly Vector2Int[] Iridium = { new(53, 3), new(56, 1), new(155, -2) };
    // Each room's TV, just inside the entrance (in view as you walk in).
    static readonly Vector2[] TvAt = { new(8f, 4.5f), new(34f, 4.5f), new(74f, 4.5f), new(112f, 4.5f), new(143f, 4.5f), new(151f, 21f) };

    [MenuItem("Tools/Space Dinosaurs/Build Training Level")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Training.unity");
        if (!scene.IsValid() || !scene.isLoaded) return "Open the Training scene first.";
        var log = new List<string>();
        T Find<T>(string name = null) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<T>(true))
                    if (name == null || c.name == name) return c;
            return null;
        }
        GameObject Root(string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        // ---------- walls ----------
        var solid = Find<Tilemap>("Solid Walls");
        var metalTile = solid.GetTile(new Vector3Int(0, 0, 0)) ?? AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tiles/Generated/Metal/MetalRuleTile.asset");
        var open = new HashSet<Vector2Int>();
        foreach (var r in Open) for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) open.Add(new Vector2Int(x, y));
        Undo.RegisterCompleteObjectUndo(solid, "Build training level");
        var origin = solid.transform.position;
        int filled = 0;
        var positions = new List<Vector3Int>(); var tiles = new List<TileBase>();
        for (int cx = 0; cx < 212; cx++)
        for (int cy = 0; cy < 114; cy++)
        {
            var world = new Vector2Int(cx + Mathf.RoundToInt(origin.x), cy + Mathf.RoundToInt(origin.y));
            bool isOpen = open.Contains(world);
            positions.Add(new Vector3Int(cx, cy, 0)); tiles.Add(isOpen ? null : metalTile);
            if (!isOpen) filled++;
        }
        solid.SetTiles(positions.ToArray(), tiles.ToArray());
        log.Add($"walls: {open.Count} open tiles, {filled} metal");

        // ---------- old pieces of the first version ----------
        foreach (var n in new[] { "temp dino bad guy", "Intro Dialogue", "Meteorite Room Dialogue", "Training Setup" })
        {
            var g = Root(n);
            if (g != null) { Undo.DestroyObjectImmediate(g); log.Add("removed " + n); }
        }

        var setup = new GameObject("Training Setup");
        SceneManager.MoveGameObjectToScene(setup, scene);
        Undo.RegisterCreatedObjectUndo(setup, "Build training level");

        // ---------- HUD (health, ore counters, inventory), no robot bars or level timer ----------
        if (Find<ResourceInventory>() == null)
        {
            var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HUD.prefab"), scene);
            Undo.RegisterCreatedObjectUndo(hud, "Build training level");
            var lt = hud.GetComponent<LevelTimer>();
            if (lt != null) lt.enabled = false;
            var robot = hud.transform.Find("Robot Bars");
            if (robot != null) robot.gameObject.SetActive(false);
            var standalone = Root("EventSystem"); // the HUD has its own
            if (standalone != null) Undo.DestroyObjectImmediate(standalone);
            log.Add("HUD added");
        }

        // ---------- TVs ----------
        var tvPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TV.prefab");
        var tvs = new Animator[TvAt.Length];
        for (int i = 0; i < TvAt.Length; i++)
        {
            var tv = (GameObject)PrefabUtility.InstantiatePrefab(tvPrefab, scene);
            tv.name = $"TV (room {i})";
            tv.transform.SetParent(setup.transform, true);
            tv.transform.position = TvAt[i];
            tvs[i] = tv.GetComponent<Animator>();
        }

        // ---------- building spots ----------
        var spotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Buildings/Building Spot.prefab");
        void Spot(Vector2 at, string only)
        {
            var s = (GameObject)PrefabUtility.InstantiatePrefab(spotPrefab, scene);
            s.transform.SetParent(setup.transform, true);
            s.transform.position = at;
            s.name = $"Building Spot ({only})";
            s.GetComponent<BuildingSpot>().onlyFor = only;
        }
        // Just inside each room's entrance, under the path the dino walks in on, so they're in view straight away.
        Spot(new Vector2(71f, -2f), "Ice Cream Stand");
        Spot(new Vector2(74f, -2f), "Ice Cream Stand");
        Spot(new Vector2(77f, -2f), "Ice Cream Stand");
        Spot(new Vector2(113f, -2f), "Turret");
        Spot(new Vector2(147f, -2.625f), "Shield Generator"); // over the tall crabs' hallway: its shield closes it off

        // ---------- the drilling wall and the ore ----------
        var walls = Find<DestructibleTilemap>().GetComponent<Tilemap>();
        var newCluster = typeof(MeteoritePlacement).GetMethod("NewCluster", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var oreInFront = typeof(MeteoritePlacement).GetMethod("OreInFront", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        foreach (var old in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeteoriteCluster>(true)).Where(c => !c.unbreakable).ToArray())
            Undo.DestroyObjectImmediate(old.gameObject); // a rebuild replaces the last wall and ore
        GameObject Cluster(string name, IEnumerable<Vector2Int> cells, TileBase tile)
        {
            var dict = cells.ToDictionary(c => walls.WorldToCell(new Vector3(c.x + 0.5f, c.y + 0.5f)), _ => tile);
            var go = (GameObject)newCluster.Invoke(null, new object[] { walls, null, name, dict, null, false });
            return go;
        }
        var rock = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tiles/Generated/WallRuleTile.asset");
        var wallCells = new List<Vector2Int>();
        for (int x = DrillWall.xMin; x < DrillWall.xMax; x++) for (int y = DrillWall.yMin; y < DrillWall.yMax; y++) wallCells.Add(new Vector2Int(x, y));
        Cluster("Drilling Wall", wallCells, rock);
        var copperTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tiles/Generated/Ore Brown/OreBrownRuleTile.asset");
        var iridiumTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tiles/Generated/Ore Green/OreGreenRuleTile.asset");
        foreach (var c in Copper) oreInFront.Invoke(null, new object[] { Cluster("Ore Vein", new[] { c }, copperTile).GetComponent<MeteoriteCluster>() });
        foreach (var c in Iridium) oreInFront.Invoke(null, new object[] { Cluster("Green Ore Vein", new[] { c }, iridiumTile).GetComponent<MeteoriteCluster>() });
        log.Add($"drilling wall {wallCells.Count} tiles, {Copper.Length} copper + {Iridium.Length} iridium");

        // ---------- the dino: starts hurt, can't die in training ----------
        var dino = Find<DinoController>();
        var health = dino.GetComponent<Health>();
        Undo.RecordObject(health, "Build training level");
        health.startHealth = 40;
        health.minHealth = 1;
        PrefabUtility.RecordPrefabInstancePropertyModifications(health);

        // ---------- the walk-in ----------
        var intro = Find<TrainingIntro>();
        Undo.RecordObject(intro, "Build training level");
        intro.talkTo = tvs[0].transform;
        intro.dialogue = null;
        intro.rearmAfter = false;
        intro.stopDistance = 3f;

        // ---------- the director ----------
        var d = setup.AddComponent<TrainingDirector>();
        d.dino = dino;
        d.intro = intro;
        d.tvs = tvs;
        d.roomAreas = new[]
        {
            Area(-8, -4, 20, 7), Area(29, -4, 42, 7), Area(69, -4, 98, 7),
            Area(107, -4, 130, 7), Area(139, -4, 162, 7), Area(139, 13, 162, 24),
        };
        d.pastTheWall = Area(46, -4, 60, 7);
        d.generalPortrait = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Portraits/Bad Guy Portrait.controller");
        d.generalVoice = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/General Talking Loop.wav");
        d.computerPortrait = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Portraits/TV Face Portrait.controller");
        d.crabPrefab = AssetDatabase.LoadAssetAtPath<EnemyController>("Assets/Prefabs/Crab1.prefab");
        d.tallCrabPrefab = AssetDatabase.LoadAssetAtPath<EnemyController>("Assets/Prefabs/TallCrab.prefab");
        d.capturedCrabAt = new Vector2(86f, 1.5f);
        d.mateEntersAt = new Vector2(97.5f, 1.5f);
        d.mateDancesAt = new Vector2(90f, 1.5f);
        d.swarmEntersAt = new Vector2(129.5f, 1.5f);
        d.swarmGoesTo = new Vector2(114f, 1f); // within the turret spot's range (4)
        d.tallCrabsEnterAt = new Vector2(147f, -18f);
        d.tallCrabsGoTo = new Vector2(147f, 2f);
        d.walkToFinalRoom = new[] { new Vector2(151f, 2f), new Vector2(151f, 10f), new Vector2(151f, 17f) };

        EditorSceneManager.MarkSceneDirty(scene);
        return string.Join("; ", log);
    }
}
