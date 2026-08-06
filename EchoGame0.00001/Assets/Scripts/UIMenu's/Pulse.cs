using UnityEngine;
using UnityEngine.UI;

public class Pulse : MonoBehaviour
{
    [SerializeField] private CanvasGroup text;
    [SerializeField] private float minAlpha = 0.2f;
    [SerializeField] private float maxAlpha = 1f;
    [SerializeField] private float pulseSpeed = 2f;
    
    void Update()
    {
        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
        text.alpha = alpha;
    }
}
