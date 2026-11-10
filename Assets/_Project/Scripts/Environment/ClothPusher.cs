using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A body that hanging cloth gets out of the way of (<see cref="HangingCloth"/>): a vertical capsule
/// standing on this transform. Its one job is to say where that capsule is.
///
/// Nobody places it by hand: the player gets one the moment it registers (it follows the player's own
/// CapsuleCollider, so crouching under a curtain pushes less of it), and the Nemesis adds its own
/// (it has no collider at all, so the size is given).
/// </summary>
[DisallowMultipleComponent]
public class ClothPusher : MonoBehaviour
{
    private static readonly List<ClothPusher> active = new List<ClothPusher>();

    /// <summary>Every enabled pusher.</summary>
    public static IReadOnlyList<ClothPusher> Active => active;

    [Tooltip("Radio del cuerpo, en metros. No se usa si sigue a un CapsuleCollider.")]
    [SerializeField, Min(0.05f)] private float radius = 0.35f;

    [Tooltip("Alto del cuerpo desde este transform (los pies), en metros.")]
    [SerializeField, Min(0.1f)] private float height = 1.86f;

    [Tooltip("Si está, la cápsula es la de este collider (su alto, su radio y su centro).")]
    [SerializeField] private CapsuleCollider follow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active.Clear();

    // After PlayerRegistry's own reset (SubsystemRegistration), before any player registers.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void FollowThePlayer() => PlayerRegistry.SubscribeAndCatchUp(AttachToPlayer);

    private static void AttachToPlayer(PlayerStateManager player)
    {
        if (player == null || player.GetComponent<ClothPusher>() != null) return;

        ClothPusher pusher = player.gameObject.AddComponent<ClothPusher>();
        pusher.follow = player.CapsuleColl;
    }

    /// <summary>Gives <paramref name="body"/> a pusher of this size, unless it has one.</summary>
    public static ClothPusher Attach(GameObject body, float radius, float height)
    {
        ClothPusher pusher = body.GetComponent<ClothPusher>();
        if (pusher != null) return pusher;

        pusher = body.AddComponent<ClothPusher>();
        pusher.radius = radius;
        pusher.height = height;
        return pusher;
    }

    private void OnEnable()
    {
        if (!active.Contains(this)) active.Add(this);
    }

    private void OnDisable() => active.Remove(this);

    /// <summary>The body right now, in world space.</summary>
    public HangingClothSolver.Capsule Capsule
    {
        get
        {
            if (follow != null)
            {
                float r = follow.radius;
                Vector3 centre = follow.transform.TransformPoint(follow.center);
                float half = Mathf.Max(0f, follow.height * 0.5f - r);
                return new HangingClothSolver.Capsule(centre - Vector3.up * half, centre + Vector3.up * half, r);
            }

            Vector3 feet = transform.position;
            float top = Mathf.Max(radius, height - radius);
            return new HangingClothSolver.Capsule(feet + Vector3.up * radius, feet + Vector3.up * top, radius);
        }
    }
}
