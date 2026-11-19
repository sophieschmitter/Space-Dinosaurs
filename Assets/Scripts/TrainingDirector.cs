using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the training level, room by room. Every line is said over the TV in the dino's current room: the General
/// (his animated portrait; the TV shows him) or the Computer (the TV's face). Each step waits for the player to
/// do what was asked: walk into a room, drill through the wall, mine the ore, build the ice cream stand and eat
/// from it, kill the captured crab with the drill and its mate with the gun, build a turret against a swarm,
/// build a shield generator in front of the tall crabs' hallway. Then the dino walks itself into the last room,
/// the Computer signs off, and Level Select loads.
///
/// Rooms are rectangles in world space (Room Areas); TVs[i] is room i's TV. Craft buttons are locked except the
/// one the current step asks for, and Q / the weapon buttons only work once the gun has been handed out.
/// The texts are all editable below. Set up by Tools > Space Dinosaurs > Build Training Level.
/// </summary>
public class TrainingDirector : MonoBehaviour
{
    [Header("Who and where")]
    public DinoController dino;
    public TrainingIntro intro;
    [Tooltip("Room 0..5 interiors (world rectangles): 0 intro, 1 wall + ore (west half), 2 ice cream + crabs, 3 turret, 4 shield, 5 final.")]
    public Rect[] roomAreas = new Rect[6];
    [Tooltip("The far side of the drilling wall in room 1, where the ore is.")]
    public Rect pastTheWall;
    [Tooltip("Each room's TV (Animator with Idle / Talk / Bad Guy states).")]
    public Animator[] tvs = new Animator[6];

    [Header("Portraits")]
    public RuntimeAnimatorController generalPortrait;
    [Tooltip("Loops while the General is talking (not the Computer).")]
    public AudioClip generalVoice;
    public string generalState = "Talk";
    public RuntimeAnimatorController computerPortrait;
    public string computerState = "Neutral";
    public string generalName = "General", computerName = "Computer";

    [Header("Crabs")]
    public EnemyController crabPrefab;
    public EnemyController tallCrabPrefab;
    [Tooltip("Room 2: where the captured crab dances.")]
    public Vector2 capturedCrabAt;
    [Tooltip("Room 2: its mate comes in here and walks to Mate Dances At.")]
    public Vector2 mateEntersAt, mateDancesAt;
    [Tooltip("Room 3: the five crabs come in here and head for Swarm Goes To.")]
    public Vector2 swarmEntersAt, swarmGoesTo;
    [Min(1)] public int swarmSize = 5;
    [Tooltip("Room 4: the tall crabs start at the far end of the narrow hallway and head for Tall Crabs Go To.")]
    public Vector2 tallCrabsEnterAt, tallCrabsGoTo;
    [Min(1)] public int tallCrabCount = 3;
    [Tooltip("The tall crabs walk this many times faster than usual here (they're slow otherwise).")]
    public float tallCrabSpeedUp = 2f;

    [Header("Ending")]
    [Tooltip("The dino walks itself through these points into the last room once the shield is up.")]
    public Vector2[] walkToFinalRoom;
    public string nextScene = "Level Select";

    [Header("What it takes")]
    [Tooltip("Ore to mine in the wall room before moving on (enough for the ice cream stand and the turret). " +
             "The shield room has more for the shield generator.")]
    public int copperNeeded = 6, iridiumNeeded = 2;
    public string iceCreamItem = "Ice Cream Stand", turretItem = "Turret", shieldItem = "Shield Generator";

