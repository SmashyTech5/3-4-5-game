using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Audio Sources")]
    public AudioSource musicSource;   // background music
    public AudioSource sfxSource;     // sound effects

    [Header("Music Clips")]
    public AudioClip mainMenuMusic;
    public AudioClip gameplayMusic;

    [Header("SFX Clips")]
    public AudioClip beadDropSfx;
    public AudioClip winSfx;
    public AudioClip loseSfx;
    public AudioClip tieSfx;
    public AudioClip buttonClickSfx;
    public AudioClip scoreSfx;   // 🔊 New for match/score
    private void Awake()
    {
        // Singleton pattern (one AudioManager across scenes)
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // keep it when changing scenes
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // -------------------- MUSIC --------------------
    public void PlayMainMenuMusic()
    {
        PlayMusic(mainMenuMusic, true);
    }
    public void PlayScore()
    {
        PlaySfx(scoreSfx);
    }

    public void PlayGameplayMusic()
    {
        PlayMusic(gameplayMusic, true);
    }

    private void PlayMusic(AudioClip clip, bool loop)
    {
        if (musicSource == null || clip == null) return;

        musicSource.clip = clip;
        musicSource.loop = loop;
        musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
    }

    // -------------------- SFX --------------------
    public void PlayBeadDrop()
    {
        PlaySfx(beadDropSfx);
    }

    public void PlayWin()
    {
        PlaySfx(winSfx);
    }

    public void PlayLose()
    {
        PlaySfx(loseSfx);
    }

    public void PlayTie()
    {
        PlaySfx(tieSfx);
    }

    public void PlayButtonClick()
    {
        PlaySfx(buttonClickSfx);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (sfxSource != null && clip != null)
        {
            sfxSource.PlayOneShot(clip);
        }
    }
}
