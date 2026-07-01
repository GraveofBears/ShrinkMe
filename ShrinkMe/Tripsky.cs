using UnityEngine;

namespace ShrinkMe
{
    /// <summary>
    /// Purely visual, local-only "trippy sky" effect for the duration of
    /// the Baked status effect, built specifically against Valheim's real
    /// skybox AND cloud materials/shaders (provided by the user):
    ///
    ///   Skybox:  Shader "Custom/SkyboxProcedural", material "skybox.mat"
    ///            (assigned via RenderSettings.skybox)
    ///   Clouds:  Shader "Custom/Clouds", used by several named material
    ///            assets: distant_cloud_planes.mat, distant_cloud_planes_2.mat,
    ///            distantclouds.mat, cloud_plane.mat, cloud_plane_menu.mat,
    ///            cloud_rain.mat, cloud_rain_downside.mat, cloud.mat,
    ///            rain_fogclouds.mat, cloud_upper.mat - found on Renderer
    ///            components in the scene (not a RenderSettings slot like
    ///            the skybox).
    ///
    /// DESIGN: static random colors, NOT continuous animation. Earlier
    /// versions of this effect tried to smoothly cycle/pulse sky, cloud,
    /// ambient, and fog colors every frame. That ran into a series of
    /// real bugs along the way (multi-slot renderers only getting slot 0
    /// tinted, periodic rescans accidentally re-cloning already-cloned
    /// materials every cycle, etc.) that produced visible flashing/
    /// strobing. Rather than keep chasing animation bugs, this version
    /// picks ONE random value for every affected property a SINGLE TIME
    /// (when the trip starts, or the first time a given cloud material is
    /// found) and never touches it again for the rest of the trip. A
    /// static random look can't strobe, because nothing keeps re-writing
    /// the value.
    ///
    /// Skybox properties (randomized once in SetupSkybox):
    ///   _SkyTint, _GroundColor, _MoonColor (Vector/Color), _SunSize,
    ///   _AtmosphereThickness, _Exposure (float).
    ///
    /// Cloud properties (randomized once per material, in
    /// DiscoverCloudMaterials, the first time each material is found):
    ///   _Color (Color, standard property - unlike the skybox's
    ///   Vector-typed color fields, this one is declared as an actual
    ///   Color in the shader).
    ///
    /// Both the skybox and every matched cloud material are cloned rather
    /// than mutated in place, since these are shared asset references
    /// (RenderSettings.skybox for the sky; sharedMaterials on potentially
    /// many Renderers for clouds) - mutating them directly would affect
    /// anything else in the game referencing the same asset and make
    /// "restore to original" unreliable. All original values are cached
    /// and restored exactly on End().
    ///
    /// NOTE - ambient light / fog color tinting was tried and removed.
    /// Confirmed by direct testing: continuously re-applying a FIXED
    /// target color toward RenderSettings.ambientLight/fogColor every
    /// frame (needed because EnvMan.SetEnv() overwrites both every single
    /// FixedUpdate tick) was the actual source of visible flashing on
    /// both the skybox and clouds - even though skybox/cloud colors
    /// themselves were already confirmed "set once, never touched again."
    /// Likely mechanism: EnvMan's own ambient/fog baseline moves
    /// continuously during day/night transitions, and blending toward a
    /// fixed target from a moving baseline every frame tracked that
    /// movement partially, reading as flicker. Decided to drop ambient/
    /// fog tinting entirely rather than rebuild it - see git history /
    /// conversation log if revisiting this is ever worthwhile.
    /// </summary>
    public class TripSky : MonoBehaviour
    {
        private static TripSky instance;

        // Shader property names, exactly as given in the real shader source.
        private const string PropSkyTint = "_SkyTint";
        private const string PropGroundColor = "_GroundColor";
        private const string PropMoonColor = "_MoonColor";
        private const string PropAtmosphereThickness = "_AtmosphereThickness";
        private const string PropSunSize = "_SunSize";
        private const string PropExposure = "_Exposure";
        private const string PropCloudColor = "_Color";