    [Header("Lines")]
    [TextArea(1, 3)] public string advanceHelp = "[Click, or press Space or Enter, to continue.]";
    [TextArea(3, 6)] public string welcome = "Welcome to the asteroid mineral acquisition team. You're doing a great service to your paleocomrades and our Earth Reclamation Secret Project. You will face great peril, but you play a critical role for our paleocountry. Your training starts now.";
    [TextArea(2, 4)] public string drillGiven = "Here is your military grade paleodrill.";
    [TextArea(3, 6)] public string drillExplained = "With it, you possess the power to dig through rocks and collect ore. Asteroids are rich in copper and iridium, both of which you will need to gather. Proceed to the first training room.";
    [TextArea(1, 3)] public string moveHelp = "[Use WASD to move, and Shift to sprint.]";
    [TextArea(2, 4)] public string wallBlocked = "The path forward seems to have been blocked. I shall punish the carnivores responsible. Try drilling through the wall to clear a path.";
    [TextArea(1, 3)] public string drillHelp = "[SpaceBar to drill]";
    [TextArea(2, 4)] public string mineOre = "Your performance thus far has been adequate. Now, try mining this ore. It will be used to craft necessary buildings that will help you during your mission.";
    [TextArea(2, 4)] public string oreDone = "This is not satisfactory. You must work faster if you want to serve our PaleoEmpire. Go to the next room.";
    [TextArea(2, 4)] public string buildIceCream = "Your vitals indicate that you need a boost to your health. Use the resources you just collected to make an Ice Cream Stand. It can be placed in any of these designated building zones.";
    [TextArea(1, 3)] public string craftHelp = "[Click to select it from the crafting menu with your mouse, then click to place it in the room]";
    [TextArea(2, 4)] public string eatIceCream = "I give you permission to walk up to the stand and consume the cone. Do not worry, the Compsognathus trapped inside will make more. He makes a new cone every 20 seconds or so.";
    [TextArea(2, 4)] public string capturedCrab = "We have captured a Rusty Crab for training purposes. It appears to be performing a mating dance. Destroy it.";
    [TextArea(1, 3)] public string drillAttackHelp = "[Press SpaceBar to use your drill]";
    [TextArea(1, 3)] public string firstCrabDead = "Yes, adequate.";
    [TextArea(2, 4)] public string mateArrives = "It appears we have found its mate. Shoot it, with your laser gun.";
    [TextArea(1, 3)] public string gunHelp = "[Press Q to switch weapons, and SpaceBar or click to shoot]";
    [TextArea(1, 3)] public string toTurretRoom = "Proceed to the next room.";
    [TextArea(2, 4)] public string twoBirds = "As the saying goes, we have killed two birds with two weapons.";
    [TextArea(3, 6)] public string buildTurret = "I do not know where they keep coming from. No matter—I have been craving some paleoseafood recently anyway. Try crafting a gun turret to help you ward off these delicious invaders, then proceed to the next room.";
    [TextArea(3, 6)] public string buildShield = "I angered some tall crabs earlier today when I landed on their queen with my spaceship. It appears they are trying to attack our base. Build a shield generator to keep their projectiles away.";
    [TextArea(1, 3)] public string shieldOreHelp = "[Mine the ore in this room to afford the shield generator.]";
    [TextArea(4, 8)] public string finale = "Congratulations! You've completed the training module. Your performance has been adequate, adequate indeed. But you'll have to do better on your first mission. Your job is to protect the mining robot from alien attackers. For some reason they don't like us fracking their asteroids. Your first mission will be on an asteroid belt not far from here...";
    public string finaleState = "Happy";

    /// <summary>Which step it's on, for debugging (shown in the Inspector while playing).</summary>
    [SerializeField] string step = "";

    int room;
    readonly List<string> crafted = new();
    readonly Dictionary<string, CraftButton> buttons = new();

    void OnEnable()
    {
        DialogueBox.LineShown += OnLine;
        DialogueBox.Closed += OnBoxClosed;
        CraftButton.Crafted += OnCrafted;
    }

    void OnDisable()
    {
        DialogueBox.LineShown -= OnLine;
        DialogueBox.Closed -= OnBoxClosed;
        CraftButton.Crafted -= OnCrafted;
    }

    void OnCrafted(string item) => crafted.Add(item);

    // The room's TV shows whoever is talking.
    void OnLine(DialogueLine line) => Tv(line.speaker == generalName ? "Bad Guy" : "Talk");
    void OnBoxClosed() => Tv("Idle");

