using UnityEngine;

namespace DigiPhant
{
    /// <summary>
    /// Plays presentation audio from the final locomotion state. It deliberately
    /// observes the locomotion component instead of changing tracking input.
    /// </summary>
    public class KatachiAudioFeedback : MonoBehaviour
    {
        [Header("Scene references")]
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public Transform elephantTravel;
        public Transform finishMarker;

        [Header("Clips")]
        public AudioClip drinkClip;
        public AudioClip turnClip;
        public AudioClip walkClip;
        public AudioClip finishClip;

        [Header("Audio sources")]
        public AudioSource walkingSource;
        public AudioSource eventSource;

        [Header("Finish gate")]
        [Min(.1f)] public float finishGateHalfWidth = 2.8f;
        [Min(.01f)] public float turnCooldown = .25f;
        [Range(.01f, .25f)] public float walkFadeSeconds = .06f;

        Vector3 previousTravelPosition;
        bool hasPreviousPosition;
        bool wasDrinking;
        bool hasFinished;
        int previousTurn;
        int finishApproachSide;
        float nextTurnSound;

        void Awake()
        {
            if (controller == null) controller = GetComponent<DigiPhantController>();
            if (locomotion == null) locomotion = GetComponent<DigiPhantLocomotion>();
            if (elephantTravel == null && locomotion != null) elephantTravel = locomotion.travelRoot;

            EnsureSources();
            ConfigureSources();
            ResetTracking();

            if (AudioListener.volume <= 0)
                Debug.LogWarning("Katachi audio feedback is active, but AudioListener.volume is zero.", this);
            if (FindObjectsByType<AudioListener>().Length == 0)
                Debug.LogError("Katachi audio feedback needs an active AudioListener in the scene.", this);
            if (drinkClip == null || turnClip == null || walkClip == null || finishClip == null)
                Debug.LogError("Katachi audio feedback has one or more unassigned AudioClips.", this);
        }

        void OnEnable()
        {
            ResetTracking();
        }

        void OnDisable()
        {
            if (walkingSource != null) walkingSource.Stop();
        }

