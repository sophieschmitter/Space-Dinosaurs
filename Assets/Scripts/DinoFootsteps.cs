using UnityEngine;

/// <summary>
/// The dino's footsteps, in time with its walk animation: each time a foot lands in the animation, one short
/// step sound plays (picked at random from Steps, never the same one twice in a row). Sprinting plays the walk
/// animation faster, so the steps speed up with it; they're also pitched up a little. Quiet while the game is
/// paused and once the dino is dead.
///
/// Where the feet land, as a fraction of each walk loop (measured from the art: where the body dips lowest):
/// side and front walks at 0 and 0.5, walking away (Back) at 0 and 0.4.
/// </summary>
[RequireComponent(typeof(DinoController))]
public class DinoFootsteps : MonoBehaviour
{
    [Tooltip("Single footsteps (Assets/SFX/Footstep Steps: cut from the cleaned recording).")]
    public AudioClip[] steps;
    [Range(0f, 1f)] public float volume = 0.35f;
    [Tooltip("Pitch while sprinting (1 = normal).")]
    [Range(1f, 1.5f)] public float sprintPitch = 1.1f;
    [Tooltip("Random pitch change per step, so repeats don't sound identical.")]
    [Range(0f, 0.15f)] public float pitchJitter = 0.04f;

    [Tooltip("Where the feet land in the side and front walk loops (0-1).")]
    public float[] footfalls = { 0f, 0.5f };
    [Tooltip("Where the feet land in the walking-away (Back) loop (0-1).")]
    public float[] backFootfalls = { 0f, 0.4f };

    DinoController dino;
    Animator animator;
    AudioSource source;
    int lastState;
    float lastTime = -1f;   // walk loop position last frame (unwrapped normalized time)
    int lastStep = -1;

    void Awake()
    {
        dino = GetComponent<DinoController>();
        animator = GetComponentInChildren<Animator>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // plain 2D sound: the camera follows the dino anyway
    }

    void Update()
    {
        if (steps == null || steps.Length == 0 || animator == null) return;
        bool active = Time.timeScale > 0f && !dino.IsDead && dino.IsMoving;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        var clips = animator.GetCurrentAnimatorClipInfo(0);
        string clipName = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "";
        bool walking = active && clipName.Contains("Walk");
        if (!walking) { lastTime = -1f; return; }

        float now = info.normalizedTime;
        if (lastTime < 0f)
        {
            // just started walking: the first foot is down straight away
            lastState = info.fullPathHash;
            lastTime = Mathf.Floor(now) - 0.0001f;
        }
        else if (info.fullPathHash != lastState)
        {
            lastState = info.fullPathHash;
            // Turned: the new walk starts from its first frame, where a foot lands. Swapped to the gun's recoil
            // version of the same walk: it carries on from the same frame, so no extra step.
            lastTime = now < 0.05f ? Mathf.Floor(now) - 0.0001f : now;
        }
        var marks = clipName.Contains("Back") ? backFootfalls : footfalls;
        // Every footfall passed since last frame (normalized time keeps counting up across loops).
        for (float loop = Mathf.Floor(lastTime); loop <= Mathf.Floor(now); loop++)
            foreach (float m in marks)
            {
                float t = loop + m;
                if (t > lastTime && t <= now) { Step(); goto done; } // at most one step per frame
            }
        done:
        lastTime = now;
    }

    [Tooltip("Never two steps closer together than this (seconds), e.g. turning just after a footfall.")]
    public float minGap = 0.12f;
    float lastStepTime = -1f;

    void Step()
    {
        if (Time.time - lastStepTime < minGap) return;
        lastStepTime = Time.time;
        int i = Random.Range(0, steps.Length);
        if (steps.Length > 1 && i == lastStep) i = (i + 1) % steps.Length;
        lastStep = i;
        source.pitch = (dino.IsSprinting ? sprintPitch : 1f) * (1f + Random.Range(-pitchJitter, pitchJitter));
        source.PlayOneShot(steps[i], volume);
    }
}
