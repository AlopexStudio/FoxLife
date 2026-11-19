using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    public enum EyeState { Open, Closed, Happy, Sad, Love, Surprised }

    /// <summary>Воксельные глаза: эмоции передаются формой глаз из нескольких вокселов.</summary>
    public partial class Fox
    {
        // строки сверху вниз; D — тёмный, W — блик, L — сердечко. Правый глаз — зеркальный.
        static readonly Dictionary<EyeState, string[]> EyeShapes = new Dictionary<EyeState, string[]>
        {
            { EyeState.Open, new[] { "WD", "DD", "DD" } },
            { EyeState.Closed, new[] { "DDD" } },
            { EyeState.Happy, new[] { ".D.", "D.D" } },
            { EyeState.Sad, new[] { "D..", ".DD" } },
            { EyeState.Love, new[] { "L.L", "LLL", ".L." } },
            { EyeState.Surprised, new[] { "DD", "DD", "DD", "DD" } },
        };

        const float EyeVox = 0.03f;
        readonly GameObject[,] eyeMeshes = new GameObject[2, 6];
        EyeState eyeState = (EyeState)(-1);
        static Material loveMat;

        void BuildEyes()
        {
            if (loveMat == null)
            {
                loveMat = MaterialFactory.Create();
                MaterialFactory.Apply(loveMat, StyleData.Make("Гладкий", "#F2668B", null, 1, 0.5f));
                loveMat.SetFloat("_Voxel", EyeVox);
            }
            var mats = new[] { style.parts[3].mat, style.parts[1].mat, loveMat };
            for (int side = 0; side < 2; side++)
            {
                var pivot = side == 0 ? eyeL : eyeR;
                foreach (var kv in EyeShapes)
                {
                    var rows = kv.Value;
                    int h = rows.Length, w = rows[0].Length;
                    var cells = new byte[w * h];
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            char ch = rows[h - 1 - y][side == 0 ? x : w - 1 - x];
                            cells[y * w + x] = ch == 'D' ? (byte)1 : ch == 'W' ? (byte)2 : ch == 'L' ? (byte)3 : (byte)0;
                        }
                    var mesh = Voxelizer.BuildMesh(cells, w, h, 1, EyeVox, new Vector3(-w * EyeVox / 2f, -h * EyeVox / 2f, -EyeVox / 2f), 3);
                    var go = new GameObject(kv.Key.ToString());
                    go.transform.SetParent(pivot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.SetActive(false);
                    eyeMeshes[side, (int)kv.Key] = go;
                }
            }
            SetEyes(EyeState.Open);
        }

        void SetEyes(EyeState s)
        {
            if (s == eyeState || eyeMeshes[0, 0] == null) return;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 6; i++)
                    if (eyeMeshes[side, i] != null) eyeMeshes[side, i].SetActive(i == (int)s);
            eyeState = s;
        }

        static readonly string[] LoveMoodlets = { "Поцелуй", "Взаимная любовь", "Флирт", "Свадьба!", "Обнимашки" };

        EyeState Emotion()
        {
            if (bubbleTimer > 0 && (bubble == "Только не это!" || bubble == "Подгорело!" || bubble == "Обидно!")) return EyeState.Surprised;
            foreach (var m in moodlets)
                foreach (var l in LoveMoodlets)
                    if (m.name == l && m.minutesLeft > 60) return EyeState.Love;
            float mood = Mood;
            if (pose == Pose.Talk && mood > 5) return EyeState.Happy;
            if (bubbleTimer > 0 && (bubble == "Ура!" || bubble == "Уии!")) return EyeState.Happy;
            if (mood > 45) return EyeState.Happy;
            if (mood < -25) return EyeState.Sad;
            return EyeState.Open;
        }
    }
}
