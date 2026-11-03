using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Throws the pieces of meat left on the floor when the run-ending explosion takes the body. Real
/// rigidbodies, so they land on whatever is there and stay: nothing removes them, the level's unload
/// does (Retry and the main menu both unload it, which is why they are moved to the player's scene).
/// How many and how hard: SO_ModuleExplosionConfig § Run-ending explosion.
/// </summary>
public static class MeatChunkBurst
{
    private const float MinSpin = 4f;
    private const float MaxSpin = 14f;

    public static void Spawn(SO_ModuleExplosionConfig config, Vector3 origin, PlayerStateManager player)
    {
        GameObject[] prefabs = config.MeatChunkPrefabs;
        if (prefabs == null || prefabs.Length == 0) return;

        // The capsule stays behind, invisible, where the body was.
        Collider[] body = player != null ? player.GetComponentsInChildren<Collider>(true) : new Collider[0];

        for (int i = 0; i < config.MeatChunkCount; i++)
        {
            GameObject prefab = prefabs[i % prefabs.Length];
            if (prefab == null) continue;

            Vector3 at = origin + Random.insideUnitSphere * config.MeatChunkSpread;
            GameObject chunk = Object.Instantiate(prefab, at, Random.rotation);
            chunk.transform.localScale *= Random.Range(config.MeatChunkMinScale, config.MeatChunkMaxScale);

            if (player != null) SceneManager.MoveGameObjectToScene(chunk, player.gameObject.scene);

            foreach (Collider chunkCollider in chunk.GetComponentsInChildren<Collider>())
                foreach (Collider part in body)
                    Physics.IgnoreCollision(chunkCollider, part);

            if (!chunk.TryGetComponent(out Rigidbody rigidbody)) continue;

            // Outwards and never straight down: a blast at the chest throws them up and away.
            Vector3 direction = Random.onUnitSphere;
            direction.y = Mathf.Abs(direction.y);
            rigidbody.linearVelocity = direction * Random.Range(config.MeatChunkMinSpeed, config.MeatChunkMaxSpeed);
            rigidbody.angularVelocity = Random.onUnitSphere * Random.Range(MinSpin, MaxSpin);
        }
    }
}
