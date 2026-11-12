using UnityEngine;

/// <summary>
/// Title screen decoration: the dino strolls across the bottom of the screen, stopping now and then,
/// and wraps around when it walks off the right edge. Uses the dino's own Animator Controller
/// (Assets/Animations/Dino/Dino.controller); state names that don't exist are simply skipped.
/// </summary>
[RequireComponent(typeof(Animator))]
public class TitleDino : MonoBehaviour
{
    public float walkSpeed = 2.2f;
    public string walkState = "RWalk";
    public string idleState = "RIdle";
    [Tooltip("Seconds of walking before a pause, random between these.")]
    public Vector2 walkTime = new Vector2(3f, 6f);
    [Tooltip("Seconds of standing still, random between these.")]
    public Vector2 idleTime = new Vector2(1f, 2.5f);
    [Tooltip("World units past the screen edge before wrapping to the other side.")]
    public float wrapMargin = 2f;

    Animator animator;
    Camera cam;
    bool walking;
    float timer;

    void Start()
    {
        animator = GetComponent<Animator>();
        cam = Camera.main;
        StartWalking();
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (walking)
        {
            transform.position += Vector3.right * (walkSpeed * Time.deltaTime);
            if (cam != null)
            {
                float halfWidth = cam.orthographicSize * cam.aspect + wrapMargin;
                float camX = cam.transform.position.x;
                if (transform.position.x > camX + halfWidth)
                {
                    var p = transform.position;
                    p.x = camX - halfWidth;
                    transform.position = p;
                }
            }
            if (timer <= 0f) StartIdle();
        }
        else if (timer <= 0f)
        {
            StartWalking();
        }
    }

    void StartWalking()
    {
        walking = true;
        timer = Random.Range(walkTime.x, walkTime.y);
        Play(walkState);
    }

    void StartIdle()
    {
        walking = false;
        timer = Random.Range(idleTime.x, idleTime.y);
        Play(idleState);
    }

    void Play(string state)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state)) return;
        int hash = Animator.StringToHash(state);
        if (animator.HasState(0, hash)) animator.Play(hash, 0, 0f);
    }
}
