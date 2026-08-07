using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class Settings : MonoBehaviour
{
    [SerializeField] private GameObject settingspanel;
    //[SerializeField] private CanvasGroup settingsPanel;

    private void Awake()
    {
        //settingsPanel.alpha = 0f;
        if(settingspanel != null)
        {
            settingspanel.SetActive(false);
        }
    }

    public void OpenSettings()
    {
        if (settingspanel != null)
        {
            settingspanel.SetActive(true);
        }
    }

    public void CloseSettings()
    {
        if (settingspanel != null)
        {
            settingspanel.SetActive(false);
        }
    }

    public void ToggleSettings()
    {
        if (settingspanel != null)
        {
            settingspanel.SetActive(!settingspanel.activeSelf);
        }
    }
    
}
