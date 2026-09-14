using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// Runs a room's fight as a series of waves.
//
// Sits idle until something calls StartWaves() — normally a WaveStartTrigger in the
// doorway. Then it sends one wave at a time, waits for the room to be emptied before
// starting the next, and reports each cleared wave to the quest system so the HUD
// counts "Waves 2 / 3" and the door bound to that objective opens on the last one.
//
// Kept separate from Spawner on purpose. Spawner is the endless trickle the test
// scene uses; this is the scripted, quest-driven fight. This one keeps its OWN pool
// rather than inheriting, so the two can be tuned apart and the throwaway one can be
// deleted later without touching this.
public class WaveSpawner : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("Where the wave enemies come from. Walked in order and wrapped, so the order you drag them in is the order they get used.")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("The enemy every wave sends. This prefab must NOT carry an ObjectiveTarget in the same group as the wave objective below, or ordinary kills would count the waves a second time.")]
    [SerializeField] private EnemyFollowPlayer enemyPrefab;

    [Header("Waves")]
    [Tooltip("The waves, in order. The player has to empty the room before the next one starts. THE NUMBER OF WAVES HERE MUST MATCH the Required Count on the objective named below.")]
    [SerializeField] private List<Wave> waves = new List<Wave>();

    [Header("Pool")]
    [Tooltip("Ceiling on wave enemies alive at once. A wave bigger than this arrives in batches as the earlier ones die. Note this is NOT the pool's maxSize — that only caps how many dead enemies are kept in reserve.")]
    [SerializeField] private int maxActiveEnemies = 15;
    [Tooltip("Starting size of the pool's internal list. Set it near the count you expect alive at once.")]
    [SerializeField] private int poolCapacity = 10;
    [Tooltip("Dead enemies kept in reserve. Releases past this are destroyed instead of pooled.")]
    [SerializeField] private int poolMaxSize = 30;

    [Header("Quest")]
    [Tooltip("The tracker told about each cleared wave. Auto-found in the scene if left empty. Clear the Objective Id below instead if these waves aren't part of a quest.")]
    [SerializeField] private QuestTracker tracker;
    [Tooltip("Id of the objective that counts waves, e.g. \"survive-waves\". One point is reported per wave cleared, so set that objective's Required Count to the number of waves above. Leave EMPTY for waves that aren't tied to a quest.")]
    [SerializeField] private string surviveWavesObjectiveId = "survive-waves";

    [Header("Debug")]
    [Tooltip("Log each wave starting and clearing. Leave this on while building the level — it's the fastest way to see whether a wave actually finished.")]
    [SerializeField] private bool logWaves = true;

    // One wave of the fight. A plain data class so waves are authored in the
    // inspector rather than in code.
    [System.Serializable]
    public class Wave
    {
        [Tooltip("How many enemies this wave sends in total. Higher than Max Active Enemies is fine — they arrive in batches as the earlier ones die.")]
        [Min(1)] public int count = 5;
        [Tooltip("Seconds between each enemy in this wave. 0 puts the whole wave out in one go.")]
        [Min(0f)] public float spawnInterval = 1f;
        [Tooltip("Seconds of quiet before this wave starts spawning. This is the breather between waves.")]
        [Min(0f)] public float startDelay = 0f;
    }

    private int nextSpawnIndex;

    // currentWaveIndex only means anything while wavesRunning.
    private int currentWaveIndex;
    private int spawnedThisWave;
    private float nextWaveSpawnAt;
    private bool wavesRunning;
    private bool wavesStarted;

    // For a wave counter on the HUD later, and useful to watch in the inspector
    // while testing.
    public bool WavesRunning => wavesRunning;
    public int WaveCount => waves != null ? waves.Count : 0;
    public int CurrentWaveNumber => wavesRunning ? currentWaveIndex + 1 : 0;

    // Concrete ObjectPool, not IObjectPool: the interface only exposes CountInactive,
    // and the live cap and the wave-cleared check both need CountActive. The enemy
    // still takes the interface in SetPool, so it stays decoupled from the concrete
    // type.
    private ObjectPool<EnemyFollowPlayer> enemyPool;

    void Awake()
    {
        enemyPool = new ObjectPool<EnemyFollowPlayer>(
            CreateEnemy,
            OnGetFromPool,
            OnReleaseToPool,
            OnDestroyPooledEnemy,
            collectionCheck: true,
            defaultCapacity: poolCapacity,
            maxSize: poolMaxSize);

        // The same auto-find QuestHud does, so a spawner dropped into a level with
        // one tracker needs no wiring at all.
        if (tracker == null) tracker = FindObjectOfType<QuestTracker>();
    }

    void Start()
    {
        // These only bite the moment the player walks into the room, which can be
        // minutes in. Say so at load instead, while the level is still open.
        if (enemyPrefab == null)
            Debug.LogWarning($"[WaveSpawner] '{name}' has no Enemy Prefab, so its waves will never spawn anything. Drag an enemy prefab into the Enemy Prefab field.", this);
        if (spawnPoints == null || spawnPoints.Length == 0)
            Debug.LogWarning($"[WaveSpawner] '{name}' has no Spawn Points, so its waves have nowhere to come from. Drag at least one spawn point transform into the Spawn Points array.", this);
        if (waves.Count == 0)
            Debug.LogWarning($"[WaveSpawner] '{name}' has an EMPTY Waves list, so starting it would finish instantly. Add at least one wave.", this);
    }

    // ---------------------------------------------------------------- public API

    // Starts the first wave. Called by WaveStartTrigger when the player walks into
    // the room, and safe to call from a cutscene or a debug button too.
    //
    // Deliberately one-shot: walking back out of the room and in again shouldn't
    // restart the fight, or stack a second set of waves on top of the one running.
    public void StartWaves()
    {
        if (wavesStarted) return;

        if (waves.Count == 0)
        {
            Debug.LogWarning($"[WaveSpawner] '{name}' was told to start, but its Waves list is EMPTY so nothing will spawn. Add at least one wave.", this);
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning($"[WaveSpawner] '{name}' was told to start, but it has no Spawn Points. Drag at least one spawn point transform into the Spawn Points array.", this);
            return;
        }

        if (enemyPrefab == null)
        {
            Debug.LogWarning($"[WaveSpawner] '{name}' was told to start, but it has no Enemy Prefab. Drag an enemy prefab into the Enemy Prefab field.", this);
            return;
        }

        wavesStarted = true;
        wavesRunning = true;
        currentWaveIndex = 0;

        CheckObjectiveWiring();
        BeginWave(0);
    }

    // ---------------------------------------------------------------- waves

    void Update()
    {
        if (!wavesRunning) return;

        Wave wave = waves[currentWaveIndex];

        // Still sending this wave out. Counting what we have SPAWNED rather than
        // what's alive is what stops a wave being called clear in the gap before the
        // first enemy appears, or half way through when a fast player out-kills the
        // spawn rate.
        if (spawnedThisWave < wave.count)
        {
            SpawnWaveEnemies(wave);
            return;
        }

        // The whole wave is out, so now it counts as cleared once the room is empty.
        // CountActive is this spawner's OWN pool, so hand-placed enemies in the other
        // rooms can't hold the wave open, and every wave enemy releases back on death
        // (EnemyHealth -> EnemyFollowPlayer.ReturnToPool) which is what makes this
        // count exact rather than a guess.
        if (enemyPool.CountActive > 0) return;

        OnWaveCleared();
    }

    // A loop rather than one enemy per frame, so a Spawn Interval of 0 really does
    // put the whole wave out at once instead of dribbling one per frame. It always
    // ends: every pass either uses up one of the wave's count or hits the live cap.
    private void SpawnWaveEnemies(Wave wave)
    {
        while (spawnedThisWave < wave.count
            && Time.time >= nextWaveSpawnAt
            && enemyPool.CountActive < maxActiveEnemies)
        {
            enemyPool.Get();
            spawnedThisWave++;
            nextWaveSpawnAt = Time.time + wave.spawnInterval;
        }
    }

    private void BeginWave(int index)
    {
        Wave wave = waves[index];
        spawnedThisWave = 0;
        nextWaveSpawnAt = Time.time + wave.startDelay;

        if (logWaves)
            Debug.Log($"[WaveSpawner] Wave {index + 1} / {waves.Count} incoming — {wave.count} enemies.", this);
    }

    private void OnWaveCleared()
    {
        if (logWaves)
            Debug.Log($"[WaveSpawner] Wave {currentWaveIndex + 1} / {waves.Count} cleared.", this);

        // One point per wave, so the HUD line reads "Waves 2 / 3".
        if (tracker != null && !string.IsNullOrWhiteSpace(surviveWavesObjectiveId))
            tracker.ReportProgress(surviveWavesObjectiveId, 1);

        currentWaveIndex++;
        if (currentWaveIndex < waves.Count)
        {
            BeginWave(currentWaveIndex);
            return;
        }

        wavesRunning = false;
        if (logWaves)
            Debug.Log($"[WaveSpawner] All {waves.Count} waves cleared.", this);

        // Belt and braces. The report above already finishes the objective when its
        // Required Count matches the number of waves. This catches a Required Count
        // set too HIGH, which would otherwise leave the quest stuck and the door shut
        // with nothing left alive to kill. Does nothing if it's already complete.
        if (tracker != null && !string.IsNullOrWhiteSpace(surviveWavesObjectiveId))
            tracker.CompleteObjective(surviveWavesObjectiveId);
    }

    // ---------------------------------------------------------------- wiring checks

    // Checked when the waves actually start rather than in Start, because an
    // objective only gets its Required Count when the tracker activates it, and that
    // happens as the player works through the objectives leading up to this one.
    private void CheckObjectiveWiring()
    {
        if (string.IsNullOrWhiteSpace(surviveWavesObjectiveId)) return;

        if (tracker == null)
        {
            Debug.LogWarning($"[WaveSpawner] '{name}' reports waves to objective \"{surviveWavesObjectiveId}\" but found no QuestTracker in the scene, so no wave will ever count. Add a QuestTracker, drag it into the Tracker field, or clear the Objective Id if these waves aren't part of a quest.", this);
            return;
        }

        QuestTracker.ObjectiveProgress objective = FindObjectiveProgress(surviveWavesObjectiveId);
        if (objective == null)
        {
            Debug.LogWarning($"[WaveSpawner] '{name}' reports waves to objective \"{surviveWavesObjectiveId}\", but no running quest has an objective with that id. Check the Id on the objective asset, and that its quest has actually started.", this);
            return;
        }

        if (objective.state != ObjectiveState.Active)
        {
            Debug.LogWarning($"[WaveSpawner] '{name}' started its waves while objective \"{surviveWavesObjectiveId}\" is {objective.state}, not Active. Progress only counts on an active objective, so the wave counter won't move. Make this the quest's current step before the player can reach the room.", this);
            return;
        }

        if (objective.required != waves.Count)
            Debug.LogWarning($"[WaveSpawner] '{name}' sends {waves.Count} waves but objective \"{surviveWavesObjectiveId}\" needs {objective.required}. Set that objective's Required Count to {waves.Count}, or the door opens at the wrong time.", this);
    }

    // Walks the tracker's live progress to find one objective by id. Read-only, and
    // only used for the warnings above.
    private QuestTracker.ObjectiveProgress FindObjectiveProgress(string objectiveId)
    {
        IReadOnlyList<QuestTracker.QuestProgress> quests = tracker.Quests;
        for (int q = 0; q < quests.Count; q++)
        {
            List<QuestTracker.ObjectiveProgress> objectives = quests[q].objectives;
            for (int o = 0; o < objectives.Count; o++)
            {
                QuestTracker.ObjectiveProgress objective = objectives[o];
                if (objective.definition != null && objective.definition.id == objectiveId)
                    return objective;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- pool

    private EnemyFollowPlayer CreateEnemy()
    {
        // Spawn on a real point rather than the origin: a NavMeshAgent that wakes up
        // off the navmesh logs a warning and can't path. This deliberately does not
        // advance the cursor — OnGetFromPool runs straight after and does the real
        // placement.
        Transform first = spawnPoints[0];
        EnemyFollowPlayer enemy = Instantiate(enemyPrefab, first.position, first.rotation);
        enemy.SetPool(enemyPool);
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

    // The spawn points are empty objects with no renderer, so draw them.
    void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;

        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.8f);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null) continue;
            Gizmos.DrawWireSphere(spawnPoints[i].position, 0.5f);
            Gizmos.DrawLine(transform.position, spawnPoints[i].position);
        }
    }
}