    void Tv(string state)
    {
        if (room < 0 || room >= tvs.Length || tvs[room] == null) return;
        tvs[room].updateMode = AnimatorUpdateMode.UnscaledTime; // the game is paused while the box is open
        tvs[room].Play(state, 0, 0f);
    }

    IEnumerator Start()
    {
        if (dino == null) dino = FindFirstObjectByType<DinoController>();
        if (intro == null) intro = FindFirstObjectByType<TrainingIntro>();
        foreach (var b in FindObjectsByType<CraftButton>(FindObjectsSortMode.None)) { buttons[b.itemName] = b; b.locked = true; }
        dino.canSwapWeapons = false;

        // ---------- room 0: welcome ----------
        Step("walk in");
        room = 0;
        while (intro != null && !intro.Done) yield return null;
        dino.Unarm();
        yield return Say(Computer(advanceHelp), General(welcome), General(drillGiven));
        dino.SetWeapons(true, false); // the drill dino
        yield return Say(General(drillExplained), Computer(moveHelp));

        // ---------- room 1: the wall, then the ore ----------
        Step("go to the wall room");
        yield return Enter(1);
        yield return Say(General(wallBlocked), Computer(drillHelp));
        Step("drill through the wall");
        yield return WaitInside(pastTheWall);
        yield return Say(General(mineOre));
        Step("mine the ore");
        while (!HaveOre()) yield return null;
        yield return Say(General(oreDone));

        // ---------- room 2: ice cream, then two crabs ----------
        Step("go to the ice cream room");
        yield return Enter(2);
        yield return Say(General(buildIceCream), Computer(craftHelp));
        Step("build the ice cream stand");
        yield return Build(iceCreamItem);
        yield return Say(General(eatIceCream));
        Step("eat the ice cream");
        while (!AteIceCream()) yield return null;

        var captured = Spawn(crabPrefab, capturedCrabAt);
        captured.Celebrate();
        yield return null;
        yield return Say(General(capturedCrab), Computer(drillAttackHelp));
        Step("drill the captured crab");
        while (Alive(captured)) yield return null;

        yield return Say(General(firstCrabDead));
        var mate = Spawn(crabPrefab, mateEntersAt);
        yield return WalkTo(mate, mateDancesAt);
        if (Alive(mate)) mate.Celebrate();
        dino.canSwapWeapons = true;
        yield return Say(General(mateArrives), Computer(gunHelp));
        Step("shoot the mate");
        while (Alive(mate)) yield return null;
        yield return Say(General(toTurretRoom));

        // ---------- room 3: the swarm and the turret ----------
        Step("go to the turret room");
        yield return Enter(3);
        yield return Say(General(twoBirds));
        var swarm = new List<EnemyController>();
        for (int i = 0; i < swarmSize; i++)
        {
            var c = Spawn(crabPrefab, swarmEntersAt + new Vector2(0f, (i % 3 - 1) * 0.9f));
            Send(c, swarmGoesTo);
            swarm.Add(c);
            yield return new WaitForSeconds(0.35f);
        }
        yield return new WaitForSeconds(1f); // let them get into the room
        yield return Say(General(buildTurret));
        Step("build the turret and beat the swarm");
        yield return Build(turretItem);
        while (swarm.Exists(Alive)) yield return null;

        // ---------- room 4: the tall crabs and the shield ----------
        Step("go to the shield room");
        yield return Enter(4);
        yield return Say(General(buildShield), Computer(shieldOreHelp));
        for (int i = 0; i < tallCrabCount; i++)
        {
            var t = Spawn(tallCrabPrefab, tallCrabsEnterAt + new Vector2((i % 2 == 0 ? -0.8f : 0.8f), 1.6f * i)); // up the hallway, side by side
            t.huntSpeed *= tallCrabSpeedUp;
            t.chaseSpeed *= Mathf.Max(1f, tallCrabSpeedUp * 0.75f);
            Send(t, tallCrabsGoTo);
        }
        Step("build the shield generator");
        yield return Build(shieldItem);
        yield return new WaitForSeconds(1.5f); // watch it go up

        // ---------- room 5: the end ----------
        Step("walk to the final room");
        yield return AutoWalk(walkToFinalRoom);
        room = 5;
        yield return Say(Computer(finale, finaleState));
        Step("done");
        SceneManager.LoadScene(nextScene);
    }