        // Exact cloud material names - matched against
        // Renderer.sharedMaterials entries on MeshRenderers only (see the
        // ParticleSystemRenderer skip in DiscoverCloudMaterials). Some of
        // these names (distant_cloud_planes, distant_cloud_planes_2,
        // rain_fogclouds) only ever appear on ParticleSystemRenderers in
        // practice, so they're effectively unreachable now - left in the
        // list rather than removed, since it's harmless and keeps this
        // list as the complete reference of every cloud material name we
        // know about, regardless of which renderer type currently uses it.
        private static readonly string[] CloudMaterialNames =
        {
            "distant_cloud_planes",
            "distant_cloud_planes_2",
            "distantclouds",
            "cloud_plane",
            "cloud_plane_menu",
            "cloud_rain",
            "cloud_rain_downside",
            "cloud",
            "rain_fogclouds",
            "cloud_upper",
        };

        private struct CloudEntry
        {
            public Material originalMaterial; // the shared asset we found
            public Material workingMaterial;  // our clone, assigned in its place
            public Color originalColor;
        }

        private Material originalSkyboxMaterial;
        private Material workingMaterial; // clone we actually animate

        private Color originalSkyTint;
        private Color originalGroundColor;
        private Color originalMoonColor;
        private float originalAtmosphereThickness;
        private float originalSunSize;
        private float originalExposure;

        private readonly System.Collections.Generic.List<CloudEntry> cloudEntries = new();

        public static void Begin()
        {
            if (instance != null) return;

            var go = new GameObject("ShrinkMe_TripSky");
            instance = go.AddComponent<TripSky>();
        }

        public static void End()
        {
            if (instance == null) return;
            var toDestroy = instance;
            instance = null;
            Destroy(toDestroy.gameObject);
        }

        private void Awake()
        {
            SetupSkybox();
            DiscoverCloudMaterials();

            // No per-frame behavior needed - everything is set once,
            // right here, and never touched again for the rest of the
            // trip (see the removed-Update()/removed-LateUpdate() notes
            // below for why this is deliberately fully static).
        }

        private void SetupSkybox()
        {
            if (RenderSettings.skybox == null)
            {
                Debug.LogWarning("[ShrinkMe] TripSky: RenderSettings.skybox is null, skipping skybox effect.");
                return;
            }

            originalSkyboxMaterial = RenderSettings.skybox;

            if (originalSkyboxMaterial.shader == null || originalSkyboxMaterial.shader.name != "Custom/SkyboxProcedural")
            {
                // Not the shader we expect (e.g. a different mod already
                // replaced the skybox, or a future Valheim update changed
                // it). Fail closed on the SKYBOX portion only - clouds are
                // handled independently in DiscoverCloudMaterials - rather
                // than risk SetColor/SetFloat calls against properties
                // that don't exist on whatever shader is actually active.
                Debug.LogWarning($"[ShrinkMe] TripSky: expected shader 'Custom/SkyboxProcedural' but found " +
                                  $"'{originalSkyboxMaterial.shader?.name ?? "null"}'. Skybox portion of the effect skipped.");
                originalSkyboxMaterial = null; // so OnDestroy knows there's nothing to restore
                return;
            }

            // Clone so we never mutate the shared asset directly.
            workingMaterial = new Material(originalSkyboxMaterial);
            RenderSettings.skybox = workingMaterial;

            originalSkyTint = workingMaterial.GetColor(PropSkyTint);
            originalGroundColor = workingMaterial.GetColor(PropGroundColor);
            originalMoonColor = workingMaterial.GetColor(PropMoonColor);
            originalAtmosphereThickness = workingMaterial.GetFloat(PropAtmosphereThickness);
            originalSunSize = workingMaterial.GetFloat(PropSunSize);
            originalExposure = workingMaterial.GetFloat(PropExposure);

            // Pick random values ONCE and apply them now - no per-frame
            // animation. Same rationale as the cloud color change: a
            // static random look for the whole trip duration, applied a
            // single time, can't strobe/flash since nothing keeps
            // touching these properties afterward. Saturation floors
            // pushed up from the original 0.5/0.4/0.3 for more vivid,
            // less washed-out color (the skybox gradient isn't multiplied
            // against a texture the way clouds are, so it doesn't have
            // the same washout problem - this is purely about matching
            // the clouds' new vividness rather than fixing a visibility bug).
            Color skyTint = Random.ColorHSV(0f, 1f, 0.75f, 1f, 0.85f, 1f);
            Color groundTint = Random.ColorHSV(0f, 1f, 0.65f, 0.95f, 0.7f, 0.95f);
            Color moonTint = Random.ColorHSV(0f, 1f, 0.5f, 0.8f, 0.5f, 0.8f);
            skyTint.a = originalSkyTint.a;
            groundTint.a = originalGroundColor.a;
            moonTint.a = originalMoonColor.a;

            workingMaterial.SetColor(PropSkyTint, skyTint);
            workingMaterial.SetColor(PropGroundColor, groundTint);
            workingMaterial.SetColor(PropMoonColor, moonTint);

            // Sun size, atmosphere thickness, exposure: pick a random
            // point within a reasonable range around the original value
            // (rather than a fully arbitrary number) so the sun doesn't
            // end up invisible (0) or absurdly oversized, and the
            // atmosphere/exposure stay within ranges that still look like
            // "sky" rather than pure white or pure black. Re-enabled
            // after testing confirmed these floats were NOT the cause of
            // the flashing - the continuous ambient/fog LateUpdate
            // re-application was (see the comment where LateUpdate used
            // to be, now removed entirely).
            float sunSize = Mathf.Clamp(originalSunSize * Random.Range(0.6f, 1.8f), 0.02f, 1f);
            float atmosphereThickness = Mathf.Clamp(originalAtmosphereThickness * Random.Range(0.7f, 1.6f), 0.2f, 5f);
            float exposure = Mathf.Clamp(originalExposure * Random.Range(0.85f, 1.3f), 0.3f, 8f);

            workingMaterial.SetFloat(PropSunSize, sunSize);
            workingMaterial.SetFloat(PropAtmosphereThickness, atmosphereThickness);
            workingMaterial.SetFloat(PropExposure, exposure);

            Debug.Log($"[ShrinkMe] TripSky: skybox randomized once - sky={skyTint}, ground={groundTint}, moon={moonTint}, sunSize={sunSize:F2}, atmosphere={atmosphereThickness:F2}, exposure={exposure:F2}");
        }

