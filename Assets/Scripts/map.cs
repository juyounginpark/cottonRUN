using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class Map : MonoBehaviour
{
    [Header("Tile Settings")]
    public GameObject tilePrefab;
    public float scrollSpeed = 5f;
    public float fixedY = 0f;

    [Header("Spawn Buffer")]
    [Tooltip("화면 오른쪽 바깥에 미리 대기할 타일 수")]
    public int spawnBuffer = 2;

    [Header("Gap Settings")]
    [Range(0f, 1f)]
    public float gapChance = 0.15f;
    public int minGapSize = 1;
    public int maxGapSize = 3;

    [Header("Jelly Settings")]
    public GameObject jellyPrefab;
    [Tooltip("젤리 간격 (작을수록 촘촘)")]
    public float jellySpacing = 0.5f;
    [Tooltip("전체 젤리 Y 기준 오프셋 (타일 상단에서 이 값만큼 위)")]
    public float jellyBaseOffsetY = 0.5f;
    [Tooltip("구멍 위 젤리 호 최고 높이 (jellyBaseOffsetY 위로 추가)")]
    public float jellyJumpOffsetY = 3.5f;

    [Header("Background Settings")]
    public GameObject backgroundPrefab;
    [Tooltip("배경 고정 Y 위치")]
    public float backgroundY = 0f;
    [Tooltip("배경 스크롤 속도 배율 (1 = 타일과 동일, <1 = 시차 효과)")]
    [Range(0f, 1f)]
    public float backgroundSpeedMultiplier = 0.5f;

    [Header("Obstacle Settings")]
    public ObstacleDatabase obstacleDatabase;
    [Tooltip("장애물 콜라이더 하단 = 타일 상단 기준 추가 오프셋 (0 = 딱 맞춤)")]
    public float obstacleOffsetY = 0f;
    [Range(0f, 1f)]
    [Tooltip("타일 하나당 장애물 생성 확률")]
    public float obstacleChance = 0.2f;
    [Tooltip("장애물 사이 최소 타일 간격 (겹침 방지)")]
    public int obstacleMinGap = 3;
    [Tooltip("장애물 꼭대기 위로 젤리가 얼마나 더 높이 뜰지 (여유 높이)")]
    public float obstacleJellyClearance = 0.5f;

    [Header("Obstacle Chunk Settings")]
    [Tooltip("2연속 장애물 청크 확률 (서로 다른 종류 가능)")]
    [Range(0f, 1f)]
    public float chunkDoubleChance = 0.2f;
    [Tooltip("3연속 장애물 청크 확률 (같은 종류만)")]
    [Range(0f, 1f)]
    public float chunkTripleChance = 0.1f;

    // ── 내부 상태 ──────────────────────────────────────

    private float tileWidth;
    private float tileHeight;
    private float screenLeft;
    private float screenRight;

    // 타일
    private Queue<GameObject> tileQueue = new Queue<GameObject>();
    private float nextSpawnX;
    private int gapTilesLeft;

    // 젤리
    private Queue<GameObject> jellyQueue = new Queue<GameObject>();
    private float nextJellyX;
    private float jellyArcStartX    = float.MaxValue;
    private float jellyArcEndX      = float.MaxValue;
    private float jellyArcPeakOffset;  // groundBase 위로 얼마나 올릴지 (arc마다 다름)

    // 배경
    private Queue<GameObject> bgQueue = new Queue<GameObject>();
    private float nextBgX;
    private float bgWidth;

    // 장애물
    private Queue<GameObject> obstacleQueue = new Queue<GameObject>();
    private int tilesSinceLastObstacle;

    [Header("Obstacle Type Change Settings")]
    [Tooltip("슬라이드↔일반 장애물 전환 시 최소 타일 텀")]
    public int typeChangeTileGap = 2;

    // 장애물 청크
    private int        obstacleChunkLeft   = 0;
    private GameObject obstacleChunkPrefab = null; // null = 혼합, non-null = 고정 종류
    private float      obstacleChunkYOffset = 0f;
    private bool       lastObstacleWasSlide = false;

    // 구멍 ±3타일 보호
    private int scheduledGapIn   = 0;  // 몇 타일 후에 gap 시작 (0 = 예약 없음)
    private int scheduledGapSize = 0;
    private int gapPostCooldown  = 0;  // gap 이후 남은 보호 타일 수

    // ── 생명주기 ──────────────────────────────────────

    void OnEnable()
    {
        if (tilePrefab == null || Camera.main == null) return;
        ClearAll();
        CalculateTileSize();
        CalculateScreenBounds();
        InitializeTiles();
    }

    void OnDisable() => ClearAll();

    void OnValidate()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            if (tilePrefab == null || Camera.main == null) return;
            ClearAll();
            CalculateTileSize();
            CalculateScreenBounds();
            InitializeTiles();
        };
