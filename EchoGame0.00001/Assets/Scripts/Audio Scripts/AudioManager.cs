
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Sound
{
    public string name;
    public AudioClip clip;

    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.1f, 3f)] public float pitch = 1f;
    public bool loop = false;
    public SoundType type = SoundType.SFX;

    [HideInInspector] public AudioSource source;
}

public enum SoundType { SFX, Music  };


[DisallowMultipleComponent]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Sounds")]
    [SerializeField] private List<Sound> sounds = new List<Sound>();

    [Header("Volume")]
    [Range(0f, 1f)][SerializeField] private float masterVolume = 1f;
    [Range(0f, 1f)][SerializeField] private float musicVolume = 1f;
    [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;

    [Header("Persistence")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    private Dictionary<string, Sound> soundLookup;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return; 
        }

        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        soundLookup = new Dictionary<string, Sound>(); 

        foreach (var s in sounds)
        {
            if (string.IsNullOrEmpty(s.name))
            {
                Debug.LogWarning("Audio Manager: a Sound entry has no name and will be skiped");

                continue;
            }

            

            if (soundLookup.ContainsKey(s.name))
            {
                Debug.LogWarning($"AudipManager: duplicate sound name '{s.name}', skipping duplicate.");

                continue;
            }
            

            s.source = gameObject.AddComponent<AudioSource>();
            s.source.clip = s.clip;
            s.source.loop = s.loop;
            s.source.playOnAwake = false;
            s.source.pitch = s.pitch; 

            soundLookup.Add(s.name, s);


           
        }

        ApplyAllVolumes();
    }

    public void Play(string name)
    {
        if (!TryGetSound(name, out Sound s)) return;
        ApplyVolume(s);
        s.source.Play();
    }

    public void PlayIfNotPlaying(string name)
    {
        if (!TryGetSound(name,out Sound s)) return;
        if (s.source.isPlaying) return;
        ApplyVolume(s);
        s.source.Play();
    }

    public void Stop(string name)
    {
        if (!TryGetSound(name, out Sound s)) return;
        s.source.Stop();
    }

    public bool IsPlaying(string name)
    {
        return TryGetSound( name, out Sound s) && s.source.isPlaying; 
    }

    private bool TryGetSound(string name, out Sound sound)
    {
        if ( soundLookup != null && soundLookup.TryGetValue(name, out sound)) return true;

        Debug.LogWarning($"AudioManager: sound '{name}' not found.");
        sound = null;
        return false; 
    }

    // Volume 

    public void SetMasterVolume (float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyAllVolumes();
    }

    public void SetMusicVolume (float value)
    {
        musicVolume = Mathf.Clamp01(value);
        ApplyAllVolumes();
    }

    public void SetSFXVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        ApplyAllVolumes();
    }

    private void ApplyAllVolumes()
    {
        if (soundLookup == null) return;
        foreach (var s in soundLookup.Values) ApplyVolume(s);
    }

    private void ApplyVolume(Sound s)
    {
        float typeVolume = s.type == SoundType.Music ? musicVolume : sfxVolume;
        s.source.volume = s.volume * typeVolume * masterVolume; 
    }

    private void OnValidate()
    {
        if (soundLookup == null) return;
        foreach (var s in sounds)
        {
            if (s.source == null) continue;
            s.source.pitch = s.pitch;
            s.source.loop = s.loop;
            ApplyVolume(s);
        }
    }
}