        private void DiscoverCloudMaterials()
        {
            // One clone per unique SOURCE material, not per-renderer:
            // multiple cloud renderers in the scene very likely share the
            // same material asset (e.g. EnvMan.m_clouds' MeshRenderer AND
            // the separate FogClouds particle system both use cloud.mat),
            // and we want them all to shift in unison as one cohesive
            // cloud layer rather than independently.
            //
            // Confirmed renderer sources for these materials (from
            // EnvMan.cs and scene inspection):
            //   - EnvMan.m_clouds            -> cloud.mat (MeshRenderer)
            //   - EnvMan.m_rainClouds        -> cloud_rain.mat (MeshRenderer)
            //   - EnvMan.m_rainCloudsDownside -> cloud_rain_downside.mat (MeshRenderer)
            //   - distant_fog_planes/mistcloud1+2 -> distant_cloud_planes(.mat)/_2 (ParticleSystemRenderer)
            //   - FogClouds/cloud            -> cloud.mat (ParticleSystemRenderer, SAME material as m_clouds)
            //   - separate upper-cloud-layer object -> cloud_upper.mat (not an EnvMan field)
            //
            // EnvMan.SetEnv() calls `this.m_clouds.material.SetFloat(_Rain, ...)`
            // every FixedUpdate tick. Since `.material` returns whatever
            // material is CURRENTLY assigned (our clone, once we've swapped
            // it in), this only ever sets the _Rain float on our clone - it
            // does not reassign or replace the material reference, so it
            // does not fight or undo our _Color changes.
            //
            // HISTORICAL CONTEXT (bug already fixed, kept for reference):
            // new Material(source) does NOT rename the clone - it keeps
            // the exact same .name as the source material (the automatic
            // " (Instance)" suffix only happens via the renderer.material
            // GETTER's implicit instancing, not via `new Material(...)`).
            // This used to matter because DiscoverCloudMaterials() was
            // called repeatedly (a periodic rescan, since removed for
            // performance - see Awake()) and would otherwise re-clone its
            // own already-created clones every cycle. Now that this
            // method only ever runs ONCE (from Awake()), that specific
            // failure mode can't happen anymore - but the seenSourceMaterials/
            // ourOwnClones tracking below still serves a real purpose
            // within this single pass: multiple renderers can share the
            // same source material (e.g. several renderers all using
            // cloud.mat), and we want all of them pointed at the SAME
            // clone, not a separate one each.
            var seenSourceMaterials = new System.Collections.Generic.Dictionary<Material, Material>();
            var ourOwnClones = new System.Collections.Generic.HashSet<Material>();
            foreach (var existing in cloudEntries)
            {
                if (existing.originalMaterial != null && existing.workingMaterial != null)
                {
                    seenSourceMaterials[existing.originalMaterial] = existing.workingMaterial;
                    ourOwnClones.Add(existing.workingMaterial);
                }
            }

            var renderers = FindObjectsOfType<Renderer>(includeInactive: true);
            foreach (var renderer in renderers)
            {
                // Skip ParticleSystemRenderer entirely - confirmed that
                // mistcloud1/mistcloud2 (and likely the FogClouds/cloud
                // particle system too, since it showed up matching
                // 'rain_fogclouds' in testing) have their "Color over
                // Lifetime" module enabled, which multiplies a per-particle
                // lifetime gradient ON TOP of whatever _Color we set on the
                // material. That gradient is independent of our material
                // tint and was producing a strobe effect tied to the
                // particle system's own emission bursts/loop - nothing we
                // can fix by changing the material color, since the
                // strobing was never coming from the material in the
                // first place. Decided to just leave particle-rendered
                // cloud layers untouched (still their original white)
                // rather than reach into the particle system's own
                // lifetime-gradient configuration to fix it there.
                if (renderer is ParticleSystemRenderer) continue;

                // sharedMaterials (plural) always returns a fresh COPY of
                // the array - we read it once, mutate our local copy, and
                // write the whole array back in one assignment, rather
                // than risk multiple partial reads/writes.
                var materials = renderer.sharedMaterials;
                bool changedAny = false;

                for (int i = 0; i < materials.Length; i++)
                {
                    var sharedMat = materials[i];
                    if (sharedMat == null) continue;

                    // If this slot already holds one of OUR clones (from
                    // a previous scan), it's already correctly set up -
                    // skip it entirely rather than letting the name check
                    // below match it again and create a redundant clone
                    // of our own clone.
                    if (ourOwnClones.Contains(sharedMat)) continue;

                    // *** CONFIRMED VIA UNITYEXPLORER SCREENSHOT ***
                    // cloud_plane_upper_downside's MeshRenderer material
                    // is literally named "cloud_plane (Instance)", not
                    // "cloud_plane" - something (Valheim's own code, or
                    // simply accessing renderer.material instead of
                    // .sharedMaterial somewhere) already auto-instanced
                    // this material before we ever got to it. The same
                    // screenshot shows 'cylinder' using "distantclouds
                    // (Instance)" too, so this isn't a one-off - any
                    // renderer that happened to get auto-instanced by
                    // something else before our scan ran would silently
                    // fail our exact-string-equality check and get
                    // skipped entirely, staying white with no warning.
                    // Fixed by matching either the exact name OR the name
                    // with Unity's auto-instance suffix stripped.
                    bool isCloudMaterial = false;
                    foreach (var name in CloudMaterialNames)
                    {
                        if (sharedMat.name == name || sharedMat.name == name + " (Instance)")
                        {
                            isCloudMaterial = true;
                            break;
                        }
                    }
                    if (!isCloudMaterial) continue;

                    if (!seenSourceMaterials.TryGetValue(sharedMat, out var clone))
                    {
                        clone = new Material(sharedMat);
                        seenSourceMaterials[sharedMat] = clone;
                        ourOwnClones.Add(clone);

                        Color originalColor = sharedMat.HasProperty(PropCloudColor) ? sharedMat.GetColor(PropCloudColor) : Color.white;

                        cloudEntries.Add(new CloudEntry
                        {
                            originalMaterial = sharedMat,
                            workingMaterial = clone,
                            originalColor = originalColor
                        });

                        // Pick ONE random color for this material, right
                        // now, and apply it once. No further per-frame
                        // writes to this property for the rest of the
                        // trip - this is the whole point of the
                        // "static random color, not animated" approach:
                        // it can't strobe/flash because nothing keeps
                        // touching it after this single assignment.
                        // Custom/Clouds multiplies the (likely near-white,
                        // bright) cloud texture by this color - a pastel,
                        // high-VALUE color multiplies into something close
                        // to white again and barely shows. Pushing
                        // saturation to near-MAX (not just "high") is the
                        // actual lever for vividness here - a fully
                        // saturated color survives the multiply much
                        // better than a partially-saturated one,
                        // regardless of value. Value stays moderate
                        // (0.55-0.85, nudged up slightly from the previous
                        // 0.45-0.75) so clouds read as vivid/colorful
                        // rather than dark and muddy.
                        Color randomColor = Random.ColorHSV(0f, 1f, 0.9f, 1f, 0.55f, 0.85f);
                        randomColor.a = originalColor.a; // preserve original opacity/alpha
                        clone.SetColor(PropCloudColor, randomColor);

                        Debug.Log($"[ShrinkMe] TripSky: cloud material matched '{sharedMat.name}' on renderer '{renderer.gameObject.name}' slot {i} (type {renderer.GetType().Name}), assigned random color {randomColor}.");
                    }

                    materials[i] = clone;
                    changedAny = true;
                }

                if (changedAny)
                {
                    // Write the WHOLE array back in one go - this updates
                    // every slot we touched (and leaves any slot we didn't
                    // touch exactly as it was), rather than reassigning
                    // sharedMaterial repeatedly and only ever affecting slot 0.
                    renderer.sharedMaterials = materials;
                }
            }

            if (cloudEntries.Count == 0)
            {
                Debug.LogWarning("[ShrinkMe] TripSky: no cloud renderers matched any known cloud material name.");
            }
        }