#endif
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        float delta = scrollSpeed * Time.deltaTime;
        float bgDelta = scrollSpeed * backgroundSpeedMultiplier * Time.deltaTime;
        MoveQueue(bgQueue, bgDelta);
        nextBgX -= bgDelta;
        SpawnBgIfNeeded();
        RecycleBg();

        MoveQueue(tileQueue, delta);
        MoveQueue(jellyQueue, delta);
        MoveQueue(obstacleQueue, delta);
        nextSpawnX     -= delta;
        nextJellyX     -= delta;
        jellyArcStartX -= delta;
        jellyArcEndX   -= delta;

        RecycleTiles();
        RecycleJellies();
        RecycleObstacles();
        SpawnTilesIfNeeded();
        SpawnJelliesIfNeeded();
    }

    // ── 초기화 ────────────────────────────────────────

    void ClearAll()
    {
        foreach (var o in tileQueue)     if (o != null) DestroyImmediate(o);
        foreach (var o in jellyQueue)    if (o != null) DestroyImmediate(o);
        foreach (var o in obstacleQueue) if (o != null) DestroyImmediate(o);
        foreach (var o in bgQueue)       if (o != null) DestroyImmediate(o);
        tileQueue.Clear();
        jellyQueue.Clear();
        obstacleQueue.Clear();
        bgQueue.Clear();

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        gapTilesLeft           = 0;
        scheduledGapIn         = 0;
        scheduledGapSize       = 0;
        gapPostCooldown        = 0;
        jellyArcStartX         = float.MaxValue;
        jellyArcEndX           = float.MaxValue;
        tilesSinceLastObstacle = obstacleMinGap;
        obstacleChunkLeft      = 0;
        obstacleChunkPrefab    = null;
        obstacleChunkYOffset   = 0f;
        lastObstacleWasSlide   = false;
    }

    void CalculateTileSize()
    {
        GameObject temp = Instantiate(tilePrefab);
        SpriteRenderer sr = temp.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            tileWidth  = sr.bounds.size.x;
            tileHeight = sr.bounds.size.y;
        }
        else
        {
            Collider2D col = temp.GetComponent<Collider2D>();
            if (col != null)
            {
                tileWidth  = col.bounds.size.x;
                tileHeight = col.bounds.size.y;
            }
            else
            {
                tileWidth  = temp.transform.localScale.x;
                tileHeight = temp.transform.localScale.y;
                Debug.LogWarning("[Map] SpriteRenderer/Collider2D 없음 — localScale 사용");
            }
        }
        DestroyImmediate(temp);
        if (tileWidth <= 0f) tileWidth = 1f;
    }

    void CalculateScreenBounds()
    {
        Camera cam = Camera.main;
        float halfW = cam.orthographicSize * cam.aspect;
        screenLeft  = cam.transform.position.x - halfW;
        screenRight = cam.transform.position.x + halfW;
    }

    void InitializeTiles()
    {
        nextSpawnX = screenLeft + tileWidth * 0.5f;
        float fillRight = screenRight + tileWidth * spawnBuffer;
        while (nextSpawnX <= fillRight)
        {
            SpawnTile(nextSpawnX);
            nextSpawnX += tileWidth;
        }

        nextJellyX     = screenRight + tileWidth;
        jellyArcStartX = float.MaxValue;
        jellyArcEndX   = float.MaxValue;

        InitBackground();
    }

    void InitBackground()
    {
        if (backgroundPrefab == null) return;

        // 배경 크기 측정
        GameObject temp = Instantiate(backgroundPrefab);
        SpriteRenderer sr = temp.GetComponent<SpriteRenderer>();
        bgWidth = sr != null ? sr.bounds.size.x : temp.transform.localScale.x;
        DestroyImmediate(temp);
        if (bgWidth <= 0f) bgWidth = 1f;

        // 화면 꽉 채우기
        nextBgX = screenLeft + bgWidth * 0.5f;
        float fillRight = screenRight + bgWidth;
        while (nextBgX <= fillRight)
        {
            SpawnBg(nextBgX);
            nextBgX += bgWidth;
        }
    }

    // ── 스폰 ──────────────────────────────────────────

    void SpawnBg(float posX)
    {
        var bg = Instantiate(backgroundPrefab, new Vector3(posX, backgroundY, 1f), Quaternion.identity, transform);
        bgQueue.Enqueue(bg);
    }

    void SpawnBgIfNeeded()
    {
        if (backgroundPrefab == null || bgWidth <= 0f) return;
        float fillRight = screenRight + bgWidth;
        while (nextBgX <= fillRight)
        {
            SpawnBg(nextBgX);
            nextBgX += bgWidth;
        }
    }

    void RecycleBg()
    {
        while (bgQueue.Count > 0)
        {
            var front = bgQueue.Peek();
            if (front == null) { bgQueue.Dequeue(); continue; }
            if (front.transform.position.x + bgWidth * 0.5f < screenLeft)
            { bgQueue.Dequeue(); Destroy(front); }
            else break;
        }
    }

    void SpawnTile(float posX, bool active = true)
    {
        var tile = Instantiate(tilePrefab, new Vector3(posX, fixedY, 0f), Quaternion.identity, transform);
        tile.SetActive(active);
        tileQueue.Enqueue(tile);
    }

    void SpawnJelly(float posX, float posY)
    {
        if (jellyPrefab == null) return;
        var jelly = Instantiate(jellyPrefab, new Vector3(posX, posY, 0f), Quaternion.identity, transform);
        jellyQueue.Enqueue(jelly);
    }

    void SpawnObstacle(float posX)
    {
        if (obstacleDatabase == null) return;
        var entry = obstacleDatabase.GetRandomEntry();
        if (entry?.prefab == null || IsTypeChangeTooSoon(entry.yOffset > 0f)) return;
        SpawnObstacleWithPrefab(posX, entry.prefab, entry.yOffset, false);
    }

    // extendArc=true: arc 시작 X 유지, 끝 X와 높이만 확장 (청크 연속 스폰용)
    void SpawnObstacleWithPrefab(float posX, GameObject prefab, float entryYOffset, bool extendArc)
    {
        float tileTop = fixedY + tileHeight * 0.5f;

        // 임시 위치에 생성 후 높이 측정 → 콜라이더 하단을 타일 상단에 정렬
        var obs = Instantiate(prefab, new Vector3(posX, tileTop, 0f), Quaternion.identity, transform);
        float obsHeight = GetObjectHeight(obs);
        float posY      = tileTop + obsHeight * 0.5f + obstacleOffsetY + entryYOffset;
        obs.transform.position = new Vector3(posX, posY, 0f);

        obstacleQueue.Enqueue(obs);
        tilesSinceLastObstacle = 0;

        bool isSlideObstacle   = entryYOffset > 0f;
        lastObstacleWasSlide   = isSlideObstacle;

        if (!isSlideObstacle)
        {
            // 일반 장애물: 장애물 위로 젤리 아크
            float obstacleTop = posY + obsHeight * 0.5f;
            float groundBase  = tileTop + jellyBaseOffsetY;
            float peakOffset  = Mathf.Max(obstacleTop - groundBase + obstacleJellyClearance, 0.3f) * 1.3f;

            if (!extendArc)
            {
                jellyArcStartX     = posX - tileWidth;
                jellyArcPeakOffset = peakOffset;
            }
            else
            {
                jellyArcPeakOffset = Mathf.Max(jellyArcPeakOffset, peakOffset);
            }
            jellyArcEndX = posX + tileWidth;
        }
        // 슬라이드 장애물: 젤리 아크 미설정 → 장애물 구간도 groundBase 높이로 평탄하게 유지
    }

    float GetObjectHeight(GameObject obj)
    {
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null) return sr.bounds.size.y;
        var col = obj.GetComponent<Collider2D>();
        if (col != null) return col.bounds.size.y;
        return obj.transform.localScale.y;
    }

    bool IsTypeChangeTooSoon(bool candidateIsSlide)
        => candidateIsSlide != lastObstacleWasSlide && tilesSinceLastObstacle < typeChangeTileGap;

    // 타일 루프: 젤리보다 1타일 앞서 실행 + 구멍 ±3타일 look-ahead
    void SpawnTilesIfNeeded()
    {
        float fillRight = screenRight + tileWidth * (spawnBuffer + 1);
        while (nextSpawnX <= fillRight)
        {
            if (gapTilesLeft > 0)
            {
                // 비활성 타일 (구멍)
                SpawnTile(nextSpawnX, false);
                gapTilesLeft--;
                tilesSinceLastObstacle++;
                if (gapTilesLeft == 0)
                    gapPostCooldown = 3; // 구멍 이후 3타일 보호
            }
            else
            {
                SpawnTile(nextSpawnX);
                if (gapPostCooldown > 0) gapPostCooldown--;

                if (scheduledGapIn > 1)
                {
                    // 구멍 3타일 전 보호 구간
                    scheduledGapIn--;
                    tilesSinceLastObstacle++;
                }
                else if (scheduledGapIn == 1)
                {
                    // 현재 타일 = 구멍 직전 타일 (pre-gap) → 구멍 시작 예약 실행
                    scheduledGapIn     = 0;
                    gapTilesLeft       = scheduledGapSize;
                    jellyArcStartX     = nextSpawnX;
                    jellyArcEndX       = nextSpawnX + (scheduledGapSize + 1) * tileWidth;
                    jellyArcPeakOffset = jellyJumpOffsetY;
                    tilesSinceLastObstacle++;
                }
                else
                {
                    // 일반 구간: gap 또는 장애물 결정
                    bool nearGap = gapPostCooldown > 0;

                    // gap 근처면 진행 중인 청크 강제 종료
                    if (nearGap) obstacleChunkLeft = 0;

                    if (obstacleChunkLeft > 0)
                    {
                        // 청크 연속 스폰
                        if (obstacleChunkPrefab != null)
                        {
                            SpawnObstacleWithPrefab(nextSpawnX, obstacleChunkPrefab, obstacleChunkYOffset, true);
                        }
                        else
                        {
                            var chunkEntry = obstacleDatabase?.GetRandomEntry();
                            if (chunkEntry?.prefab != null && !IsTypeChangeTooSoon(chunkEntry.yOffset > 0f))
                                SpawnObstacleWithPrefab(nextSpawnX, chunkEntry.prefab, chunkEntry.yOffset, true);
                            else
                                tilesSinceLastObstacle++;
                        }
                        obstacleChunkLeft--;
                    }
                    else if (!nearGap && Random.value < gapChance)
                    {
                        // 구멍을 3타일 뒤로 예약 → 현재 타일 포함 3타일이 pre-gap 보호
                        scheduledGapSize    = Random.Range(minGapSize, maxGapSize + 1);
                        scheduledGapIn      = 3;
                        obstacleChunkLeft   = 0;
                        tilesSinceLastObstacle++;
                    }
                    else
                    {
                        bool canSpawn = !nearGap
                                 && obstacleDatabase != null
                                 && tilesSinceLastObstacle >= obstacleMinGap;
                        if (canSpawn && Random.value < obstacleChance)
                        {
                            float r = Random.value;
                            if (r < chunkTripleChance)
                            {
                                // 같은 종류 청크: entry의 maxConsecutive로 연속 횟수 결정
                                var entry = obstacleDatabase.GetRandomEntry();
                                if (entry?.prefab != null && !IsTypeChangeTooSoon(entry.yOffset > 0f))
                                {
                                    SpawnObstacleWithPrefab(nextSpawnX, entry.prefab, entry.yOffset, false);
                                    obstacleChunkLeft    = Mathf.Max(0, entry.maxConsecutive - 1);
                                    obstacleChunkPrefab  = entry.prefab;
                                    obstacleChunkYOffset = entry.yOffset;
                                }
                                else tilesSinceLastObstacle++;
                            }
                            else if (r < chunkTripleChance + chunkDoubleChance)
                            {
                                // 2연속: 혼합 가능
                                var entry2 = obstacleDatabase.GetRandomEntry();
                                if (entry2?.prefab != null && !IsTypeChangeTooSoon(entry2.yOffset > 0f))
                                {
                                    SpawnObstacleWithPrefab(nextSpawnX, entry2.prefab, entry2.yOffset, false);
                                    obstacleChunkLeft    = 1;
                                    obstacleChunkPrefab  = null;
                                    obstacleChunkYOffset = 0f;
                                }
                                else tilesSinceLastObstacle++;
                            }
                            else
                            {
                                // 단일 장애물
                                SpawnObstacle(nextSpawnX);
                                obstacleChunkLeft = 0;
                            }
                        }
                        else
                            tilesSinceLastObstacle++;
                    }
                }
            }
            nextSpawnX += tileWidth;
        }
    }

    // 젤리 루프: nextJellyX는 항상 jellySpacing씩만 전진
    void SpawnJelliesIfNeeded()
    {
        float fillRight = screenRight + tileWidth * spawnBuffer;
        while (nextJellyX <= fillRight)
        {
            float tileTopY   = fixedY + tileHeight * 0.5f;
            float groundBase = tileTopY + jellyBaseOffsetY;

            float posY;
            bool inArc = nextJellyX >= jellyArcStartX && nextJellyX <= jellyArcEndX;
            if (inArc)
            {
                // 상승(tileWidth) → 평탄 → 하강(tileWidth) 형태
                // 장애물 n연 시 중간이 flat하게 유지됨
                float riseEnd   = jellyArcStartX + tileWidth;
                float fallStart = jellyArcEndX   - tileWidth;

                if (nextJellyX <= riseEnd)
                {
                    float t = tileWidth > 0f ? (nextJellyX - jellyArcStartX) / tileWidth : 1f;
                    posY = groundBase + jellyArcPeakOffset * Mathf.Sin(t * Mathf.PI * 0.5f);
                }
                else if (nextJellyX >= fallStart)
                {
                    float t = tileWidth > 0f ? (nextJellyX - fallStart) / tileWidth : 0f;
                    posY = groundBase + jellyArcPeakOffset * Mathf.Cos(t * Mathf.PI * 0.5f);
                }
                else
                {
                    posY = groundBase + jellyArcPeakOffset;
                }
            }
            else
            {
                posY = groundBase;
            }

            SpawnJelly(nextJellyX, posY);
            nextJellyX += jellySpacing;
        }
    }

    // ── 이동 & 재활용 ─────────────────────────────────

    void MoveQueue(Queue<GameObject> queue, float delta)
    {
        foreach (var obj in queue)
            if (obj != null) obj.transform.position += Vector3.left * delta;
    }

    void RecycleTiles()
    {
        while (tileQueue.Count > 0)
        {
            var front = tileQueue.Peek();
            if (front == null) { tileQueue.Dequeue(); continue; }
            if (front.transform.position.x + tileWidth * 0.5f < screenLeft)
            { tileQueue.Dequeue(); Destroy(front); }
            else break;
        }
    }

    void RecycleJellies()
    {
        while (jellyQueue.Count > 0)
        {
            var front = jellyQueue.Peek();
            if (front == null) { jellyQueue.Dequeue(); continue; }
            if (front.transform.position.x < screenLeft)
            { jellyQueue.Dequeue(); Destroy(front); }
            else break;
        }
    }

    void RecycleObstacles()
    {
        while (obstacleQueue.Count > 0)
        {
            var front = obstacleQueue.Peek();
            if (front == null) { obstacleQueue.Dequeue(); continue; }
            if (front.transform.position.x < screenLeft)
            { obstacleQueue.Dequeue(); Destroy(front); }
            else break;
        }
    }

    // ── 에디터 시각화 ─────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (Camera.main == null) return;
        Camera cam = Camera.main;
        float halfW = cam.orthographicSize * cam.aspect;
        float halfH = cam.orthographicSize;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(
            new Vector3(cam.transform.position.x, fixedY, 0f),
            new Vector3(halfW * 2f, halfH * 2f, 0f));
    }
}
