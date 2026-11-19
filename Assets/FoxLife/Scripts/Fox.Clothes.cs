using System.Collections.Generic;
using UnityEngine;

namespace FoxLife
{
    /// <summary>
    /// Одежда лисички: три наряда (повседневный, пижама, дождевик). Лисичка переодевается сама:
    /// ко сну — в пижаму, под дождём на улице — в дождевик. Каждую вещь можно перекрасить «Созданием стиля».
    /// </summary>
    public partial class Fox
    {
        public Outfit[] outfits;
        public int wornOutfit = -1;
        readonly List<(OutfitItem item, Styleable st)> worn = new List<(OutfitItem, Styleable)>();

        Transform Anchor(ClothSlot s) => s == ClothSlot.Head || s == ClothSlot.Eyes ? head : model;

        int DesiredOutfit
        {
            get
            {
                if (G.ui != null && ((G.ui.clothesOpen && G.mode == Mode.Live && G.selected == this) || (G.mode == Mode.Town && G.ui.townEditing && G.ui.townEditFox == this))) return G.ui.outfitTab;
                if (IsPlayer && !G.autoOutfits) return wornOutfit < 0 ? 0 : wornOutfit;
                if (sleeping) return 1;
                if (G.weather != null && G.weather.Raining && Level == 0 && G.house.RoomAt(0, Cell) == null) return 2;
                return 0;
            }
        }

        void UpdateClothes()
        {
            if (head == null) return;
            if (outfits == null || outfits.Length != 3) outfits = Wardrobe.Defaults(this);
            int d = DesiredOutfit;
            if (d != wornOutfit) Wear(d);
        }

        bool undressed;

        /// <summary>В ванне одежда снимается полностью (и надевается обратно после).</summary>
        void BathUndress(bool bath)
        {
            if (bath == undressed) return;
            undressed = bath;
            foreach (var w in worn)
                if (w.st != null)
                    foreach (var r in w.st.GetComponentsInChildren<Renderer>(true)) if (!r.forceRenderingOff) r.enabled = !bath && !hiddenByLevel;
        }

        /// <summary>Снять текущий наряд (запомнив цвета) и надеть другой.</summary>
        public void Wear(int i)
        {
            SyncWorn();
            foreach (var w in worn) if (w.st != null) Destroy(w.st.gameObject);
            worn.Clear();
            wornOutfit = i;
            if (outfits == null || i < 0 || i >= outfits.Length) return;
            foreach (var item in outfits[i].items)
            {
                var def = Wardrobe.Get(item.id);
                if (def == null) continue;
                var go = new GameObject("Одежда: " + def.name);
                go.transform.SetParent(Anchor(def.slot), false);
                var st = go.AddComponent<Styleable>();
                st.kind = SurfaceKind.Object;
                st.depthLayer = def.slot == ClothSlot.Body ? 1f : def.slot == ClothSlot.Neck ? 2.2f : def.slot == ClothSlot.Head ? 2f : 3f;
                st.displayName = foxName + ": " + def.name.ToLower();
                for (int k = 0; k < def.parts.Length; k++)
                {
                    var s = k < item.styles.Count && item.styles[k] != null ? item.styles[k] : def.parts[k].style.Clone();
                    st.AddPart(def.parts[k].name, s, def.glassLast && k == def.parts.Length - 1);
                }
                def.build(st, go.transform);
                foreach (var p in st.parts) p.mat.SetFloat("_Voxel", 0.03f);
                worn.Add((item, st));
            }
            if (hiddenByLevel) foreach (var w in worn) foreach (var r in w.st.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        /// <summary>Перенести цвета надетых вещей обратно в наряд (после «Создания стиля»).</summary>
        public void SyncWorn()
        {
            foreach (var w in worn)
            {
                if (w.st == null) continue;
                for (int k = 0; k < w.st.parts.Count; k++)
                {
                    while (w.item.styles.Count <= k) w.item.styles.Add(null);
                    w.item.styles[k] = w.st.Get(k).Clone();
                }
            }
        }

        /// <summary>Новый повседневный наряд: другие вещи и цвета.</summary>
        public void NewLook()
        {
            SyncWorn();
            var fresh = Wardrobe.Defaults(this, Random.Range(0, 1000000));
            if (outfits == null) outfits = fresh;
            else outfits[0] = fresh[0];
            if (wornOutfit == 0) { foreach (var w in worn) if (w.st != null) Destroy(w.st.gameObject); worn.Clear(); wornOutfit = -1; }
            OnEvent(new GameEvent { kind = "style" });
        }

        /// <summary>Положить вещь в наряд на это место (или убрать, если id = null).</summary>
        public void SetItem(int outfit, ClothSlot slot, string id)
        {
            if (outfits == null) outfits = Wardrobe.Defaults(this);
            SyncWorn();
            var o = outfits[outfit];
            o.items.RemoveAll(i => Wardrobe.Get(i.id) == null || Wardrobe.Get(i.id).slot == slot);
            if (id != null) o.items.Add(Wardrobe.Item(id));
            if (wornOutfit == outfit) { worn.ForEach(w => { if (w.st != null) Destroy(w.st.gameObject); }); worn.Clear(); wornOutfit = -1; }
        }
    }
}
