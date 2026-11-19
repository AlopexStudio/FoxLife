using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        PlayerSettings.productName = "Лисья жизнь";
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

    [MenuItem("FoxLife/Build Windows")]
    public static void BuildWindows()
    {
        if (!File.Exists(ScenePath)) Run();
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
