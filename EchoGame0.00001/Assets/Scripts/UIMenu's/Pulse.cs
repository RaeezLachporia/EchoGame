using UnityEngine;
using UnityEngine.UI;

public class Pulse : MonoBehaviour
{
    [SerializeField] private CanvasGroup text;
    [SerializeField] private float minAlpha = 0.2f;
    [SerializeField] private float maxAlpha = 1f;
    [SerializeField] private float pulseSpeed = 2f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f;
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
        text.alpha = alpha;
    }
}
