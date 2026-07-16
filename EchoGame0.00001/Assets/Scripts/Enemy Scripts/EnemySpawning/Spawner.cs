using UnityEngine;
using UnityEngine.Pool;

public class Spawner : MonoBehaviour
{
    [Tooltip("Spawn point arrays for the enemies spawning script")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("Time between spawning new enemies will be adjustable in the inspector")]
    [SerializeField] private float timeBetweenSpawning = 5f;
    [SerializeField] private EnemyFollowPlayer enemyPrefab;

    [Header("Pool")]
    [Tooltip("Ceiling on enemies alive at once. Note this is NOT the pool's maxSize — that only caps how many dead enemies are kept in reserve.")]
    [SerializeField] private int maxActiveEnemies = 15;
    [Tooltip("Starting size of the pool's internal list. Set near the count you expect alive at once.")]
    [SerializeField] private int poolCapacity = 10;
    [Tooltip("Dead enemies kept in reserve. Releases past this are destroyed instead of pooled.")]
    [SerializeField] private int poolMaxSize = 30;

    private float nextSpawnTime;
    private int nextSpawnIndex;

    // Concrete ObjectPool, not IObjectPool: the interface only exposes CountInactive,
    // and the live cap below needs CountActive. The enemy still takes the interface
    // in SetPool, so it stays decoupled from the pool's concrete type.
    public ObjectPool<EnemyFollowPlayer> EnemyPool;

    private void Awake()
    {
        EnemyPool = new ObjectPool<EnemyFollowPlayer>(
            CreateEnemy,
            OnGetFromPool,
            OnReleaseToPool,
            OnDestroyPooledEnemy,
            collectionCheck: true,
            defaultCapacity: poolCapacity,
            maxSize: poolMaxSize);
    }

    private EnemyFollowPlayer CreateEnemy()
    {
        // Spawn on a real point rather than the origin: a NavMeshAgent that wakes
        // up off the navmesh logs a warning and can't path. This deliberately does
        // not advance the cursor — OnGetFromPool runs straight after and does the
        // real placement.
        Transform first = spawnPoints[0];
        EnemyFollowPlayer enemy = Instantiate(enemyPrefab, first.position, first.rotation);
        enemy.SetPool(EnemyPool);
        enemy.gameObject.SetActive(false);
        return enemy;
    }

    private void OnGetFromPool(EnemyFollowPlayer enemy)
    {
        Transform point = NextSpawnPoint();
        // Placed while still inactive: the agent re-attaches to the navmesh at its
        // transform position when the object is enabled, so moving it after that
        // would desync the agent from its path and snap it back.
        enemy.transform.SetPositionAndRotation(point.position, point.rotation);
        enemy.gameObject.SetActive(true);
    }

    private void OnReleaseToPool(EnemyFollowPlayer enemy)
    {
        enemy.gameObject.SetActive(false);
    }

    private void OnDestroyPooledEnemy(EnemyFollowPlayer enemy)
    {
        Destroy(enemy.gameObject);
    }

    // Walks the spawn points in inspector order and wraps. Order is authored, so
    // keep it deterministic — no random pick.
    private Transform NextSpawnPoint()
    {
        Transform point = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;
        return point;
    }

    void Update()
    {
        if (Time.time < nextSpawnTime) return;
        if (spawnPoints == null || spawnPoints.Length == 0) return;

        // Reset the timer before the cap check, not after. Otherwise sitting at the
        // cap leaves nextSpawnTime stuck in the past, and the frame you kill someone
        // a replacement pops instantly instead of waiting out the interval.
        nextSpawnTime = Time.time + timeBetweenSpawning;

        if (EnemyPool.CountActive >= maxActiveEnemies) return;

        //spawn an enemy
        EnemyPool.Get();
    }
}
