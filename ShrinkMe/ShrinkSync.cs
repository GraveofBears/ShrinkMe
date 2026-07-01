using UnityEngine;

namespace ShrinkMe
{
    /// <summary>
    /// Drives all networked state for the Shrink effect off the player's own ZDO.
    ///
    /// Why ZDO instead of RPC broadcast:
    /// - ZDOs replicate automatically to every client in range, including
    ///   players who join/load in AFTER the effect was applied. A one-shot
    ///   RPC broadcast (the old approach) only reaches clients who happen to
    ///   be connected at the exact moment it fires.
    /// - The old code only sent the broadcast when ZNet.instance.IsServer()
    ///   was true. In a normal multiplayer session the affected player is
    ///   usually a CLIENT, not the server, so that condition was false and
    ///   the broadcast silently never went out. That was the main reason
    ///   sync never worked.
    /// - Per the Player ZDO caveat: Player ZDOs are not persisted across
    ///   relog/respawn the way world-object ZDOs are. So we still need to
    ///   re-apply visuals locally whenever this component (re)initializes -
    ///   we don't rely on the ZDO to "remember" state long-term, only to
    ///   replicate it live between currently-connected clients.
    /// </summary>
    public class ShrinkSync : MonoBehaviour
    {
        // ZDO custom-data keys. Keep these unique/namespaced so we don't
        // collide with vanilla fields or other mods.
        private const string KeyScale = "ShrinkMe_Scale";
        private const string KeyBoneSeed = "ShrinkMe_BoneSeed";
        private const string KeyBoneRandom = "ShrinkMe_BoneRandom";

        private Character character;
        private ZNetView znv;

        private float lastAppliedScale = -1f;
        private bool lastAppliedBoneRandom;
        private int lastAppliedBoneSeed;

        private void Awake()
        {
            character = GetComponent<Character>();
            znv = GetComponent<ZNetView>();
        }

        private void OnEnable()
        {
            // Re-apply whatever the ZDO currently says, in case this
            // component is coming back after a relog/respawn where the
            // visual state was reset but the session-level ZDO value
            // (set by another still-connected client's view of us) lingers.
            ApplyFromZdo(force: true);
        }

        private void Update()
        {
            // Cheap poll for ZDO changes. Valheim doesn't give us a clean
            // "ZDO changed" callback for arbitrary custom fields, so we
            // diff against the last value we applied. This runs once per
            // frame per affected character only, so it's negligible cost.
            ApplyFromZdo(force: false);
        }

        private bool HasValidZdo()
        {
            return znv != null && znv.IsValid();
        }

        /// <summary>
        /// Called locally by the status effect on the owning client to push
        /// a new state out. Writes to this character's own ZDO; ZNetView
        /// takes care of replicating that ZDO to other clients.
        /// </summary>
        public void SetState(float scale, bool boneRandomEnabled, int boneSeed)
        {
            if (!HasValidZdo()) return;

            var zdo = znv.GetZDO();
            zdo.Set(KeyScale, scale);
            zdo.Set(KeyBoneRandom, boneRandomEnabled);
            zdo.Set(KeyBoneSeed, boneSeed);

            // Apply immediately on this client too rather than waiting for
            // our own next Update poll - avoids a one-frame visual lag for
            // the player actually triggering the change.
            ApplyFromZdo(force: true);
        }

        private void ApplyFromZdo(bool force)
        {
            if (!HasValidZdo()) return;

            var zdo = znv.GetZDO();

            float scale = zdo.GetFloat(KeyScale, 1f);
            bool boneRandom = zdo.GetBool(KeyBoneRandom, false);
            int boneSeed = zdo.GetInt(KeyBoneSeed, 0);

            bool scaleChanged = force || !Mathf.Approximately(scale, lastAppliedScale);
            bool boneChanged = force || boneRandom != lastAppliedBoneRandom || boneSeed != lastAppliedBoneSeed;

            if (!scaleChanged && !boneChanged) return;

            if (scaleChanged)
            {
                ApplyUniformScale(scale);
                lastAppliedScale = scale;
            }

            if (boneChanged)
            {
                SetBoneRandomEnabled(boneRandom, boneSeed);
                lastAppliedBoneRandom = boneRandom;
                lastAppliedBoneSeed = boneSeed;
            }
        }

        private Transform FindVisual()
        {
            // Player rig keeps its mesh under a "Visual" child.
            return transform.Find("Visual");
        }

        private void ApplyUniformScale(float scale)
        {
            var visual = FindVisual();
            if (visual == null) return;
            visual.localScale = new Vector3(scale, scale, scale);

            // The collider lives on the player root (or a dedicated
            // collider child), separate from "Visual". Scaling Visual
            // alone resizes the model but leaves hit/physical bounds at
            // their original size - see ShrinkCollider.cs for the fix.
            ShrinkCollider.ApplyScale(character, scale);
        }

        private void SetBoneRandomEnabled(bool enabled, int seed)
        {
            var visual = FindVisual();
            if (visual == null) return;

            var randomizer = visual.GetComponent<BoneRandomizer>();
            if (enabled)
            {
                if (randomizer == null)
                {
                    randomizer = visual.gameObject.AddComponent<BoneRandomizer>();
                }
                randomizer.ApplySeed(seed);
            }
            else if (randomizer != null)
            {
                randomizer.ResetBones();
                Destroy(randomizer);
            }
        }
    }
}