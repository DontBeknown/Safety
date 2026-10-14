using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        private readonly List<AudioSource> activePlayers = new List<AudioSource>();
        private ObjectPool<AudioSource> players;
        private bool paused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }

            Instance = this;
            players = new ObjectPool<AudioSource>(CreatePlayer, TakePlayer, ReturnPlayer,
                DestroyPlayer, true, 8, 256);

            // Hold every prewarmed player before releasing, so eight distinct sources are created.
            var prewarmed = new AudioSource[8];
            for (var i = 0; i < prewarmed.Length; i++) prewarmed[i] = players.Get();
            foreach (var source in prewarmed) players.Release(source);
        }

        public void Play(AudioClip clip, float volume = 1f, bool randomPitch = false)
        {
            PlayInternal(clip, transform.position, false, volume, randomPitch);
        }

        public void PlayAtPosition(AudioClip clip, Vector3 position, float volume = 1f, bool randomPitch = false)
        {
            PlayInternal(clip, position, true, volume, randomPitch);
        }

        private void PlayInternal(AudioClip clip, Vector3 position, bool spatial, float volume, bool randomPitch)
        {
            if (clip == null || !isActiveAndEnabled || players == null) return;

            var source = players.Get();
            source.transform.position = position;
            source.clip = clip;
            source.volume = float.IsNaN(volume) ? 0f : Mathf.Clamp01(volume);
            source.pitch = randomPitch ? Random.Range(0.9f, 1.1f) : 1f;
            source.spatialBlend = spatial ? 1f : 0f;
            activePlayers.Add(source);
            source.Play();
            if (paused) source.Pause();
        }

        private AudioSource CreatePlayer()
        {
            var player = new GameObject("Pooled Sound Player");
            player.SetActive(false);
            player.transform.SetParent(transform, false);
            var source = player.AddComponent<AudioSource>();
            ReturnPlayer(source);
            return source;
        }

        private void TakePlayer(AudioSource source)
        {
            source.gameObject.SetActive(true);
        }

        private void ReturnPlayer(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.playOnAwake = false;
            source.loop = false;
            source.mute = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.panStereo = 0f;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.spread = 0f;
            source.priority = 128;
            source.outputAudioMixerGroup = null;
            source.ignoreListenerPause = false;
            source.ignoreListenerVolume = false;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.bypassReverbZones = false;
            source.reverbZoneMix = 1f;
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.gameObject.SetActive(false);
        }

        private void DestroyPlayer(AudioSource source)
        {
            if (source != null) Destroy(source.gameObject);
        }

        private void Update()
        {
            if (paused || AudioListener.pause) return;

            for (var i = activePlayers.Count - 1; i >= 0; i--)
            {
                var source = activePlayers[i];
                if (source.isPlaying) continue;
                activePlayers.RemoveAt(i);
                players.Release(source);
            }
        }

        public void PauseAll(bool value)
        {
            if (paused == value) return;
            paused = value;
            foreach (var source in activePlayers)
            {
                if (paused) source.Pause();
                else source.UnPause();
            }
        }

        public void StopAll()
        {
            for (var i = activePlayers.Count - 1; i >= 0; i--)
                players.Release(activePlayers[i]);
            activePlayers.Clear();
            paused = false;
        }

        private void OnDisable()
        {
            StopAll();
        }

        private void OnDestroy()
        {
            StopAll();
            players?.Clear();
            if (Instance == this) Instance = null;
        }
    }
}
