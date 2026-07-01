using UnityEngine;

namespace ShrinkMe
{
    public static class ShrinkUtils
    {
        // Single isolated RNG instance instead of repeatedly reseeding
        // UnityEngine.Random's GLOBAL state from Time.time. The old approach
        // had two problems:
        //  1. Reseeding the global Random affects every other system in the
        //     game that calls UnityEngine.Random the same frame.
        //  2. Seeding from (Time.time + val) * 1000 can produce identical
        //     seeds if called twice within the same frame/tick (e.g. two
        //     players drinking the pipe simultaneously), making their rolls
        //     correlated instead of independent.
        private static readonly System.Random rng = new System.Random();

        public static int RollDice(this int val)
        {
            return rng.Next(0, val);
        }

        /// <summary>
        /// Produces a fresh int suitable for seeding deterministic
        /// per-client systems (e.g. BoneRandomizer) that need to be synced
        /// as a single small value rather than per-result data.
        /// </summary>
        public static int NextSeed()
        {
            return rng.Next(int.MinValue, int.MaxValue);
        }
    }
}