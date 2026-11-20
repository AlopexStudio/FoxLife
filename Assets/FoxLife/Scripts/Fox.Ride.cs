using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Личный транспорт: роликовые коньки и скейтборд — отдельная категория одежды («Транспорт»), надевается
    /// и снимается через шкаф. На улице лисичка катится заметно быстрее и двигается по-другому; в помещении
    /// транспорт убирается (не виден и не ускоряет).
    /// </summary>
    public partial class Fox
    {
        public Transform[] Legs => new[] { legFL, legFR, legBL, legBR };

        /// <summary>Детали транспорта на лапах (ролики): живут вне объекта вещи, убираются вместе с нарядом.</summary>
        public readonly System.Collections.Generic.List<GameObject> rideBits = new System.Collections.Generic.List<GameObject>();

        public Transform RideHolder(Transform leg)
        {
            var h = new GameObject("ролик").transform;
            h.SetParent(leg, false);
            rideBits.Add(h.gameObject);
            return h;
        }

        /// <summary>Что из транспорта надето в текущем наряде (или null).</summary>
        public string RideId
        {
            get
            {
                if (outfits == null || wornOutfit < 0 || wornOutfit >= outfits.Length) return null;
                var it = outfits[wornOutfit].Get(ClothSlot.Ride);
                return it != null ? it.id : null;
            }
        }

        /// <summary>Катится прямо сейчас: идёт, на улице, на первом этаже, транспорт надет.</summary>
        public bool Riding => walking && !carried && RideId != null && Level == 0 && G.house != null && G.house.RoomAt(0, Cell) == null;

        float RideMul => RideId == "skateboard" ? 2.15f : 1.85f;

        /// <summary>Множитель скорости ходьбы: транспорт, бег или обычный шаг.</summary>
        float MoveMul => Riding ? RideMul : running ? RunMul : 1f;

        bool rideShown = true;

        /// <summary>Транспорт виден только пока лисичка катится; дома — убран.</summary>
        void UpdateRideVisual()
        {
            bool show = Riding && !hiddenByLevel;
            if (show == rideShown) return;
            rideShown = show;
            foreach (var w in worn)
            {
                if (w.st == null) continue;
                var def = Wardrobe.Get(w.item.id);
                if (def == null || def.slot != ClothSlot.Ride) continue;
                w.st.gameObject.SetActive(show);
            }
            foreach (var b in rideBits) if (b != null) b.SetActive(show);
        }

        /// <summary>Анимация катания: на скейте — стоит и иногда отталкивается, на роликах — широкие скользящие шаги.</summary>
        bool AnimateRide(ref Vector3 basePos, ref Vector3 tPos, ref Quaternion tRot, ref float swing, ref float frontComp, ref float hindFold,
            ref float tailPitch, ref float tailYaw, ref float headPitch)
        {
            if (!Riding) return false;
            if (RideId == "skateboard")
            {
                basePos.y += 0.09f;
                float push = Mathf.Max(0, Mathf.Sin(animT * 2.6f));
                frontComp = push * push * 24f;
                hindFold = -4f;
                swing = 0;
                tRot = Quaternion.Euler(-3f, 0, Mathf.Sin(animT * 1.3f) * 4f);
                tPos.y = Mathf.Sin(animT * 2.6f) * 0.01f;
                tailPitch = 26f;
                tailYaw = Mathf.Sin(animT * 2f) * 10f;
                headPitch = -6f;
            }
            else
            {
                basePos.y += 0.07f;
                float g = animT * 5.5f;
                swing = Mathf.Sin(g) * 26f;
                hindFold = -6f;
                tRot = Quaternion.Euler(-7f, Mathf.Sin(g) * 5f, Mathf.Sin(g) * 5f);
                tPos.x = Mathf.Sin(g) * 0.025f;
                tPos.y = Mathf.Abs(Mathf.Sin(g)) * 0.015f;
                tailPitch = 22f;
                tailYaw = Mathf.Sin(g) * 14f;
                headPitch = -4f;
            }
            return true;
        }
    }
}
