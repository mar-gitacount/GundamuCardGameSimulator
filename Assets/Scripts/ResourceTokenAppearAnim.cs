using UnityEngine;

/// <summary>リソース／EX トークン追加時の出現アニメ（スケールイン）。</summary>
public sealed class ResourceTokenAppearAnim : MonoBehaviour
{
    private float _delay;
    private float _elapsed = -1f;
    private const float Duration = 0.22f;

    public void Play(float delaySeconds = 0f)
    {
        _delay = Mathf.Max(0f, delaySeconds);
        _elapsed = -1f;
        transform.localScale = Vector3.zero;
        enabled = true;
    }

    private void Update()
    {
        if (_elapsed < 0f)
        {
            _delay -= Time.unscaledDeltaTime;
            if (_delay > 0f)
            {
                return;
            }

            _elapsed = 0f;
        }

        _elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_elapsed / Duration);
        float s = Mathf.SmoothStep(0f, 1f, t);
        transform.localScale = new Vector3(s, s, 1f);
        if (t >= 1f)
        {
            transform.localScale = Vector3.one;
            Destroy(this);
        }
    }
}
