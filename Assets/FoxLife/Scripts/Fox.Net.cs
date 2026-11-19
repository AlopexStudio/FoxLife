using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Лисичка у второго игрока (клиента): сама ничего не решает, а повторяет то, что присылает хост —
    /// положение (плавно, между снимками), позу, занятие, очередь, предмет во рту и наряд.
    /// </summary>
    public partial class Fox
    {
        /// <summary>Сколько раз лисичка что-то сказала (по сети шлём только новые фразы).</summary>
        public int sayCount;
        /// <summary>Какой наряд на ней у хоста (−1 — не знаем).</summary>
        public int netWorn = -1;
        public string netStyleJson, netOutfitJson;

        Vector3 netFrom, netTo;
        Quaternion netRotFrom, netRotTo;
        float netT, netDur = 0.1f, netLast;
        bool netHas;

        public void NetSnap(Vector3 pos, float yaw, int fl, Pose p, float[] nd)
        {
            float now = Time.unscaledTime;
            var rot = Quaternion.Euler(0, yaw, 0);
            if (!netHas || (pos - transform.position).sqrMagnitude > 16f)
            {
                transform.position = pos;
                transform.rotation = rot;
                netFrom = netTo = pos;
                netRotFrom = netRotTo = rot;
                netHas = true;
            }
            else
            {
                netFrom = transform.position;
                netRotFrom = transform.rotation;
                netTo = pos;
                netRotTo = rot;
            }
            netDur = Mathf.Clamp(now - netLast, 0.05f, 0.35f);
            netLast = now;
            netT = 0;
            walking = (fl & 1) != 0;
            running = (fl & 2) != 0;
            sleeping = (fl & 4) != 0;
            carried = (fl & 8) != 0;
            atWork = (fl & 16) != 0;
            pose = p;
            for (int i = 0; i < 5 && i < needs.Length; i++) needs[i] = nd[i];
        }

        void NetStep()
        {
            if (!netHas) return;
            netT += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(netT / netDur);
            transform.position = Vector3.Lerp(netFrom, netTo, a);
            transform.rotation = Quaternion.Slerp(netRotFrom, netRotTo, a);
        }

        public void NetAction(FoxAction cur, List<FoxAction> q, string held, bool work, Fox sideWith, int worn)
        {
            current = cur;
            queue.Clear();
            queue.AddRange(q);
            if (held != heldKind) { if (string.IsNullOrEmpty(held)) Drop(); else Hold(held); }
            atWork = work;
            side = sideWith != null ? new SideTalk { other = sideWith } : null;
            netWorn = worn;
        }
    }
}
