using UnityEngine;

namespace FoxLife
{
    /// <summary>Камера: ПКМ — вращение, колесо — зум, СКМ/WASD — перемещение, Q/E — поворот.</summary>
    public class CameraRig : MonoBehaviour
    {
        public Vector3 target = new Vector3(7, 0, 5);
        public float yaw = 0, pitch = 48, dist = 15;
        Vector3 targetS;
        float yawS, pitchS, distS;
        Vector3 lastMouse;
        bool rotating, panning;
        public bool edgeScroll = true;
        public float minPitch = 18f;
        public float minDist = 3.5f;   // автотест подлетает ближе

        /// <summary>Сразу встать в нужное место (без плавного подлёта).</summary>
        public void Snap() { targetS = target; yawS = yaw; pitchS = pitch; distS = dist; }
        Transform follow;

        void Start()
        {
            targetS = target; yawS = yaw; pitchS = pitch; distS = dist;
        }

        public void Focus(Vector3 p, float maxDist = 9f)
        {
            target = new Vector3(p.x, target.y, p.z);
            dist = Mathf.Min(dist, maxDist);
            follow = null;
        }

        public void Follow(Transform t) => follow = t;
        public Transform Following => follow;

        public SaveCamera Capture(House house, System.Collections.Generic.List<Fox> foxes)
        {
            var followed = follow != null ? foxes.Find(f => f != null && f.transform == follow) : null;
            return new SaveCamera
            {
                captured = true, target = targetS, yaw = yawS, pitch = pitchS, distance = distS,
                floor = house.viewLevel, wallMode = (int)house.wallMode, followId = followed?.id
            };
        }

        public void Restore(Game game, SaveCamera saved)
        {
            rotating = panning = false;
            Follow(null);
            if (saved != null && saved.captured && Valid(saved.target.x) && Valid(saved.target.y) && Valid(saved.target.z) &&
                Valid(saved.yaw) && Valid(saved.pitch) && Valid(saved.distance) && saved.distance > 0)
            {
                target = saved.target;
                yaw = saved.yaw;
                pitch = Mathf.Clamp(saved.pitch, minPitch, 85f);
                dist = Mathf.Clamp(saved.distance, minDist, 48f);
                game.house.wallMode = (House.WallMode)Mathf.Clamp(saved.wallMode, 0, 2);
                game.SetViewLevel(saved.floor);
                var followed = Fox.FoxById(saved.followId);
                if (followed != null) Follow(followed.transform);
            }
            else
            {
                var selected = game.selected;
                Vector3 center = selected != null ? selected.transform.position : GridMap.Center(game.house.EntryCell);
                int floor = selected != null ? selected.Level : 0;
                game.SetViewLevel(floor);
                target = new Vector3(center.x, game.house.GroundUnder(center) + floor * House.WallH, center.z);
                yaw = 0f; pitch = 48f; dist = 10f;
            }
            Snap();
            ApplyPose(game);
        }

        static bool Valid(float n) => !float.IsNaN(n) && !float.IsInfinity(n);

