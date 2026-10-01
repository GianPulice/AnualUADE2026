using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace PROTOFACTOR
{
    /// <summary>
    /// Plays all animations from the attached Animator Controller one by one in alphabetical order.
    /// Supports filtering out root motion variations (_RM suffix) and resetting position after RM animations.
    /// </summary>
    public class AnimationCyclePlayer : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("If true, animations ending with '_RM' (root motion variations) will be included in the cycle.")]
        public bool playRootMotionVariations = false;

        [Header("Reset Settings")]
        [Tooltip("If true, resets the character's position and rotation to the original scene start values before playing the next animation after a _RM animation.")]
        public bool resetPositionAfterRootMotion = true;

        private Animator animator;
        private List<string> animationNames = new List<string>();
        private int currentIndex = 0;

        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private bool lastPlayedWasRootMotion = false;
        private bool previousPlayRootMotionVariations;

        private Coroutine playCoroutine;

        void Start()
        {
            animator = GetComponent<Animator>();

            if (animator == null)
            {
                Debug.LogError("[PROTOFACTOR] No Animator component found on this GameObject!");
                enabled = false;
                return;
            }

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                Debug.LogError("[PROTOFACTOR] No Animator Controller attached to the Animator!");
                enabled = false;
                return;
            }

            // Store original transform for reset functionality
            originalPosition = transform.position;
            originalRotation = transform.rotation;

            previousPlayRootMotionVariations = playRootMotionVariations;

            BuildAnimationList();

            if (animationNames.Count == 0)
            {
                Debug.LogWarning("[PROTOFACTOR] No valid animations found after filtering.");
                return;
            }

            Debug.Log($"[PROTOFACTOR] AnimationCyclePlayer started. Found {animationNames.Count} animations. Root Motion Variations: {(playRootMotionVariations ? "INCLUDED" : "EXCLUDED")}");

            playCoroutine = StartCoroutine(PlayAnimationsCycle());
        }

        void Update()
        {
            // Check if the toggle changed at runtime (Inspector or code)
            if (playRootMotionVariations != previousPlayRootMotionVariations)
            {
                previousPlayRootMotionVariations = playRootMotionVariations;

                Debug.Log($"[PROTOFACTOR] playRootMotionVariations changed to: {playRootMotionVariations}");

                BuildAnimationList();

                // Restart coroutine to use the new list immediately
                if (playCoroutine != null)
                {
                    StopCoroutine(playCoroutine);
                }
                playCoroutine = StartCoroutine(PlayAnimationsCycle());
            }
        }

        private void BuildAnimationList()
        {
            animationNames.Clear();

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null) return;

            AnimationClip[] clips = controller.animationClips;

            HashSet<string> uniqueNames = new HashSet<string>();

            foreach (AnimationClip clip in clips)
            {
                if (string.IsNullOrEmpty(clip.name)) continue;

                bool isRootMotionVariation = clip.name.EndsWith("_RM");

                // Filter based on toggle
                if (isRootMotionVariation && !playRootMotionVariations)
                    continue;

                uniqueNames.Add(clip.name);
            }

            animationNames.AddRange(uniqueNames);
            animationNames.Sort(); // Alphabetical order

            // Clamp index if list shrank
            if (currentIndex >= animationNames.Count)
            {
                currentIndex = 0;
            }

            Debug.Log($"[PROTOFACTOR] Animation list rebuilt. Total animations: {animationNames.Count}");
        }

        private IEnumerator PlayAnimationsCycle()
        {
            while (true)
            {
                if (animationNames.Count == 0)
                {
                    yield return null;
                    continue;
                }

                string currentAnim = animationNames[currentIndex];

                bool isCurrentRootMotion = currentAnim.EndsWith("_RM");

                // === RESET POSITION LOGIC ===
                // If the PREVIOUS animation was a _RM, reset before playing this one
                if (lastPlayedWasRootMotion && resetPositionAfterRootMotion)
                {
                    transform.position = originalPosition;
                    transform.rotation = originalRotation;

                    Debug.Log($"[PROTOFACTOR] Reset position/rotation after _RM animation. Now playing: {currentAnim}");
                }

                // Play the animation
                animator.Play(currentAnim, 0, 0f);

                Debug.Log($"[PROTOFACTOR] Playing [{currentIndex + 1}/{animationNames.Count}]: {currentAnim}  |  RootMotion: {isCurrentRootMotion}");

                // Wait for animation to complete
                yield return new WaitForSeconds(GetCurrentAnimationLength() + 0.05f);

                // Remember if this one was root motion for the *next* animation
                lastPlayedWasRootMotion = isCurrentRootMotion;

                // Advance to next
                currentIndex++;
                if (currentIndex >= animationNames.Count)
                {
                    currentIndex = 0;
                    Debug.Log("[PROTOFACTOR] === Cycle complete. Restarting from beginning ===");
                }
            }
        }

        private float GetCurrentAnimationLength()
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            return stateInfo.length > 0 ? stateInfo.length : 1f; // fallback
        }

        /// <summary>
        /// Manually reset to the very first animation in the current list.
        /// </summary>
        public void ResetToFirstAnimation()
        {
            if (animationNames.Count > 0)
            {
                currentIndex = 0;
                lastPlayedWasRootMotion = false;

                if (playCoroutine != null)
                {
                    StopCoroutine(playCoroutine);
                }
                playCoroutine = StartCoroutine(PlayAnimationsCycle());
            }
        }

        /// <summary>
        /// Force reset character to original scene start position/rotation.
        /// </summary>
        public void ResetToOriginalPosition()
        {
            transform.position = originalPosition;
            transform.rotation = originalRotation;
            Debug.Log("[PROTOFACTOR] Manually reset to original position.");
        }

        void OnDisable()
        {
            if (playCoroutine != null)
            {
                StopCoroutine(playCoroutine);
            }
        }
    }
}