        // Update() removed entirely. It only contained a cheap per-frame
        // check that tracked cloud renderers hadn't reverted to their
        // original material (a safety net against the particle system's
        // own loop restart, an LOD swap, or some other system resetting
        // things). That was never actually confirmed to be necessary -
        // the real bugs we found and fixed (the periodic rescan
        // duplicating clones, and the "(Instance)" name mismatch) were
        // both unrelated to this check. Removed for a fully static
        // effect: skybox and cloud colors are set once in Awake() and
        // never touched again, for zero per-frame cost.


        // LateUpdate() removed entirely. CONFIRMED BY TESTING: the
        // continuous ambient/fog blend (re-applying a fixed target color
        // toward RenderSettings.ambientLight/fogColor every single frame,
        // to counteract EnvMan overwriting those fields every
        // FixedUpdate tick) was the actual source of the flashing -
        // skybox and clouds both stopped flashing the moment this was
        // disabled, even though their own code paths were already
        // confirmed "set once, never touched again." The likely
        // mechanism: EnvMan's own ambient/fog values move continuously
        // during any day/night transition (dawn/dusk blend factors
        // shifting every tick), and blending toward a FIXED target from a
        // CONTINUOUSLY MOVING baseline every frame tracked that movement
        // partially, which read as flicker. Decided not to attempt
        // ambient/fog tinting at all rather than rebuild this carefully -
        // it was the smallest, least essential part of the effect.

