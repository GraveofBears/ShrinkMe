using System.Linq;
using UnityEngine;

namespace ShrinkMe
{
    public class SE_Shrink : SE_Stats
    {
        internal static int randomnum;
        private int boneSeed;

        public void OnEnable()
        {
            m_name = "Baked!";
            m_icon = ShrinkMe.HaldorPipe?.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons.First();
            m_cooldown = 320;
            m_ttl = 300;
            m_speedModifier = .05f;
        }

        public override void Setup(Character character)
        {
            int dice = 12;
            randomnum = dice.RollDice();

            // One seed per "trip" - reused for the whole duration so the
            // bone randomization doesn't reshuffle every tick. Derived from
            // ShrinkUtils' isolated RNG, not UnityEngine.Random's global state.
            boneSeed = ShrinkUtils.NextSeed();

            var player = character as Player;
            if (player != null && player == Player.m_localPlayer && ShrinkMe.skyEffectEnabled.Value)
            {
                // Purely visual, local-client-only - no sync needed, this
                // never touches anything another player would see.
                TripSky.Begin();
            }

            base.Setup(character);
        }

        public override void UpdateStatusEffect(float dt)
        {
            var player = m_character as Player;
            if (player != null && player == Player.m_localPlayer)
            {
                float scale = SE_Shrink.randomnum == ShrinkMe.luckyno.Value
                    ? ShrinkMe.biggestsize.Value
                    : ShrinkMe.smallestshrink.Value;

                var sync = player.GetComponent<ShrinkSync>();
                if (sync == null)
                {
                    sync = player.gameObject.AddComponent<ShrinkSync>();
                }

                sync.SetState(
                    scale,
                    ShrinkMe.randomBoneSizeEnabled.Value,
                    boneSeed);
            }

            base.UpdateStatusEffect(dt);
        }

        public override void Stop()
        {
            var player = m_character as Player;
            if (player != null && player == Player.m_localPlayer)
            {
                float normal = 0.95f;

                var sync = player.GetComponent<ShrinkSync>();
                if (sync == null)
                {
                    sync = player.gameObject.AddComponent<ShrinkSync>();
                }

                // Explicitly turn bone-random off on stop, regardless of
                // config, so the effect fully clears when it ends.
                sync.SetState(normal, false, boneSeed);

                TripSky.End();
            }

            base.Stop();
        }
    }
}