        void LateUpdate()
        {
            var game = Game.I;
            bool overUI = game != null && game.ui != null && game.ui.MouseOverUI;
            bool inMenu = game != null && game.ui != null && game.ui.screen != GameUI.MenuScreen.None;
            bool typing = inMenu || (game != null && game.ui != null && game.ui.Typing);
            if (game != null && game.TitleSceneActive && inMenu) { rotating = panning = false; }
            float dt = Time.unscaledDeltaTime;
            Vector3 m = Input.mousePosition;
            Vector3 dm = m - lastMouse;
            lastMouse = m;

            if (Input.GetMouseButtonDown(1) && !overUI) rotating = true;
            if (!Input.GetMouseButton(1)) rotating = false;
            if (Input.GetMouseButtonDown(2) && !overUI) panning = true;
            if (!Input.GetMouseButton(2)) panning = false;

            if (rotating)
            {
                yaw += dm.x * 0.25f;
                pitch -= dm.y * 0.2f;
            }

            Quaternion yawRot = Quaternion.Euler(0, yaw, 0);
            Vector3 right = yawRot * Vector3.right, fwd = yawRot * Vector3.forward;
            float panSpeed = dist * 0.9f;

            if (panning)
            {
                target -= (right * dm.x + fwd * dm.y) * dist * 0.0018f;
                follow = null;
            }

            // с зажатым Ctrl клавиши — это сочетания (Ctrl+Z — отмена), а не движение камеры
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand);
            if (!typing && !ctrl)
            {
                Vector3 mv = Vector3.zero;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) mv += fwd;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) mv -= fwd;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) mv += right;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) mv -= right;
                // прокрутка, когда курсор у края окна
                if (edgeScroll && Application.isFocused && !rotating && !panning &&
                    m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height)
                {
                    float edge = Mathf.Max(6f, Screen.height * 0.008f);
                    if (m.x <= edge) mv -= right;
                    else if (m.x >= Screen.width - 1 - edge) mv += right;
                    if (m.y <= edge) mv -= fwd;
                    else if (m.y >= Screen.height - 1 - edge) mv += fwd;
                }
                if (mv != Vector3.zero) { target += mv.normalized * panSpeed * dt; follow = null; }
                if (Input.GetKey(KeyCode.Q)) yaw += 90f * dt;
                if (Input.GetKey(KeyCode.E)) yaw -= 90f * dt;
                if (Input.GetKey(KeyCode.Z)) dist *= 1f + dt;
                if (Input.GetKey(KeyCode.X)) dist *= 1f - dt;
            }

            if (!overUI)
            {
                float sc = Input.mouseScrollDelta.y;
                if (Mathf.Abs(sc) > 0.01f) dist *= 1f - sc * 0.1f;
            }

            if (follow != null) target = new Vector3(follow.position.x, target.y, follow.position.z);

            pitch = Mathf.Clamp(pitch, minPitch, 85f);
            dist = Mathf.Clamp(dist, minDist, 48f);
            target.x = Mathf.Clamp(target.x, Town.MinX - 6f, Town.MinX + Town.W + 6f);
            target.z = Mathf.Clamp(target.z, Town.MinZ - 6f, Town.MinZ + Town.H + 6f);
            // камера держится над землёй: в горах поднимается, в низинах опускается
            if (Game.I != null && Game.I.house != null && Game.I.house.terrain != null)
            {
                float ty = Game.I.house.GroundUnder(target) + (Game.I.house.viewLevel * House.WallH);
                target.y = Mathf.Abs(target.y - ty) < 0.01f ? ty : Mathf.Lerp(target.y, ty, 1f - Mathf.Exp(-3f * dt));
            }

            float k = 1f - Mathf.Exp(-10f * dt);
            targetS = Vector3.Lerp(targetS, target, k);
            yawS = Mathf.LerpAngle(yawS, yaw, k);
            pitchS = Mathf.Lerp(pitchS, pitch, k);
            distS = Mathf.Lerp(distS, dist, k);
            // Хвост плавной доводки — сдвиги меньше пикселя ещё секунды две, от них дрожат края вокселей
            // и тени. Когда до цели совсем чуть-чуть, встаём ровно в неё.
            float eps = Mathf.Max(0.002f, dist * 0.0015f);
            if ((targetS - target).sqrMagnitude < eps * eps) targetS = target;
            if (Mathf.Abs(Mathf.DeltaAngle(yawS, yaw)) < 0.08f) yawS = yaw;
            if (Mathf.Abs(pitchS - pitch) < 0.08f) pitchS = pitch;
            if (Mathf.Abs(distS - dist) < eps) distS = dist;

            ApplyPose(game);
        }

        void ApplyPose(Game game)
        {
            var rot = Quaternion.Euler(pitchS, yawS, 0);
            var pos = targetS + Vector3.up * 0.4f + rot * new Vector3(0, 0, -distS);
            // камера не уходит под землю на склонах
            if (game != null && game.house != null && game.house.terrain != null)
            {
                float gy = game.house.terrain.GroundAt(pos.x, pos.z) + 1.2f;
                if (pos.y < gy) { pos.y = gy; transform.position = pos; transform.LookAt(targetS + Vector3.up * 0.4f); return; }
            }
            transform.position = pos;
            transform.rotation = rot;
        }
    }
}