    // ---------- steps ----------

    void Step(string s) => step = s;

    DialogueLine General(string text) => new DialogueLine { speaker = generalName, portraitAnimation = generalPortrait, portraitState = generalState, voice = generalVoice, text = text };
    DialogueLine Computer(string text, string state = null) => new DialogueLine { speaker = computerName, portraitAnimation = computerPortrait, portraitState = state ?? computerState, text = text };

    IEnumerator Say(params DialogueLine[] lines)
    {
        var box = DialogueBox.Instance;
        if (box == null) yield break;
        bool talking = true;
        box.Play(lines, () => talking = false);
        while (talking) yield return null;
    }

    IEnumerator Enter(int index)
    {
        yield return WaitInside(roomAreas[index]);
        room = index;
    }

    IEnumerator WaitInside(Rect area)
    {
        while (!area.Contains(dino.transform.position)) yield return null;
    }

    // Unlocks one craft button and waits until that building has been put down.
    IEnumerator Build(string item)
    {
        crafted.Clear();
        if (buttons.TryGetValue(item, out var b)) b.locked = false;
        while (!crafted.Contains(item)) yield return null;
        if (b != null) b.locked = true;
    }

    bool HaveOre()
    {
        var inv = ResourceInventory.Instance;
        if (inv == null) return true;
        return inv.Get(ResourceType.Copper) >= copperNeeded && inv.Get(ResourceType.Iridium) >= iridiumNeeded;
    }

    static bool AteIceCream()
    {
        foreach (var s in FindObjectsByType<IceCreamStand>(FindObjectsSortMode.None))
            if (!s.HasIceCream) return true;
        return false;
    }

    static bool Alive(EnemyController e) => e != null && !e.IsDead;

    EnemyController Spawn(EnemyController prefab, Vector2 at) => Instantiate(prefab, at, Quaternion.identity);

    // Sends an enemy along a path to a point (a temporary goal there), after which it behaves normally.
    static void Send(EnemyController e, Vector2 to)
    {
        var goal = new GameObject("Training Goal").AddComponent<EnemyGoal>();
        goal.transform.position = to;
        goal.arriveDistance = 0.6f;
        e.GoTo(goal);
        Destroy(goal.gameObject, 20f);
    }

    // Walks an enemy to a point; done once it's close (or has stopped, or after a few seconds).
    IEnumerator WalkTo(EnemyController e, Vector2 to, float closeEnough = 2f, float give = 6f)
    {
        Send(e, to);
        while (Alive(e) && e.IsHunting && give > 0f && Vector2.Distance(e.transform.position, to) > closeEnough)
        {
            give -= Time.deltaTime;
            yield return null;
        }
    }

    // The dino walks itself through the points (player control off), then stops.
    IEnumerator AutoWalk(Vector2[] points)
    {
        if (points == null || points.Length == 0) yield break;
        dino.BeginScripted();
        var body = dino.GetComponent<Rigidbody2D>();
        foreach (var p in points)
        {
            float give = 15f;
            while (Vector2.Distance(body.position, p) > 0.4f && give > 0f)
            {
                dino.ScriptedMove((p - body.position).normalized);
                give -= Time.deltaTime;
                yield return null;
            }
        }
        dino.ScriptedMove(Vector2.zero);
        yield return new WaitForSeconds(0.4f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        if (roomAreas != null) foreach (var r in roomAreas) Gizmos.DrawWireCube(r.center, r.size);
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
        Gizmos.DrawWireCube(pastTheWall.center, pastTheWall.size);
    }
}
