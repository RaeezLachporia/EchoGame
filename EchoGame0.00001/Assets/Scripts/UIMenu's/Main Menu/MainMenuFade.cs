using UnityEngine;
using System.Collections;


public class MainMenuFade : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private CanvasGroup Night;
    [SerializeField] private CanvasGroup MainMenu;

    [Header("Timing")]
    [SerializeField] private float holdOnNightDuration = 4f;
    [SerializeField] private float fadeDuration = 1.5f;
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Coroutine fadeRoutine;

   /*public void start()
    {
        MainMenu.alpha = 0f;
    }*/
    

    public void BeginTransitionToMainMenu()
    {
        if (fadeRoutine !=null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(TransitionSequence());
    }

    private IEnumerator TransitionSequence()
    {
        yield return new WaitForSeconds(holdOnNightDuration);

        MainMenu.blocksRaycasts = true;

        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float eval = fadeCurve.Evaluate(t / fadeDuration);

            Night.alpha = 1f - eval;
            MainMenu.alpha = eval;

            yield return null;
        }

        Night.alpha = 0f;
        Night.interactable = false;
        Night.blocksRaycasts = false;

        MainMenu.alpha = 1f;
        MainMenu.interactable = true;
        MainMenu.blocksRaycasts = true;
    }
   
}
