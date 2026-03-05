using UnityEngine;
using UnityEngine.UI;

public class HP : MonoBehaviour
{
    [Header("HP Settings")]
    public float maxHp = 1000f;

    [Header("Drain Settings")]
    [Tooltip("몇 초마다 HP가 닳는지 (틱 간격)")]
    public float drainInterval = 1f;
    [Tooltip("틱당 감소할 HP 양")]
    public float drainAmount = 10f;

    [Header("UI")]
    [Tooltip("HP 바 Image (Image Type: Filled, Fill Method: Horizontal)")]
    public Image hpBarImage;
    [Tooltip("HP 바 보간 속도 (클수록 빠름)")]
    public float barLerpSpeed = 5f;

    private float currentHp;
    private float targetFill;
    private float drainTimer;

    public float CurrentHp  => currentHp;
    public float Ratio      => currentHp / maxHp;

    void Start()
    {
        currentHp  = maxHp;
        targetFill = 1f;
        drainTimer = 0f;

        if (hpBarImage != null)
            hpBarImage.fillAmount = 1f;
    }

    void Update()
    {
        // 틱 감소
        drainTimer += Time.deltaTime;
        if (drainTimer >= drainInterval)
        {
            drainTimer -= drainInterval;
            ApplyDamage(drainAmount);
        }

        // HP 바 부드러운 보간
        if (hpBarImage != null)
            hpBarImage.fillAmount = Mathf.Lerp(hpBarImage.fillAmount, targetFill, Time.deltaTime * barLerpSpeed);
    }

    // 외부(장애물 등)에서 HP 감소 호출
    public void TakeDamage(float amount)
    {
        ApplyDamage(amount);
    }

    public void Heal(float amount)
    {
        currentHp  = Mathf.Min(maxHp, currentHp + amount);
        targetFill = currentHp / maxHp;
    }

    void ApplyDamage(float amount)
    {
        currentHp  = Mathf.Max(0f, currentHp - amount);
        targetFill = currentHp / maxHp;

        if (currentHp <= 0f)
            OnDead();
    }

    void OnDead()
    {
        Debug.Log("[HP] 사망");
    }
}
