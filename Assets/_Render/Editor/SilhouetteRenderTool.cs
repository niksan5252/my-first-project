using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace TunnelRender
{
    /// <summary>
    /// Offline silhouette render for video editing (not part of the game, not in the build).
    /// Plays a humanoid clip on the character in Edit Mode, frame by frame, and writes a PNG sequence
    /// with alpha: flat black body, white outline along the outer contour only.
    ///
    /// Frames are rendered straight through a CommandBuffer from baked skinned meshes, so the result is
    /// frame-exact and independent of URP post-processing and of Play Mode timing.
    /// </summary>
    public static class SilhouetteRenderTool
    {
        const string Root = "Assets/_Render";
        const string SilhouetteFolder = Root + "/Silhouette";
        const string ScenePath = Root + "/SilhouetteRender.unity";
        const string SettingsPath = Root + "/Editor/SilhouetteRenderSettings.asset";
        const string CharacterPath = SilhouetteFolder + "/Jennifer.fbx";
        const string CharacterSource = "Assets/SharedAssets/Mixamo/Characters/Jennifer.fbx";
        const string CharacterName = "Character";
        const string CameraName = "RenderCamera";

        // ---------------------------------------------------------------- setup

        [MenuItem("Tools/Silhouette Render/Setup Scene")]
        public static void SetupScene()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[SilhouetteRender] Leave Play Mode first."); return; }

            EnsureFolder(SilhouetteFolder);

            // The project already holds Jennifer with the embedded textures stripped (2 MB instead of 54 MB);
            // same mesh and skeleton, and the silhouette never uses textures.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath) == null)
                AssetDatabase.CopyAsset(CharacterSource, CharacterPath);
            ConfigureHumanoid(CharacterPath, importAnimation: false);

            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
            var settings = LoadOrCreateSettings();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(character, scene);
            instance.name = CharacterName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.avatar = AvatarOf(CharacterPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // A real orthographic camera, so the framing can be previewed and tweaked in the Scene/Game view.
            var camGo = new GameObject(CameraName);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            camGo.transform.SetPositionAndRotation(new Vector3(0, 1, 10), Quaternion.Euler(0, 180, 0));

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (settings.clip != null) FrameCamera(settings);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SilhouetteRender] Scene ready: {ScenePath}");
        }

        [MenuItem("Tools/Silhouette Render/Import Animation FBX...")]
        public static void ImportAnimationFromDialog()
        {
            var src = EditorUtility.OpenFilePanel("Mixamo animation FBX", "", "fbx");
            if (!string.IsNullOrEmpty(src)) ImportAnimation(src);
        }

        /// <summary>
        /// Copies a Mixamo animation FBX into the render folder as a humanoid clip and selects it in the settings.
        /// mirror = true imports a left/right mirrored copy under its own name, next to the original.
        /// </summary>
        public static AnimationClip ImportAnimation(string sourceFile, bool mirror = false, bool selectInSettings = true)
        {
            EnsureFolder(SilhouetteFolder);
            var name = Path.GetFileNameWithoutExtension(sourceFile) + (mirror ? " (Mirror)" : "");
            var assetPath = SilhouetteFolder + "/" + name + ".fbx";
            File.Copy(sourceFile, ProjectPath(assetPath), overwrite: true);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ConfigureHumanoid(assetPath, importAnimation: true, mirror: mirror);

            AnimationClip clip = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) { clip = c; break; }

            if (selectInSettings)
            {
                var settings = LoadOrCreateSettings();
                settings.clip = clip;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[SilhouetteRender] Imported {assetPath}: clip '{clip?.name}', {clip?.length:0.00} s");
            return clip;
        }

        /// <summary>
        /// Renders one move without touching the saved settings: the clips play back to back
        /// (e.g. "stand to crouch" then "crouch to stand"), the whole sequence repeated 'loops' times.
        /// Size, fps, outline and margin come from the settings asset.
        /// </summary>
        public static string RenderSequence(AnimationClip[] clips, string outputFolder, int loops = 1)
        {
            var saved = LoadOrCreateSettings();
            var s = Object.Instantiate(saved);
            s.clip = clips[0];
            s.then = clips.Length > 1 ? clips[1..] : new AnimationClip[0];
            s.outputFolder = outputFolder;
            s.loops = loops;
            try { return Render(s); }
            finally { Object.DestroyImmediate(s); }
        }

        // ---------------------------------------------------------------- render

        [MenuItem("Tools/Silhouette Render/Render")]
        public static void RenderMenu() => Render();

        public static string Render() => Render(LoadOrCreateSettings());

        public static string Render(SilhouetteRenderSettings settings)
        {
            if (EditorApplication.isPlaying) return Fail("Leave Play Mode first.");
            if (settings.clip == null) return Fail("No clip in " + SettingsPath);

            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return Fail("Cancelled.");
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            FrameCamera(settings);

            var character = GameObject.Find(CharacterName);
            var cam = GameObject.Find(CameraName).GetComponent<Camera>();
            var skinned = character.GetComponentsInChildren<SkinnedMeshRenderer>();

            var outDir = ProjectPath(settings.outputFolder);
            Directory.CreateDirectory(outDir);
            foreach (var old in Directory.GetFiles(outDir, "*.png")) File.Delete(old);

            int ss = Mathf.Max(1, settings.supersample);
            int bigW = settings.width * ss, bigH = settings.height * ss;
            var mask = new RenderTexture(bigW, bigH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            var outlined = new RenderTexture(bigW, bigH, 0, RenderTextureFormat.ARGB32);
            var final = new RenderTexture(settings.width, settings.height, 0, RenderTextureFormat.ARGB32)
                { filterMode = FilterMode.Bilinear };
            outlined.filterMode = FilterMode.Bilinear;
            var readback = new Texture2D(settings.width, settings.height, TextureFormat.RGBA32, false);

            var bodyMat = new Material(Shader.Find("Hidden/SilhouetteRender/BodyMask"));
            var outlineMat = new Material(Shader.Find("Hidden/SilhouetteRender/Outline"));
            outlineMat.SetFloat("_RadiusPx", settings.outlinePx * ss);
            outlineMat.SetColor("_OutlineColor", settings.outlineColor);

            // Glow is computed at half the output size: cheap, and a glow needs no detail.
            bool glow = settings.glowRadiusPx > 0.01f && settings.glowIntensity > 0.001f;
            var glowMat = new Material(Shader.Find("Hidden/SilhouetteRender/Glow"));
            glowMat.SetFloat("_Intensity", settings.glowIntensity);
            int gw = Mathf.Max(1, settings.width / 2), gh = Mathf.Max(1, settings.height / 2);
            var glowA = new RenderTexture(gw, gh, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            var glowB = new RenderTexture(gw, gh, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
            var composed = new RenderTexture(settings.width, settings.height, 0, RenderTextureFormat.ARGB32);
            // 13 taps span +-6 steps; two blur iterations widen it by ~sqrt(2).
            float glowStep = settings.glowRadiusPx / 2f / 6f / 1.41f;

            var baked = new Mesh[skinned.Length];
            for (int i = 0; i < baked.Length; i++) baked[i] = new Mesh();

            var seq = new Sequence(character, settings);
            float duration = seq.Length * settings.loops;
            int frames = Mathf.Max(1, Mathf.RoundToInt(duration * settings.fps));
            string previewPath = null;

            AnimationMode.StartAnimationMode();
            try
            {
                for (int f = 0; f < frames; f++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Silhouette Render", $"Frame {f + 1}/{frames}", (float)f / frames))
                        break;

                    seq.Sample(f / (float)settings.fps);

                    var cmd = new CommandBuffer { name = "SilhouetteRender" };
                    cmd.SetRenderTarget(mask);
                    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
                    for (int i = 0; i < skinned.Length; i++)
                    {
                        if (!skinned[i].enabled || !skinned[i].gameObject.activeInHierarchy) continue;
                        skinned[i].BakeMesh(baked[i]);
                        var m = skinned[i].transform.localToWorldMatrix;
                        for (int sub = 0; sub < baked[i].subMeshCount; sub++)
                            cmd.DrawMesh(baked[i], m, bodyMat, sub, 0);
                    }
                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Release();

                    Graphics.Blit(mask, outlined, outlineMat);
                    Graphics.Blit(outlined, final); // box-filtered downsample of premultiplied colour

                    var result = final;
                    if (glow)
                    {
                        Graphics.Blit(final, glowA, glowMat, 0);
                        for (int it = 0; it < 2; it++)
                        {
                            glowMat.SetVector("_Direction", new Vector4(glowStep, 0, 0, 0));
                            Graphics.Blit(glowA, glowB, glowMat, 1);
                            glowMat.SetVector("_Direction", new Vector4(0, glowStep, 0, 0));
                            Graphics.Blit(glowB, glowA, glowMat, 1);
                        }
                        glowMat.SetTexture("_GlowTex", glowA);
                        Graphics.Blit(final, composed, glowMat, 2);
                        result = composed;
                    }

                    var prev = RenderTexture.active;
                    RenderTexture.active = result;
                    readback.ReadPixels(new Rect(0, 0, settings.width, settings.height), 0, 0);
                    readback.Apply();
                    RenderTexture.active = prev;

                    var px = readback.GetPixels32();
                    Unpremultiply(px);
                    readback.SetPixels32(px);
                    File.WriteAllBytes(Path.Combine(outDir, $"frame_{f:0000}.png"), readback.EncodeToPNG());

                    if (f == frames / 3) previewPath = WritePreview(px, settings);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AnimationMode.StopAnimationMode();
                seq.Destroy();
                foreach (var m in baked) Object.DestroyImmediate(m);
                Object.DestroyImmediate(bodyMat);
                Object.DestroyImmediate(outlineMat);
                Object.DestroyImmediate(glowMat);
                Object.DestroyImmediate(readback);
                mask.Release(); outlined.Release(); final.Release();
                glowA.Release(); glowB.Release(); composed.Release();
            }

            var written = Directory.GetFiles(outDir, "*.png").Length;
            var msg = $"[SilhouetteRender] {written} frames ({settings.width}x{settings.height}, {settings.fps} fps, " +
                      $"'{seq.Names}' {seq.Length:0.00} s x{settings.loops}) -> {outDir}; preview: {previewPath}";
            Debug.Log(msg);
            return msg;
        }

        // ---------------------------------------------------------------- framing

        /// <summary>
        /// Fixes the camera for the whole clip: full body, centred, the same margin above the highest and
        /// below the lowest point of the move. The camera never moves between frames.
        /// </summary>
        public static void FrameCamera(SilhouetteRenderSettings settings)
        {
            var character = GameObject.Find(CharacterName);
            var cam = GameObject.Find(CameraName).GetComponent<Camera>();
            var skinned = character.GetComponentsInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            var bounds = new Bounds();
            bool first = true;

            var seq = new Sequence(character, settings);
            AnimationMode.StartAnimationMode();
            try
            {
                int samples = Mathf.Max(2, Mathf.CeilToInt(seq.Length * settings.fps));
                for (int s = 0; s < samples; s++)
                {
                    seq.Sample(seq.Length * s / samples);
                    foreach (var r in skinned)
                    {
                        r.BakeMesh(mesh);
                        var m = r.transform.localToWorldMatrix;
                        foreach (var v in mesh.vertices)
                        {
                            var w = m.MultiplyPoint3x4(v);
                            if (first) { bounds = new Bounds(w, Vector3.zero); first = false; }
                            else bounds.Encapsulate(w);
                        }
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                seq.Destroy();
                Object.DestroyImmediate(mesh);
            }

            float aspect = settings.width / (float)settings.height;
            float outlineWorld = 0; // the outline is added in screen space; reserve its pixels in the margin
            float halfH = bounds.extents.y / Mathf.Max(0.05f, 1f - 2f * settings.verticalMargin);
            halfH = Mathf.Max(halfH, bounds.extents.x / aspect * 1.05f);
            outlineWorld = halfH * 2f * settings.outlinePx / settings.height;
            cam.orthographicSize = halfH + outlineWorld;
            cam.aspect = aspect;
            cam.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.center.y, bounds.center.z + 10f), Quaternion.Euler(0, 180, 0));
            EditorUtility.SetDirty(cam.transform);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>The settings' clip followed by its 'then' clips, sampled as one continuous move.</summary>
        sealed class Sequence
        {
            readonly AnimationClip[] clips;
            readonly PlayableGraph[] graphs;
            readonly AnimationClipPlayable[] playables;
            public readonly float Length;

            public Sequence(GameObject character, SilhouetteRenderSettings settings)
            {
                var list = new System.Collections.Generic.List<AnimationClip> { settings.clip };
                if (settings.then != null)
                    foreach (var c in settings.then) if (c != null) list.Add(c);
                clips = list.ToArray();
                graphs = new PlayableGraph[clips.Length];
                playables = new AnimationClipPlayable[clips.Length];
                for (int i = 0; i < clips.Length; i++)
                {
                    graphs[i] = CreateGraph(character, clips[i], out playables[i]);
                    Length += clips[i].length;
                }
            }

            public string Names => string.Join(" + ", System.Array.ConvertAll(clips, c => c.name));

            public void Sample(float time)
            {
                float t = Length > 0 ? time % Length : 0;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (t <= clips[i].length || i == clips.Length - 1)
                    {
                        SamplePose(graphs[i], playables[i], Mathf.Min(t, clips[i].length));
                        return;
                    }
                    t -= clips[i].length;
                }
            }

            public void Destroy()
            {
                foreach (var g in graphs) if (g.IsValid()) g.Destroy();
            }
        }

        static PlayableGraph CreateGraph(GameObject character, AnimationClip clip, out AnimationClipPlayable clipPlayable)
        {
            var animator = character.GetComponent<Animator>();
            var graph = PlayableGraph.Create("SilhouetteRender");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            clipPlayable = AnimationClipPlayable.Create(graph, clip);
            clipPlayable.SetApplyFootIK(true);
            output.SetSourcePlayable(clipPlayable);
            return graph;
        }

        static void SamplePose(PlayableGraph graph, AnimationClipPlayable clipPlayable, float time)
        {
            clipPlayable.SetTime(time);
            AnimationMode.BeginSampling();
            AnimationMode.SamplePlayableGraph(graph, 0, time);
            AnimationMode.EndSampling();
        }

        static void ConfigureHumanoid(string assetPath, bool importAnimation, bool mirror = false)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(assetPath);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = importAnimation;
            if (importAnimation)
            {
                var clips = importer.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.loopTime = false;
                    c.mirror = mirror;
                    // In place: the figure never drifts out of the fixed frame.
                    c.lockRootRotation = true;
                    c.lockRootHeightY = true;
                    c.lockRootPositionXZ = true;
                    // Body orientation, not the mocap's: a boxing stance filmed half-turned still faces the camera.
                    c.keepOriginalOrientation = false;
                    c.keepOriginalPositionY = true;
                    c.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = clips;
            }
            importer.SaveAndReimport();
        }

        static Avatar AvatarOf(string assetPath)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (o is Avatar a) return a;
            return null;
        }

        static SilhouetteRenderSettings LoadOrCreateSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<SilhouetteRenderSettings>(SettingsPath);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<SilhouetteRenderSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        static void Unpremultiply(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a == 0) { px[i] = new Color32(0, 0, 0, 0); continue; }
                if (c.a == 255) continue;
                float k = 255f / c.a;
                px[i] = new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), c.a);
            }
        }

        /// <summary>One frame composited on a dark background, to judge the outline thickness.</summary>
        static string WritePreview(Color32[] px, SilhouetteRenderSettings settings)
        {
            var bg = new Color32(32, 32, 36, 255);
            var outPx = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
                outPx[i] = Color32.Lerp(bg, new Color32(px[i].r, px[i].g, px[i].b, 255), px[i].a / 255f);
            var tex = new Texture2D(settings.width, settings.height, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            var dir = ProjectPath("Screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"silhouette_render_preview_{Path.GetFileName(settings.outputFolder)}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string ProjectPath(string relative) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

        static string Fail(string message)
        {
            Debug.LogError("[SilhouetteRender] " + message);
            return "ERROR: " + message;
        }
    }
}
