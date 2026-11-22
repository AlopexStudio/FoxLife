using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Типы сетевых сообщений (первый байт сообщения).</summary>
    public enum Msg : byte
    {
        Hello = 1, Welcome, Refused, World, Ready, Claim, Claimed,
        Tick, Roster, FoxFull, FoxStyle,
        Furn, FurnDel, NidMap, LotState, Terrain, Prop,
        Cmd, Op, Settings, Money, Log, Open, Sfx, Bye,
        FoxEdit, Chat, Ping,
    }

    /// <summary>
    /// Одно соединение по TCP. Сообщение = длина (4 байта) + тип + данные. Чтение и запись — в своих потоках,
    /// игра забирает пришедшее из <see cref="inbox"/> в главном потоке.
    /// </summary>
    public class NetLink
    {
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>();
        public readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
        public volatile bool closed;
        public volatile string error;
        public long received, sent;

        // ----- игрок на стороне хоста -----
        public int id;
        public string name = "Друг";
        public int lot = -1;            // за какую семью играет (-1 — ещё выбирает)
        public bool worldSent;          // мир отправлен: дальше ему идут все изменения
        public int vote = 1;            // за какую скорость времени
        public bool autonomy = true, autoOutfits = true;
        public int moneySeq;
        public float lastHeard = Time.unscaledTime; // когда от него что-то приходило (молчит долго — связь пропала)
        public string selected;                     // за какую лисичку сейчас играет
        /// <summary>Временные номера мебели, которую поставил игрок, → настоящие номера у хоста.</summary>
        public readonly Dictionary<int, int> tempNid = new Dictionary<int, int>();
        // что уже отправлено (дальше шлём только изменения)
        public readonly Dictionary<Fox, ulong> sentAct = new Dictionary<Fox, ulong>();
        public readonly Dictionary<Fox, string> sentBubble = new Dictionary<Fox, string>();
        public readonly Dictionary<string, string> sentFull = new Dictionary<string, string>();
        public List<string> sentRoster;

        public NetLink(TcpClient c)
        {
            tcp = c;
            tcp.NoDelay = true;
            tcp.SendBufferSize = 1 << 20;
            tcp.ReceiveBufferSize = 1 << 20;
            stream = tcp.GetStream();
            new Thread(ReadLoop) { IsBackground = true, Name = "FoxNet read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "FoxNet write" }.Start();
        }

        public string Address { get { try { return ((IPEndPoint)tcp.Client.RemoteEndPoint).Address.ToString(); } catch { return "?"; } } }

        void ReadLoop()
        {
            try
            {
                var br = new BinaryReader(stream);
                while (!closed)
                {
                    int len = br.ReadInt32();
                    if (len <= 0 || len > 256 << 20) throw new IOException("неверная длина сообщения: " + len);
                    var buf = br.ReadBytes(len);
                    if (buf.Length < len) throw new EndOfStreamException();
                    received += len + 4;
                    inbox.Enqueue(buf);
                }
            }
            catch (Exception e) { if (!closed) error = e.Message; }
            closed = true;
        }

        void WriteLoop()
        {
            try
            {
                foreach (var m in outbox.GetConsumingEnumerable())
                {
                    var len = BitConverter.GetBytes(m.Length);
                    stream.Write(len, 0, 4);
                    stream.Write(m, 0, m.Length);
                    sent += m.Length + 4;
                }
            }
            catch (Exception e) { if (!closed) error = e.Message; }
            closed = true;
        }

        public void Send(byte[] m)
        {
            if (closed || m == null) return;
            try { outbox.Add(m); } catch { closed = true; }
        }

        public int Pending => outbox.Count;

        public void Close()
        {
            if (closed) { try { tcp.Close(); } catch { } return; }
            // дать писателю дослать последнее сообщение (например, «пока»)
            try { outbox.CompleteAdding(); } catch { }
            var t = new Thread(() => { Thread.Sleep(300); closed = true; try { tcp.Close(); } catch { } }) { IsBackground = true };
            t.Start();
        }
    }

    /// <summary>
    /// Игра по сети (по локальной сети или через Radmin VPN). Хост ведёт весь мир: время, лисичек, деньги.
    /// Друзья получают мир целиком при входе, дальше — поток положений и занятий лисичек, а свои приказы,
    /// стройку и покраску отправляют хосту. У каждого — своя семья в общем городе.
    /// </summary>
    public partial class Net : MonoBehaviour
    {
        public static Net I;
        public const int DefaultPort = 7777;
        public const int Protocol = 9;
        public enum Role { None, Host, Client }
        public Role role;

        public static bool Active => I != null && I.role != Role.None;
        public static bool IsHost => I != null && I.role == Role.Host;
        public static bool IsClient => I != null && I.role == Role.Client;

        /// <summary>Сейчас применяются данные, пришедшие по сети: свои изменения обратно не рассылаем.</summary>
        public static bool Applying;
        /// <summary>У хоста: от какого игрока пришли применяемые данные (ему их не возвращаем).</summary>
        public static int From = -1;

        static Game G => Game.I;
        public string status = "";
        public float statusTime;
        public string myName = "Лисичка";

        public static Net Ensure()
        {
            if (I == null) I = Game.I.gameObject.AddComponent<Net>();
            return I;
        }

        void SetStatus(string s) { status = s; statusTime = Time.unscaledTime; Debug.Log("FoxLife: сеть: " + s); }

        // ================= упаковка =================

        public static byte[] Pack(Msg t, Action<BinaryWriter> body)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write((byte)t);
            body?.Invoke(w);
            w.Flush();
            return ms.ToArray();
        }

        public static byte[] Gzip(string s)
        {
            var raw = Encoding.UTF8.GetBytes(s);
            var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) gz.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        public static string Gunzip(byte[] b)
        {
            using (var gz = new GZipStream(new MemoryStream(b), CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                gz.CopyTo(o);
                return Encoding.UTF8.GetString(o.ToArray());
            }
        }

        static void WStr(BinaryWriter w, string s) => w.Write(s ?? "");
        static string RStr(BinaryReader r) { var s = r.ReadString(); return s.Length == 0 ? null : s; }
        static void WVec(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 RVec(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        /// <summary>Адреса этого компьютера (IPv4): их называют другу. 26.x.x.x — адрес в Radmin VPN.</summary>
        public static List<string> LocalAddresses()
        {
            var l = new List<string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                            l.Add(a.Address.ToString());
                }
            }
            catch (Exception e) { Debug.LogWarning("FoxLife: адреса: " + e.Message); }
            l.Sort((a, b) => (b.StartsWith("26.") ? 1 : 0).CompareTo(a.StartsWith("26.") ? 1 : 0));
            return l;
        }

        // ================= хост =================

        TcpListener listener;
        readonly ConcurrentQueue<TcpClient> accepted = new ConcurrentQueue<TcpClient>();
        public readonly List<NetLink> peers = new List<NetLink>();
        int nextPeerId = 1;
        public int port = DefaultPort;
        public int nidCounter;

        public bool StartHost(int p, out string err)
        {
            err = null;
            if (role != Role.None) Stop(null);
            try
            {
                listener = new TcpListener(IPAddress.Any, p);
                listener.Start();
            }
            catch (Exception e) { err = "Не получилось открыть порт " + p + ": " + e.Message; listener = null; return false; }
            port = p;
            role = Role.Host;
            ResetSync();
            chat.Clear();
            StartBeacon();
            // все предметы получают сетевые номера заново
            nidCounter = 0;
            foreach (var f in G.furniture) if (f != null) f.nid = ++nidCounter;
            byNid.Clear();
            var l = listener;
            new Thread(() =>
            {
                try { while (true) { var c = l.AcceptTcpClient(); accepted.Enqueue(c); } }
                catch { }
            }) { IsBackground = true, Name = "FoxNet accept" }.Start();
            SetStatus("Игра открыта для друзей, порт " + p);
            G.Log("Игра открыта для друзей по сети.");
            return true;
        }

        /// <summary>Игрок, который играет за эту семью (у хоста), или null.</summary>
        public NetLink PeerOf(int lot)
        {
            if (lot < 0) return null;
            foreach (var p in peers) if (p.lot == lot && !p.closed) return p;
            return null;
        }

        public static bool RemoteOwned(Fox f) => IsHost && f != null && I.PeerOf(f.home) != null;

        /// <summary>Участки, за которые кто-то играет (у хоста — свой и друзей; у клиента — со слов хоста).</summary>
        public List<int> TakenLots()
        {
            if (role == Role.Client) return takenLots;
            var l = new List<int> { House.PlayerLotId };
            foreach (var p in peers) if (p.lot >= 0 && !p.closed) l.Add(p.lot);
            return l;
        }

        public string OwnerName(int lot)
        {
            if (role == Role.Host)
            {
                if (lot == House.PlayerLotId) return myName;
                var p = PeerOf(lot);
                return p != null ? p.name : null;
            }
            int i = takenLots.IndexOf(lot);
            return i >= 0 && i < takenNames.Count ? takenNames[i] : null;
        }

        void Broadcast(byte[] m, int except = -1)
        {
            foreach (var p in peers) if (p.worldSent && p.id != except) p.Send(m);
        }

        /// <summary>Отправить: клиент — хосту, хост — всем (кроме того, от кого пришло).</summary>
        void SendOut(byte[] m, int origin)
        {
            if (role == Role.Client) { if (server != null) server.Send(m); }
            else if (role == Role.Host) Broadcast(m, origin);
        }

        // ================= клиент =================

        public NetLink server;
        public string joinAddress;
        volatile TcpClient connected;
        volatile string connectError;
        public bool connecting, loadingWorld, worldReady;
        public int myId;
        public int myLot = -1;
        public string hostName = "";
        public readonly List<int> takenLots = new List<int>();
        public readonly List<string> takenNames = new List<string>();
        public bool NeedsPicker => role == Role.Client && worldReady && myLot < 0;
        public float worldBytes;

        public void StartClient(string address, int p, string name)
        {
            if (role != Role.None) Stop(null);
            role = Role.Client;
            ResetSync();
            chat.Clear();
            myName = string.IsNullOrEmpty(name) ? "Друг" : name;
            joinAddress = address;
            port = p;
            reconnecting = false; rejoinLot = -1;
            connecting = true;
            worldReady = false;
            loadingWorld = false;
            myLot = -1;
            connected = null; connectError = null;
            SetStatus("Подключаемся к " + address + "…");
            BeginConnect(address, p);
        }

        void BeginConnect(string address, int p)
        {
            connecting = true;
            connected = null; connectError = null;
            new Thread(() =>
            {
                try
                {
                    var c = new TcpClient();
                    var ar = c.BeginConnect(address, p, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(8))) { c.Close(); connectError = "Нет ответа. Проверь адрес и что друг открыл игру."; return; }
                    c.EndConnect(ar);
                    connected = c;
                }
                catch (Exception e) { connectError = "Не удалось подключиться: " + e.Message; }
            }) { IsBackground = true, Name = "FoxNet connect" }.Start();
        }

        /// <summary>Закончить сетевую игру. reason — что сказать (null — молча).</summary>
        public void Stop(string reason)
        {
            var was = role;
            if (was == Role.Host)
            {
                var bye = Pack(Msg.Bye, w => WStr(w, reason ?? "Хозяин закрыл игру."));
                foreach (var p in peers) { p.Send(bye); p.Close(); }
                peers.Clear();
                try { listener?.Stop(); } catch { }
                StopBeacon();
                listener = null;
                while (accepted.TryDequeue(out var c)) { try { c.Close(); } catch { } }
            }
            if (was == Role.Client && server != null)
            {
                server.Send(Pack(Msg.Bye, w => WStr(w, "")));
                server.Close();
                server = null;
            }
            bool replaced = worldReady || loadingWorld || reconnecting; // город друга уже (или почти) загружен вместо своего
            reconnecting = false; rejoinLot = -1;
            role = Role.None;
            connecting = false;
            worldReady = false;
            loadingWorld = false;
            myLot = -1;
            Applying = false;
            From = -1;
            if (G != null) G.speedIndex = G.wantSpeed;
            if (reason != null) SetStatus(reason);
            if (was == Role.Client && G != null && replaced) G.ReturnToTitle(reason);
        }

        void OnApplicationQuit() { if (role != Role.None) Stop(null); }

        // ================= главный цикл =================

        void Update()
        {
            if (G == null) return;
            try
            {
                FinderUpdate();
                if (role == Role.Host) { BeaconUpdate(); HostUpdate(); }
                else if (role == Role.Client) ClientUpdate();
            }
            catch (Exception e) { Debug.LogError("FoxLife: сеть: " + e); Applying = false; From = -1; }
        }

        void LateUpdate()
        {
            if (G == null || role == Role.None) return;
            try { FlushChanges(); }
            catch (Exception e) { Debug.LogError("FoxLife: сеть (отправка): " + e); }
        }
    }
}