        private void OnDestroy()
        {
            if (originalSkyboxMaterial != null)
            {
                // Restore the ORIGINAL material reference, not just its
                // values - we swapped to a clone in Awake, so put the real
                // asset back rather than trying to reset every property on
                // the clone and leaving the clone assigned.
                RenderSettings.skybox = originalSkyboxMaterial;

                if (workingMaterial != null)
                {
                    Destroy(workingMaterial);
                }
            }

            // Restore every renderer that was switched to a cloud clone
            // back to its original shared material, then destroy the
            // clones. We re-scan renderers rather than caching them
            // up-front, since cloud planes could in principle be
            // destroyed/recreated by the game while our effect was
            // active (e.g. distant cloud LOD swaps) - re-scanning is the
            // safer choice over holding stale Renderer references.
            if (cloudEntries.Count > 0)
            {
                var cloneToOriginal = new System.Collections.Generic.Dictionary<Material, Material>();
                foreach (var entry in cloudEntries)
                {
                    if (entry.workingMaterial != null && entry.originalMaterial != null)
                    {
                        cloneToOriginal[entry.workingMaterial] = entry.originalMaterial;
                    }
                }

                var renderers = FindObjectsOfType<Renderer>(includeInactive: true);
                foreach (var renderer in renderers)
                {
                    // Same plural-array reasoning as DiscoverCloudMaterials:
                    // a renderer could have our clone in ANY slot, not just
                    // slot 0, so we check/restore every slot rather than
                    // just renderer.sharedMaterial.
                    var materials = renderer.sharedMaterials;
                    bool changedAny = false;

                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null && cloneToOriginal.TryGetValue(materials[i], out var original))
                        {
                            materials[i] = original;
                            changedAny = true;
                        }
                    }

                    if (changedAny)
                    {
                        renderer.sharedMaterials = materials;
                    }
                }

                foreach (var entry in cloudEntries)
                {
                    if (entry.workingMaterial != null)
                    {
                        Destroy(entry.workingMaterial);
                    }
                }
            }
        }
    }
}