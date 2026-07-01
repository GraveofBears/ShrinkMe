using UnityEngine;

namespace ShrinkMe
{
    /// <summary>
    /// Scales the player's actual collision geometry to match the visual
    /// scale applied by SE_Shrink/ShrinkSync.
    ///
    /// Why this is needed: ShrinkSync only ever resized
    /// transform.Find("Visual").localScale - that resizes the rendered
    /// mesh, but Valheim's movement collision is driven by a
    /// CharacterController component sitting on the player ROOT, not
    /// under Visual. CharacterController.radius/height are absolute
    /// values, NOT affected by transform.localScale on a child object.
    /// So a shrunk player kept colliding with the world at full size
    /// (and a grown player could still fit through gaps meant for normal
    /// size) - the exact "still uses the normal collider" symptom.
    ///
    /// There is also typically a separate CapsuleCollider used for combat
    /// hit detection (taking/dealing damage), which has the same problem
    /// independently of the CharacterController.
    ///
    /// This class caches each component's ORIGINAL values the first time
    /// it sees them, then derives the scaled value from that original any time
    /// SetScale is called - so repeated shrink/grow cycles can't drift
    /// from compounding a scale onto an already-scaled value.
    /// </summary>
    public static class ShrinkCollider
    {
        private struct OriginalCapsule
        {
            public float radius;
            public float height;
            public Vector3 center;
        }

        private struct OriginalController
        {
            public float radius;
            public float height;
            public Vector3 center;
        }

        // Cached per-character originals, keyed by the component instance
        // itself so this safely handles multiple players without mixing
        // up whose "original" belongs to whom.
        private static readonly System.Collections.Generic.Dictionary<CharacterController, OriginalController> originalControllers = new();
        private static readonly System.Collections.Generic.Dictionary<CapsuleCollider, OriginalCapsule> originalCapsules = new();

        public static void ApplyScale(Character character, float scale)
        {
            if (character == null) return;

            ApplyToCharacterController(character, scale);
            ApplyToCapsuleColliders(character, scale);
        }

        private static void ApplyToCharacterController(Character character, float scale)
        {
            var cc = character.GetComponent<CharacterController>();
            if (cc == null) return;

            if (!originalControllers.TryGetValue(cc, out var original))
            {
                original = new OriginalController
                {
                    radius = cc.radius,
                    height = cc.height,
                    center = cc.center
                };
                originalControllers[cc] = original;
            }

            cc.radius = original.radius * scale;
            cc.height = original.height * scale;
            cc.center = original.center * scale;
        }

        private static void ApplyToCapsuleColliders(Character character, float scale)
        {
            // GetComponentsInChildren so we catch a combat/hitbox capsule
            // that may live on a child object rather than the root - but
            // we deliberately do NOT touch colliders that are themselves
            // under "Visual", since the Visual transform's own localScale
            // change already scales anything parented under it for free.
            // Re-scaling those again would double-apply the scale.
            var visual = character.transform.Find("Visual");

            var capsules = character.GetComponentsInChildren<CapsuleCollider>(true);
            foreach (var capsule in capsules)
            {
                if (visual != null && capsule.transform.IsChildOf(visual))
                {
                    continue; // already scaled via Visual's localScale
                }

                if (!originalCapsules.TryGetValue(capsule, out var original))
                {
                    original = new OriginalCapsule
                    {
                        radius = capsule.radius,
                        height = capsule.height,
                        center = capsule.center
                    };
                    originalCapsules[capsule] = original;
                }

                capsule.radius = original.radius * scale;
                capsule.height = original.height * scale;
                capsule.center = original.center * scale;
            }
        }
    }
}