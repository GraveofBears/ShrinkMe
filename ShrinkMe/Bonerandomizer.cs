using System;
using System.Collections.Generic;
using UnityEngine;
namespace ShrinkMe
{
    /// <summary>
    /// Randomizes individual bone scales on the player's rig.
    ///
    /// Sync approach: rather than syncing the resulting per-bone scale
    /// values themselves (which would mean sending one float per bone over
    /// the network, and would need to be re-sent any time anything
    /// changed), we sync a single int seed via the ZDO. Every client runs
    /// the exact same deterministic RNG sequence from that seed and arrives
    /// at the same per-bone scales independently. This keeps network cost
    /// to one int regardless of how many bones are affected.
    ///
    /// Legs are deliberately EXCLUDED from randomization. Each leg bone
    /// gets its own independent random roll, so a naive "randomize every
    /// bone" pass produces mismatched leg lengths (one leg noticeably
    /// bigger/smaller than the other) - that reads as a broken/glitched
    /// model rather than a fun wobbly one, since legs need to stay paired
    /// for the silhouette to make sense. Matching left/right rolls would
    /// fix the mismatch but adds complexity for a part of the body that
    /// isn't the visual focus anyway - simplest fix is to just leave legs
    /// untouched and keep randomizing everything else (arms, spine, head,
    /// etc., which don't have the same paired-symmetry problem).
    /// </summary>
    public class BoneRandomizer : MonoBehaviour
    {
        [Tooltip("Minimum per-bone scale multiplier.")]
        public float minScale = 0.6f;
        [Tooltip("Maximum per-bone scale multiplier.")]
        public float maxScale = 1.6f;

        // Case-insensitive substring patterns used to identify and skip
        // leg bones, regardless of which naming convention the rig uses
        // (Mixamo-style "UpperLeg_L"/"LeftUpLeg", or simpler "Leg_L", etc).
        // "Foot" and "Toe" are included since they're part of the same
        // limb and would look just as mismatched if scaled independently
        // of the leg they're attached to.
        private static readonly string[] LegBonePatterns =
        {
            "leg",
            "foot",
            "toe",
            "shin",
            "calf",
            "thigh",
        };

        private readonly Dictionary<Transform, Vector3> originalScales = new();
        private int appliedSeed = int.MinValue;

        private static bool IsLegBone(string boneName)
        {
            string lower = boneName.ToLowerInvariant();
            foreach (var pattern in LegBonePatterns)
            {
                if (lower.Contains(pattern)) return true;
            }
            return false;
        }

        public void ApplySeed(int seed)
        {
            if (seed == appliedSeed) return;
            appliedSeed = seed;
            var smr = GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.bones == null || smr.bones.Length == 0)
            {
                Debug.LogWarning("[ShrinkMe] BoneRandomizer: no SkinnedMeshRenderer/bones found.");
                return;
            }
            // Deterministic, isolated RNG - do NOT touch UnityEngine.Random's
            // global state here, since that would affect/be affected by
            // anything else in the game calling Random that frame.
            var rng = new System.Random(seed);
            foreach (var bone in smr.bones)
            {
                if (bone == null) continue;
                if (IsLegBone(bone.name)) continue;

                if (!originalScales.ContainsKey(bone))
                {
                    originalScales[bone] = bone.localScale;
                }
                float scale = (float)(minScale + rng.NextDouble() * (maxScale - minScale));
                bone.localScale = originalScales[bone] * scale;
            }
        }
        public void ResetBones()
        {
            foreach (var kvp in originalScales)
            {
                if (kvp.Key == null) continue;
                kvp.Key.localScale = kvp.Value;
            }
            originalScales.Clear();
            appliedSeed = int.MinValue;
        }
        private void OnDisable()
        {
            ResetBones();
        }
    }
}