        [ContextMenu("Test Audio Feedback (Drink Clip)")]
        void TestAudioFeedback()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Enter Play mode before testing Katachi audio feedback.", this);
                return;
            }
            EnsureSources();
            ConfigureSources();
            if (eventSource == null || drinkClip == null)
            {
                Debug.LogError("Audio test failed: event AudioSource or Drink AudioClip is missing.", this);
                return;
            }
            eventSource.Stop();
            eventSource.PlayOneShot(drinkClip);
            Debug.Log("Katachi audio test: playing Drink clip.", this);
        }

        void OnValidate()
        {
            finishGateHalfWidth = Mathf.Max(.1f, finishGateHalfWidth);
            turnCooldown = Mathf.Max(.01f, turnCooldown);
            walkFadeSeconds = Mathf.Clamp(walkFadeSeconds, .01f, .25f);
        }

        void LateUpdate()
        {
            if (locomotion == null || elephantTravel == null) return;

            bool drinking = locomotion.IsDrinkActive;
            bool crossedFinish = DetectFinishCrossing();
            if (!hasFinished && crossedFinish)
            {
                hasFinished = true;
                StopWalkingImmediately();
                if (eventSource != null)
                {
                    eventSource.Stop();
                    if (finishClip != null) eventSource.PlayOneShot(finishClip);
                }
            }

            if (!hasFinished && drinking && !wasDrinking)
            {
                StopWalkingImmediately();
                if (eventSource != null)
                {
                    eventSource.Stop();
                    if (drinkClip != null) eventSource.PlayOneShot(drinkClip);
                }
            }

            int turn = TurnDirection(locomotion.CurrentAction);
            if (!hasFinished && !drinking && turn != 0 && turn != previousTurn && Time.unscaledTime >= nextTurnSound)
            {
                if (eventSource != null && turnClip != null)
                {
                    eventSource.Stop();
                    eventSource.PlayOneShot(turnClip);
                }
                nextTurnSound = Time.unscaledTime + turnCooldown;
            }

            bool moved = hasPreviousPosition && (elephantTravel.position - previousTravelPosition).sqrMagnitude > .00000001f;
            UpdateWalking(!hasFinished && !drinking && moved);
            previousTravelPosition = elephantTravel.position;
            hasPreviousPosition = true;
            wasDrinking = drinking;
            previousTurn = turn;
        }

        void EnsureSources()
        {
            var sources = GetComponents<AudioSource>();
            if (walkingSource == null)
                walkingSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
            if (eventSource == null || eventSource == walkingSource)
            {
                for (int i = 0; i < sources.Length; i++)
                    if (sources[i] != walkingSource) { eventSource = sources[i]; break; }
                if (eventSource == null || eventSource == walkingSource)
                    eventSource = gameObject.AddComponent<AudioSource>();
            }
        }

        void ConfigureSources()
        {
            if (walkingSource != null)
            {
                walkingSource.clip = walkClip;
                walkingSource.loop = true;
                walkingSource.playOnAwake = false;
                walkingSource.spatialBlend = 0;
                walkingSource.dopplerLevel = 0;
                walkingSource.volume = 0;
            }
            if (eventSource != null)
            {
                eventSource.playOnAwake = false;
                eventSource.spatialBlend = 0;
                eventSource.dopplerLevel = 0;
            }
        }

        void ResetTracking()
        {
            hasPreviousPosition = elephantTravel != null;
            if (hasPreviousPosition) previousTravelPosition = elephantTravel.position;
            wasDrinking = locomotion != null && locomotion.IsDrinkActive;
            previousTurn = 0;
            nextTurnSound = 0;
            hasFinished = false;
            if (finishMarker != null && elephantTravel != null)
            {
                float localZ = finishMarker.InverseTransformPoint(elephantTravel.position).z;
                finishApproachSide = localZ >= 0 ? 1 : -1;
            }
        }

        bool DetectFinishCrossing()
        {
            if (!hasPreviousPosition || finishMarker == null || hasFinished) return false;
            Vector3 previous = finishMarker.InverseTransformPoint(previousTravelPosition);
            Vector3 current = finishMarker.InverseTransformPoint(elephantTravel.position);
            if (finishApproachSide == 0 || previous.z * finishApproachSide < 0 || current.z * finishApproachSide > 0)
                return false;
            float distance = previous.z - current.z;
            if (Mathf.Abs(distance) < .00001f) return false;
            float crossing = previous.z / distance;
            if (crossing < 0 || crossing > 1) return false;
            float lateral = Mathf.Lerp(previous.x, current.x, crossing);
            return Mathf.Abs(lateral) <= finishGateHalfWidth;
        }

        void UpdateWalking(bool shouldWalk)
        {
            if (walkingSource == null || walkClip == null) return;
            if (shouldWalk && !walkingSource.isPlaying)
            {
                walkingSource.clip = walkClip;
                walkingSource.volume = 0;
                walkingSource.Play();
            }
            float targetVolume = shouldWalk ? 1 : 0;
            walkingSource.volume = Mathf.MoveTowards(walkingSource.volume, targetVolume,
                Time.unscaledDeltaTime / walkFadeSeconds);
            if (!shouldWalk && walkingSource.isPlaying && walkingSource.volume <= 0)
                walkingSource.Stop();
        }

        void StopWalkingImmediately()
        {
            if (walkingSource == null) return;
            walkingSource.Stop();
            walkingSource.volume = 0;
        }

        static int TurnDirection(string action)
        {
            if (action == "Turn left" || action == "Walk left" || action == "Run left") return -1;
            if (action == "Turn right" || action == "Walk right" || action == "Run right") return 1;
            return 0;
        }
    }
}
