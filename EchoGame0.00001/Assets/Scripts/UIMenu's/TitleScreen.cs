using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;


public class TitleScreen : MonoBehaviour
{
    /*[SerializeField] private CanvasGroup Sunset;
    [SerializeField] private CanvasGroup Night;
    [SerializeField] private float fadeDuration = 3f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private bool hasTransitioned = false;
    private Coroutine fadeRoutine;

    [Header("MainMenu Fade in")]
    [SerializeField] private MainMenuFade nightToMenuFade;
   
    private void Awake()
    {
        Debug.Log("[TitleScreen] Awake called");

        if (Sunset == null) Debug.LogError("[TitleScreen] Sunset Group is NOT assigned in Inspector!");
        if (Night == null) Debug.LogError("[TitleScreen] Night Group is NOT assigned in Inspector!");
        if (nightToMenuFade == null) Debug.LogError("[TitleScreen] nightToMenuFade is NOT assigned in Inspector!");

        SetGroup(Sunset, 1f);
        SetGroup(Night, 0f);
    }

    private void Update()
    {
        if (hasTransitioned) return;

        bool keyPressed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        if (keyPressed) Debug.Log("[TitleScreen] Keyboard press detected");
        if (mousePressed) Debug.Log("[TitleScreen] Mouse click detected");

        if (keyPressed || mousePressed)
        {
            Debug.Log("[TitleScreen] Triggering crossfade now");
            hasTransitioned = true;
            //StartCrossfade(Sunset, Night);
            StartCrossfade(Sunset, Night, OnReachedNight);
        }
    }

    private void OnReachedNight()
    {
        Debug.Log("[TitleScreen] Reached night, starting transition to Main Menu");

        if (nightToMenuFade != null)
        {
            nightToMenuFade.BeginTransitionToMainMenu();
        }
        else
        {
            Debug.LogError("[TitleScreen] nightToMenuFade is NOT assigned in Inspector!");
        }
    }

    public void CrossfadeToNight() => StartCrossfade(Sunset, Night);
    public void CrossfadeToSunset() => StartCrossfade(Night, Sunset);

    private void StartCrossfade(CanvasGroup from, CanvasGroup to)
    {
        if (fadeRoutine != null) 
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(from, to));
    }

    private IEnumerator Fade(CanvasGroup from, CanvasGroup to)
    {
        to.blocksRaycasts = true;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float eval = fadeCurve.Evaluate(t / fadeDuration);
            to.alpha = eval;
            from.alpha = 1f - eval;
            yield return null;
        }
        SetGroup(to, 1f);
        SetGroup(from, 0f);

        onComplete?.Invoke();
    }

    private void SetGroup(CanvasGroup group, float alpha)
    {
        group.alpha = alpha;
        group.interactable = alpha > 0.99f;
        group.blocksRaycasts = alpha > 0.99f;
    }
}*/




    [SerializeField] private CanvasGroup Sunset;
    [SerializeField] private CanvasGroup Night;
    [SerializeField] private float fadeDuration = 3f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private bool hasTransitioned = false;
    private Coroutine fadeRoutine;

    [Header("MainMenu Fade in")]
    [SerializeField] private MainMenuFade nightToMenuFade;

    private void Awake()
    {
        Debug.Log("[TitleScreen] Awake called");

        if (Sunset == null) Debug.LogError("[TitleScreen] Sunset Group is NOT assigned in Inspector!");
        if (Night == null) Debug.LogError("[TitleScreen] Night Group is NOT assigned in Inspector!");
        if (nightToMenuFade == null) Debug.LogError("[TitleScreen] nightToMenuFade is NOT assigned in Inspector!");

        SetGroup(Sunset, 1f);
        SetGroup(Night, 0f);
    }

    private void Update()
    {
        if (hasTransitioned) return;

        bool keyPressed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        if (keyPressed) Debug.Log("[TitleScreen] Keyboard press detected");
        if (mousePressed) Debug.Log("[TitleScreen] Mouse click detected");

        if (keyPressed || mousePressed)
        {
            Debug.Log("[TitleScreen] Triggering crossfade now");
            hasTransitioned = true;
            StartCrossfade(Sunset, Night, OnReachedNight);
        }
    }

    private void OnReachedNight()
    {
        Debug.Log("[TitleScreen] Reached night, starting transition to Main Menu");

        if (nightToMenuFade != null)
        {
            nightToMenuFade.BeginTransitionToMainMenu();
        }
        else
        {
            Debug.LogError("[TitleScreen] nightToMenuFade is NOT assigned in Inspector!");
        }
    }

    public void CrossfadeToNight() => StartCrossfade(Sunset, Night);
    public void CrossfadeToSunset() => StartCrossfade(Night, Sunset);

    private void StartCrossfade(CanvasGroup from, CanvasGroup to, System.Action onComplete = null)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(from, to, onComplete));
    }

    private IEnumerator Fade(CanvasGroup from, CanvasGroup to, System.Action onComplete = null)
    {
        to.blocksRaycasts = true;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float eval = fadeCurve.Evaluate(t / fadeDuration);
            to.alpha = eval;
            from.alpha = 1f - eval;
            yield return null;
        }
        SetGroup(to, 1f);
        SetGroup(from, 0f);

        onComplete?.Invoke();
    }

    private void SetGroup(CanvasGroup group, float alpha)
    {
        group.alpha = alpha;
        group.interactable = alpha > 0.99f;
        group.blocksRaycasts = alpha > 0.99f;
    }
}
