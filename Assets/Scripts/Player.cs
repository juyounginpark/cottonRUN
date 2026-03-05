using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : MonoBehaviour
{
    [Header("Visuals")]
    public GameObject runPrefabObj;
    public GameObject slidePrefabObj;

    [Header("Jump")]
    public float jumpForce = 10f;

    [Header("Hit Settings")]
    public HP hpComponent;
    public float obstacleDamage = 100f;
    [Tooltip("무적 지속 시간 (초)")]
    public float invincibleDuration = 2f;
    [Tooltip("깜빡임 간격 (초)")]
    public float blinkInterval = 0.12f;

    [Header("Hit Effects")]
    [Tooltip("전체 화면을 덮는 붉은 UI 패널 프리팹")]
    public GameObject screenFlashPrefab;
    [Tooltip("붉은 화면 최대 알파 (0~1)")]
    public float flashMaxAlpha = 0.45f;
    [Tooltip("붉은 화면 페이드인+아웃 총 시간 (초)")]
    public float flashDuration = 0.35f;
    [Tooltip("카메라 흔들림 지속 시간 (초)")]
    public float shakeDuration = 0.4f;
    [Tooltip("카메라 흔들림 강도")]
    public float shakeMagnitude = 0.18f;

    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isSliding;
    private int  jumpsRemaining;
    private bool isInvincible;

    // 깜빡임용 렌더러 목록
    private SpriteRenderer[] spriteRenderers;

    // 런타임에 인스턴스화된 플래시 Image
    private Image screenFlashImage;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
        SetSlide(false);

        // 스크린 플래시 프리팹 인스턴스화 → Canvas 하위에 배치
        if (screenFlashPrefab != null)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            Transform parent = canvas != null ? canvas.transform : null;

            GameObject instance = Instantiate(screenFlashPrefab, parent);
            instance.transform.SetAsLastSibling(); // 최상단 렌더링

            screenFlashImage = instance.GetComponent<Image>();
            if (screenFlashImage == null)
                screenFlashImage = instance.GetComponentInChildren<Image>();

            if (screenFlashImage != null)
            {
                screenFlashImage.color = new Color(1f, 0f, 0f, 0f);
                screenFlashImage.raycastTarget = false;
            }
        }
    }

    // ── 입력 ──────────────────────────────────────────

    void Update()
    {
        HandleJump();
        HandleSlide();
    }

    void HandleJump()
    {
        if (Input.GetKeyDown(KeyCode.Space) && jumpsRemaining > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpsRemaining--;
            isGrounded = false;
        }
    }

    void HandleSlide()
    {
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (shiftHeld && !isSliding)
            SetSlide(true);
        else if (!shiftHeld && isSliding)
            SetSlide(false);
    }

    void SetSlide(bool sliding)
    {
        isSliding = sliding;
        if (runPrefabObj != null)   runPrefabObj.SetActive(!sliding);
        if (slidePrefabObj != null) slidePrefabObj.SetActive(sliding);
    }

    // ── 충돌 ──────────────────────────────────────────

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
        {
            foreach (ContactPoint2D contact in col.contacts)
            {
                if (contact.normal.y > 0.5f)
                {
                    isGrounded     = true;
                    jumpsRemaining = 2;
                    break;
                }
            }
        }

        if (col.gameObject.CompareTag("Obstacle"))
            HandleObstacleHit();
    }

    void OnCollisionExit2D(Collision2D col)
    {
        if (col.gameObject.CompareTag("Ground"))
            isGrounded = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Jelly"))
            Destroy(other.gameObject);

        if (other.CompareTag("Obstacle"))
            HandleObstacleHit();
    }

    // ── 피격 처리 ────────────────────────────────────

    void HandleObstacleHit()
    {
        if (isInvincible) return;

        hpComponent?.TakeDamage(obstacleDamage);

        StartCoroutine(InvincibilityRoutine());
        StartCoroutine(ScreenFlashRoutine());
        StartCoroutine(CameraShakeRoutine());
    }

    // 무적 + 깜빡임 (알파 70% ↔ 50%)
    IEnumerator InvincibilityRoutine()
    {
        isInvincible = true;
        float elapsed = 0f;
        bool  bright  = true;

        while (elapsed < invincibleDuration)
        {
            SetRenderersAlpha(bright ? 0.7f : 0.5f);
            bright = !bright;
            yield return new WaitForSeconds(blinkInterval);
            elapsed += blinkInterval;
        }

        SetRenderersAlpha(1f);
        isInvincible = false;
    }

    void SetRenderersAlpha(float alpha)
    {
        foreach (var sr in spriteRenderers)
        {
            if (sr == null) continue;
            Color c = sr.color;
            c.a     = alpha;
            sr.color = c;
        }
    }

    // 붉은 화면 페이드인 → 페이드아웃
    IEnumerator ScreenFlashRoutine()
    {
        if (screenFlashImage == null) yield break;

        float half = flashDuration * 0.5f;

        // 페이드인
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float a = Mathf.Lerp(0f, flashMaxAlpha, t / half);
            screenFlashImage.color = new Color(1f, 0f, 0f, a);
            yield return null;
        }

        // 페이드아웃
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float a = Mathf.Lerp(flashMaxAlpha, 0f, t / half);
            screenFlashImage.color = new Color(1f, 0f, 0f, a);
            yield return null;
        }

        screenFlashImage.color = new Color(1f, 0f, 0f, 0f);
    }

    // 카메라 흔들림
    IEnumerator CameraShakeRoutine()
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 origin  = cam.transform.localPosition;
        float   elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            float progress = elapsed / shakeDuration;
            float strength = shakeMagnitude * (1f - progress); // 시간이 지날수록 약해짐

            cam.transform.localPosition = origin + (Vector3)Random.insideUnitCircle * strength;

            elapsed += Time.deltaTime;
            yield return null;
        }

        cam.transform.localPosition = origin;
    }
}
