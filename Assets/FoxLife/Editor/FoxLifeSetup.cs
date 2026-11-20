using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Меню FoxLife → Create Main Scene: создаёт сцену и настраивает проект для сборки.</summary>
public static class FoxLifeSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("FoxLife/Create Main Scene")]
    public static void Run()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("FoxLife").AddComponent<FoxLife.Game>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.productName = "Лисья жизнь"; // не менять: от имени зависит папка сохранений и настроек (AppData/LocalLow)
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        AssetDatabase.SaveAssets();
        Debug.Log("FoxLife: scene created at " + ScenePath);
    }

    const string IconPath = "Assets/FoxLife/Icon.png";

    /// <summary>Иконка игры: воксельная мордочка лисички на голубом круге (рисуется кодом).</summary>
    [MenuItem("FoxLife/Make Icon")]
    public static void MakeIcon()
    {
        const int N = 32, S = 8; // 32×32 «вокселя» по 8 пикселей
        // левая половина мордочки, правая — зеркальная: лиса ровно по центру
        string[] half =
        {
            "................", "................", "................",
            "......OO........", ".....OOOO.......", ".....OPPOO......", ".....OPPPOO.....", ".....OPPPPOOOOOO",
            ".....OOPPOOOOOOO", "....OOOOOOOOOOOO", "....OOOOOOOOOOOO", "...OOOOOOOOOOOOO", "...OOOOOKKOOOOOO",
            "...OOOOOKKKOOOOO", "...OOOOOKWKOOOOO", "...OOOOOKKKOOOOO", "...WWOOOOOOOOOOO", "...WWWWOOOOOOOOO",
            "....WWWWWWWOOOOO", "....WWWWWWWWWWWW", ".....WWWWWWWWWKK", "......WWWWWWWWKK", ".......WWWWWWWWW",
            "........WWWWWWWW", "..........WWWWWW", "............WWWW",
            "................", "................", "................", "................", "................", "................",
        };
        var art = new string[N];
        for (int i = 0; i < N; i++)
        {
            var row = half[i];
            var rev = row.ToCharArray(); System.Array.Reverse(rev);
            art[i] = row + new string(rev);
        }
        var tex = new Texture2D(N * S, N * S, TextureFormat.RGBA32, false);
        var bg = new Color(0.62f, 0.82f, 0.95f);
        var px = new Color[N * S * N * S];
        float R = N * S * 0.5f;
        for (int y = 0; y < N * S; y++)
            for (int x = 0; x < N * S; x++)
            {
                float dx = x - R + 0.5f, dy = y - R + 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                Color c = d < R - 2 ? Color.Lerp(bg, new Color(0.85f, 0.93f, 1f), Mathf.Clamp01(dy / R * 0.5f + 0.5f)) : new Color(0, 0, 0, 0);
                if (d >= R - 10 && d < R - 2) c = new Color(1f, 1f, 1f, 1f);
                int ax = x / S, ay = N - 1 - y / S - 2; // мордочка по центру круга
                if (ax >= 0 && ax < N && ay >= 0 && ay < N)
                {
                    char ch = art[ay][ax];
                    bool edge = (x % S == 0) || (y % S == 0);
                    Color v = ch == 'O' ? new Color(0.93f, 0.48f, 0.2f) : ch == 'P' ? new Color(0.98f, 0.75f, 0.7f)
                        : ch == 'K' ? new Color(0.15f, 0.12f, 0.12f) : ch == 'W' ? new Color(0.99f, 0.97f, 0.93f) : new Color(0, 0, 0, 0);
                    if (v.a > 0) c = edge ? v * 0.92f : v;
                    c.a = c.a > 0 || v.a > 0 ? 1 : 0;
                }
                px[y * N * S + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(IconPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(IconPath);
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
        AssetDatabase.SaveAssets();
    }

    const string SettingsDir = "Assets/FoxLife/Settings";

    /// <summary>
    /// Настройки URP: Forward+ (много фонарей без потери скорости), тени солнца и фонарей, HDR, сглаживание
    /// и свой полноэкранный проход «миниатюра» после постобработки (старый «антиблик» больше не нужен — в URP бликов нет).
    /// Сама постобработка (свечение, ACES, цвет, виньетка) — объёмом URP, его создаёт игра (PostFX.cs).
    /// </summary>
    [MenuItem("FoxLife/Setup URP")]
    public static void SetupURP()
    {
        Directory.CreateDirectory(SettingsDir);
        string rdPath = SettingsDir + "/FoxRenderer.asset", pPath = SettingsDir + "/FoxURP.asset";
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
        if (rd == null)
        {
            rd = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rd, rdPath);
        }
        rd.renderingMode = RenderingMode.ForwardPlus;
        if (rd.postProcessData == null)
            rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");

        // свои полноэкранные проходы
        foreach (var old in rd.rendererFeatures.ToArray())
            if (old != null && (old.name == "Антиблик" || old.name == "Миниатюра")) { rd.rendererFeatures.Remove(old); Object.DestroyImmediate(old, true); }
        rd.rendererFeatures.RemoveAll(f => f == null);
        Material Mat(string shader, string file)
        {
            string path = SettingsDir + "/" + file + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            else m.shader = Shader.Find(shader);
            return m;
        }
        void Add(string name, Material m, FullScreenPassRendererFeature.InjectionPoint at)
        {
            var f = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            f.name = name;
            f.injectionPoint = at;
            f.passMaterial = m;
            f.fetchColorBuffer = true;
            f.requirements = ScriptableRenderPassInput.Color;
            AssetDatabase.AddObjectToAsset(f, rd);
            rd.rendererFeatures.Add(f);
        }
        Add("Миниатюра", Mat("Hidden/FoxLife/TiltShift", "TiltShift"), FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing);
        EditorUtility.SetDirty(rd);
        AssetDatabase.SaveAssets();
        var so = new SerializedObject(rd);
        var map = so.FindProperty("m_RendererFeatureMap");
        map.arraySize = rd.rendererFeatures.Count;
        for (int i = 0; i < rd.rendererFeatures.Count; i++)
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rd.rendererFeatures[i], out string _, out long id)) map.GetArrayElementAtIndex(i).longValue = id;
        so.ApplyModifiedPropertiesWithoutUndo();

        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pPath);
        if (asset == null)
        {
            asset = UniversalRenderPipelineAsset.Create(rd);
            AssetDatabase.CreateAsset(asset, pPath);
        }
        var ps = new SerializedObject(asset);
        void B(string n, bool v) { var p = ps.FindProperty(n); if (p != null) p.boolValue = v; else Debug.LogWarning("FoxLife: нет поля " + n); }
        void I(string n, int v) { var p = ps.FindProperty(n); if (p != null) p.intValue = v; else Debug.LogWarning("FoxLife: нет поля " + n); }
        void F(string n, float v) { var p = ps.FindProperty(n); if (p != null) p.floatValue = v; else Debug.LogWarning("FoxLife: нет поля " + n); }
        B("m_SupportsHDR", true);
        I("m_MSAA", 4);
        B("m_MainLightShadowsSupported", true);
        I("m_MainLightShadowmapResolution", 4096);
        I("m_AdditionalLightsRenderingMode", 1); // попиксельно
        I("m_AdditionalLightsPerObjectLimit", 8);
        B("m_AdditionalLightShadowsSupported", true);
        I("m_AdditionalLightsShadowmapResolution", 2048);
        // чёткие тени — до ~36 м от камеры (раньше ~13 м), дальше — попроще, всего до 60 м
        F("m_ShadowDistance", 60f);
        I("m_ShadowCascadeCount", 2);
        F("m_Cascade2Split", 0.6f);
        // небольшое смещение по нормали: тонкие воксельные ступеньки крыш не «худеют» в тени и не дают полос на полу
        F("m_ShadowDepthBias", 1.2f);
        F("m_ShadowNormalBias", 0.1f); // большой сдвиг по нормали «сжимает» вокселы и открывает щёлки света на рёбрах
        B("m_SoftShadowsSupported", true);
        ps.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);

        GraphicsSettings.defaultRenderPipeline = asset;
        int cur = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(cur, false);
        AssetDatabase.SaveAssets();
        Debug.Log("FoxLife: URP настроен (" + pPath + ")");
    }

    [MenuItem("FoxLife/Build Windows")]
    public static void BuildWindows()
    {
        if (!File.Exists(ScenePath)) Run();
        SetupURP();
        // туман включён в сцене — иначе Unity выбросит варианты шейдеров с туманом и погода «Туман» не будет видна
        var sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.004f;
        EditorSceneManager.MarkSceneDirty(sc);
        EditorSceneManager.SaveScene(sc);
        MakeIcon();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/FoxLife/FoxLife.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log("FoxLife: build result " + report.summary.result + ", errors: " + report.summary.totalErrors);
    }
}
