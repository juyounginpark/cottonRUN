using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ObstacleDatabase", menuName = "Game/Obstacle Database")]
public class ObstacleDatabase : ScriptableObject
{
    [System.Serializable]
    public class ObstacleEntry
    {
        public GameObject prefab;
        [Tooltip("출현 가중치 (높을수록 자주 등장)")]
        public float weight = 1f;
        [Tooltip("같은 종류 청크에서 최대 연속 등장 횟수 (1 = 연속 없음)")]
        public int maxConsecutive = 3;
        [Tooltip("전역 obstacleOffsetY에 추가되는 개체별 Y 오프셋")]
        public float yOffset = 0f;
    }

    public List<ObstacleEntry> obstacles = new List<ObstacleEntry>();

    // 가중치 기반 랜덤 엔트리 선택
    public ObstacleEntry GetRandomEntry()
    {
        if (obstacles == null || obstacles.Count == 0) return null;

        float total = 0f;
        foreach (var e in obstacles)
            if (e.prefab != null) total += e.weight;

        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        foreach (var e in obstacles)
        {
            if (e.prefab == null) continue;
            cumulative += e.weight;
            if (roll <= cumulative)
                return e;
        }

        return obstacles[obstacles.Count - 1];
    }

    public GameObject GetRandom() => GetRandomEntry()?.prefab;
}
