using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace FoxLife
{
    /// <summary>Игра в локальной сети (или в Radmin VPN), которую нашёл поиск.</summary>
    public class LanGame
    {
        public string token, address, town, host;
        public int port, players;
        public float seen;
    }

    /// <summary>Строчка чата.</summary>
    public class ChatLine
    {
        public string who, text;
        public float time;
        public bool mine;
    }

    public partial class Net
    {
        // ================= поиск игр в сети =================
        // Друг рассылает широковещательный вопрос «FOXLIFE?» на порт 7778 во все свои сети (включая Radmin VPN),
        // открытая игра отвечает «FOXLIFE!|…» прямо ему. Хозяину ничего настраивать не нужно.

        public const int DiscoveryPort = 7778;
        const string Ask = "FOXLIFE?", Answer = "FOXLIFE!";
        static readonly string Token = Guid.NewGuid().ToString("N").Substring(0, 10);

        UdpClient beacon;   // у хоста: отвечает на вопросы
        UdpClient finder;   // у ищущего
        float nextAsk;
        /// <summary>Пока открыто окно «Играть вместе» — ищем игры (окно продлевает это время каждый кадр).</summary>
        public float searchUntil;
        public readonly List<LanGame> found = new List<LanGame>();
        public bool Searching => finder != null;

        static UdpClient NewUdp(int port)
        {
            var u = new UdpClient(AddressFamily.InterNetwork);
            u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            u.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            u.EnableBroadcast = true;
            try { u.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch { } // SIO_UDP_CONNRESET: без «соединение сброшено» от ICMP
            return u;
        }

        void StartBeacon()
        {
            StopBeacon();
            try { beacon = NewUdp(DiscoveryPort); }
            catch (Exception e) { beacon = null; Debug.LogWarning("FoxLife: сеть: поиск по локальной сети недоступен: " + e.Message); }
        }

        void StopBeacon()
        {
            try { beacon?.Close(); } catch { }
            beacon = null;
        }

        static string Clean(string s) => (s ?? "").Replace("|", "/").Replace("\n", " ");

        /// <summary>У хоста: ответить всем, кто ищет игры.</summary>
        void BeaconUpdate()
        {
            if (beacon == null) return;
            for (int guard = 0; guard < 32; guard++)
            {
                try
                {
                    if (beacon.Available <= 0) return;
                    IPEndPoint ep = null;
                    var data = beacon.Receive(ref ep);
                    if (!Encoding.UTF8.GetString(data).StartsWith(Ask)) continue;
                    int players = 1;
                    foreach (var p in peers) if (!p.closed && p.worldSent) players++;
                    var reply = Encoding.UTF8.GetBytes($"{Answer}|{Protocol}|{Token}|{port}|{Clean(G.townName)}|{Clean(myName)}|{players}");
                    beacon.Send(reply, reply.Length, ep);
                }
                catch (Exception e) { Debug.LogWarning("FoxLife: сеть: поиск: " + e.Message); return; }
            }
        }

        /// <summary>Куда спрашивать: общий широковещательный адрес, широковещательный адрес каждой сети и свой компьютер.</summary>
        static List<IPAddress> AskTargets()
        {
            var l = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a.Address)) continue;
                        byte[] ip = a.Address.GetAddressBytes(), mask = null;
                        try { if (a.IPv4Mask != null) mask = a.IPv4Mask.GetAddressBytes(); } catch { }
                        if (mask == null || mask.Length != 4 || (mask[0] == 0 && mask[1] == 0 && mask[2] == 0 && mask[3] == 0))
                            mask = ip[0] == 26 ? new byte[] { 255, 0, 0, 0 } : new byte[] { 255, 255, 255, 0 };
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        var ba = new IPAddress(b);
                        if (!l.Contains(ba)) l.Add(ba);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("FoxLife: сеть: адреса: " + e.Message); }
            return l;
        }

        static int AddrRank(string a) => a.StartsWith("26.") ? 0 : a.StartsWith("192.168.") || a.StartsWith("10.") ? 1 : a.StartsWith("127.") ? 3 : 2;

        /// <summary>У ищущего: раз в 1,5 с спросить, собрать ответы, забыть молчащие игры.</summary>
        void FinderUpdate()
        {
            bool want = Time.unscaledTime < searchUntil && role != Role.Host;
            if (!want)
            {
                if (finder != null) { try { finder.Close(); } catch { } finder = null; }
                return;
            }
            if (finder == null)
            {
                try { finder = NewUdp(0); nextAsk = 0; }
                catch (Exception e) { Debug.LogWarning("FoxLife: сеть: поиск: " + e.Message); searchUntil = 0; return; }
            }
            if (Time.unscaledTime >= nextAsk)
            {
                nextAsk = Time.unscaledTime + 1.5f;
                var q = Encoding.UTF8.GetBytes(Ask);
                foreach (var t in AskTargets())
                {
                    try { finder.Send(q, q.Length, new IPEndPoint(t, DiscoveryPort)); }
                    catch { }
                }
            }
            for (int guard = 0; guard < 64; guard++)
            {
                try
                {
                    if (finder.Available <= 0) break;
                    IPEndPoint ep = null;
                    var data = finder.Receive(ref ep);
                    var parts = Encoding.UTF8.GetString(data).Split('|');
                    if (parts.Length < 7 || parts[0] != Answer) continue;
                    if (!int.TryParse(parts[1], out int proto) || !int.TryParse(parts[3], out int port) || !int.TryParse(parts[6], out int players)) continue;
                    string token = parts[2];
                    if (token == Token) continue; // это мы сами
                    string addr = ep.Address.ToString();
                    var g = found.Find(x => x.token == token);
                    if (g == null) { g = new LanGame { token = token, address = addr }; found.Add(g); Debug.Log($"FoxLife: сеть: найдена игра {parts[4]} ({addr}:{port})"); }
                    else if (AddrRank(addr) < AddrRank(g.address)) g.address = addr;
                    g.port = port;
                    g.town = parts[4] + (proto != Protocol ? " (другая версия игры)" : "");
                    g.host = parts[5];
                    g.players = players;
                    g.seen = Time.unscaledTime;
                }
                catch (Exception e) { Debug.LogWarning("FoxLife: сеть: поиск: " + e.Message); break; }
            }
            found.RemoveAll(x => Time.unscaledTime - x.seen > 5f);
        }

        // ================= чат =================

        public readonly List<ChatLine> chat = new List<ChatLine>();

        void AddChat(string who, string text, bool mine)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            chat.Add(new ChatLine { who = who ?? "?", text = text, time = Time.unscaledTime, mine = mine });
            if (chat.Count > 40) chat.RemoveAt(0);
            Debug.Log($"FoxLife: чат: {who}: {text}");
        }

        static string ChatClean(string t)
        {
            t = (t ?? "").Replace("\n", " ").Replace("\r", " ").Trim();
            return t.Length > 200 ? t.Substring(0, 200) : t;
        }

        /// <summary>Написать в чат всем, кто в игре.</summary>
        public void SendChat(string text)
        {
            text = ChatClean(text);
            if (text.Length == 0) return;
            if (role == Role.Host)
            {
                AddChat(myName, text, true);
                Broadcast(Pack(Msg.Chat, w => { WStr(w, myName); WStr(w, text); }));
            }
            else if (role == Role.Client && server != null)
                server.Send(Pack(Msg.Chat, w => WStr(w, text))); // своё увидим, когда хост разошлёт всем
        }

        void HostChat(NetLink p, string text)
        {
            text = ChatClean(text);
            if (text.Length == 0) return;
            AddChat(p.name, text, false);
            Broadcast(Pack(Msg.Chat, w => { WStr(w, p.name); WStr(w, text); }));
        }

        void ClientChat(string who, string text) => AddChat(who, text, who == myName);
    }
}
