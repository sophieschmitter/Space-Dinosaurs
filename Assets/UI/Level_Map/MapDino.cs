using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The little dino on the level map. The Level Map tells it where to go and it walks there along the
/// dotted path, using the dino's own walk animations (left / right / up / down), then stands facing the camera.
/// Long trips speed up so no walk takes longer than Max Walk Time.
/// Needs an Animator using Assets/Animations/Dino/Dino.controller.
/// </summary>
[RequireComponent(typeof(Animator))]
public class MapDino : MonoBehaviour
{
    [Tooltip("Walking speed in world units per second.")]
    public float walkSpeed = 4f;
    [Tooltip("The longest any walk may take, in seconds; longer trips speed up to fit.")]
    public float maxWalkTime = 2f;
    [Tooltip("Shift the dino from the path point (e.g. to lift its feet onto the asteroid).")]
    public Vector2 feetOffset = Vector2.zero;

    [Header("Animation states")]
    public string walkRight = "RWalk";
    public string walkLeft = "LWalk";
    public string walkUp = "Back_Walk";
    public string walkDown = "Front_Walk";
    public string idle = "Front_Idle";

    public bool IsMoving { get; private set; }

    Animator animator;
    string currentState;
    Coroutine walking;
    bool facingHorizontal = true;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>Jump straight to a point and stand there.</summary>
    public void PlaceAt(Vector3 point)
    {
        Stop();
        transform.position = WithFeet(point);
        Play(idle);
    }

    /// <summary>Walk through these points in order, then stand.</summary>
    public void WalkPath(IList<Vector3> points)
    {
        Stop();
        if (points == null || points.Count == 0) return;
        walking = StartCoroutine(Walk(points));
    }

    void Stop()
    {
        if (walking != null) StopCoroutine(walking);
        walking = null;
        IsMoving = false;
    }

    IEnumerator Walk(IList<Vector3> points)
    {
        IsMoving = true;

        float length = 0f;
        Vector3 previous = transform.position;
        foreach (var p in points)
        {
            Vector3 q = WithFeet(p);
            length += Vector3.Distance(previous, q);
            previous = q;
        }
        float speed = Mathf.Max(walkSpeed, length / Mathf.Max(0.1f, maxWalkTime));

        int index = 0;
        while (index < points.Count)
        {
            Vector3 position = transform.position;
            Vector3 before = position;
            float step = speed * Time.unscaledDeltaTime;
            // Use up this frame's whole step, even across several path points.
            while (step > 0f && index < points.Count)
            {
                Vector3 target = WithFeet(points[index]);
                float distance = Vector3.Distance(position, target);
                if (distance <= step)
                {
                    position = target;
                    step -= distance;
                    index++;
                }
                else
                {
                    position += (target - position) / distance * step;
                    step = 0f;
                }
            }
            transform.position = position;
            Face(position - before);
            yield return null;
        }

        IsMoving = false;
        walking = null;
        Play(idle);
    }

    void Face(Vector3 direction)
    {
        float ax = Mathf.Abs(direction.x), ay = Mathf.Abs(direction.y);
        if (ax < 0.0001f && ay < 0.0001f) return;
        // A little stickiness so the dino doesn't flicker between side and front on diagonals.
        if (facingHorizontal) facingHorizontal = ax * 1.5f >= ay;
        else facingHorizontal = ax >= ay * 1.5f;

        if (facingHorizontal) Play(direction.x >= 0f ? walkRight : walkLeft);
        else Play(direction.y > 0f ? walkUp : walkDown);
    }

    Vector3 WithFeet(Vector3 point)
    {
        point += (Vector3)feetOffset;
        point.z = transform.position.z;
        return point;
    }

    void Play(string state)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        if (string.IsNullOrEmpty(state) || state == currentState) return;
        int hash = Animator.StringToHash(state);
        if (!animator.HasState(0, hash)) return;
        animator.Play(hash, 0, 0f);
        currentState = state;
    }
}
