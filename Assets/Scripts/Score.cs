using TMPro;
using UnityEngine;

public class Score : MonoBehaviour
{
    public static Score Instance { get; private set; }

    [Header("UI")]
    public TMP_Text scoreText;

    private int score;

    void Awake()
    {
        Instance = this;
    }

    public void AddJelly()
    {
        score += 100;
        UpdateText();
    }

    void UpdateText()
    {
        if (scoreText != null)
            scoreText.text = $"Score: {score}";
    }
}
