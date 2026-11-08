using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AqwCompanion.Core.Capture;

namespace AqwCompanion.Tests.Capture;

/// <summary>Builds fictional game traffic for the capture pipeline tests.</summary>
internal static class GameCaptureBuilder
{
    public const int GamePort = 5588;
    public const int ClientPort = 50000;

    /// <summary>A server-to-client data segment.</summary>
    public static TcpSegment ServerData(uint sequence, byte[] payload, int clientPort = ClientPort) =>
        new(PcapBuilder.ServerAddress, GamePort, PcapBuilder.ClientAddress, clientPort, sequence, false, payload);

    /// <summary>The server's SYN-ACK; the first data byte has sequence <paramref name="sequence"/> + 1.</summary>
    public static TcpSegment SynAck(uint sequence, int clientPort = ClientPort) =>
        new(PcapBuilder.ServerAddress, GamePort, PcapBuilder.ClientAddress, clientPort, sequence, true, []);

    /// <summary>A client-to-server data segment.</summary>
    public static TcpSegment ClientData(uint sequence, byte[] payload, int clientPort = ClientPort) =>
        new(PcapBuilder.ClientAddress, clientPort, PcapBuilder.ServerAddress, GamePort, sequence, false, payload);

    /// <summary>The text as bytes followed by a NUL.</summary>
    public static byte[] Terminated(string text) => [.. Encoding.UTF8.GetBytes(text), 0];

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>The message envelope the game uses: <c>{"t":"xt","b":{"r":-1,"o":{"cmd":..., ...}}}</c>.</summary>
    public static Dictionary<string, object?> Envelope(string cmd, IDictionary<string, object?>? body = null)
    {
        var o = new Dictionary<string, object?> { ["cmd"] = cmd };
        foreach (var (key, value) in body ?? new Dictionary<string, object?>())
        {
            o[key] = value;
        }
        return new Dictionary<string, object?>
        {
            ["t"] = "xt",
            ["b"] = new Dictionary<string, object?> { ["r"] = -1, ["o"] = o },
        };
    }

    /// <summary>The JSON text of the object as bytes, without a terminator.</summary>
    public static byte[] PlainJson(object json) => JsonSerializer.SerializeToUtf8Bytes(json);

    /// <summary>An encoded blob: marker characters, then base64 of the zlib (or gzip) stream of the JSON text.</summary>
    public static byte[] EncodeBlob(object json, int markerChars = 1, bool stripPadding = false, bool gzip = false) =>
        EncodeBlobText(PlainJson(json), markerChars, stripPadding, gzip);

    public static byte[] EncodeBlobText(byte[] content, int markerChars = 1, bool stripPadding = false, bool gzip = false)
    {
        var base64 = Convert.ToBase64String(Compress(content, gzip));
        if (stripPadding)
        {
            base64 = base64.TrimEnd('=');
        }
        return Encoding.ASCII.GetBytes(new string('Z', markerChars) + base64);
    }

    public static byte[] Compress(byte[] content, bool gzip = false)
    {
        using var output = new MemoryStream();
        using (Stream compressor = gzip
            ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
            : new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressor.Write(content);
        }
        return output.ToArray();
    }

    public const string SecretEmail = "SECRET-EMAIL@example.com";
    public const string SecretIp = "203.0.113.77";
    public const string SecretToken = "SECRET-TOKEN-0123456789";
    public const string SecretPlayer = "SECRET-PLAYER";
    public const string SecretPassword = "SECRET-PASSWORD";

    /// <summary>Every marker secret; none of them may ever reach a snapshot.</summary>
    public static readonly string[] Secrets = [SecretEmail, SecretIp, SecretToken, SecretPlayer, SecretPassword];

    /// <summary>A faction entry as the game sends it: all values are strings.</summary>
    public static Dictionary<string, object?> Faction(int id, string name, long rep) => new()
    {
        ["CharFactionID"] = (900000 + id).ToString(),
        ["FactionID"] = id.ToString(),
        ["sName"] = name,
        ["iRep"] = rep.ToString(),
    };

    /// <summary>An item entry with the allowed fields and several disallowed ones that carry marker secrets.</summary>
    public static Dictionary<string, object?> Item(
        string name,
        string type = "Item",
        int itemId = 1,
        int qty = 1,
        int stack = 1,
        int level = 1,
        bool coins = false,
        bool upgrade = false,
        bool equip = false,
        bool wear = false,
        bool quest = false,
        bool bank = false,
        bool house = false,
        bool temp = false,
        int? classId = null)
    {
        var item = new Dictionary<string, object?>
        {
            ["ItemID"] = itemId.ToString(),
            ["CharItemID"] = "55" + itemId,
            ["sName"] = name,
            ["sType"] = type,
            ["sDesc"] = "A fictional thing. " + SecretPlayer,
            ["sFile"] = "items/fictional.swf",
            ["sMeta"] = SecretToken,
            ["iQty"] = qty.ToString(),
            ["iStk"] = stack.ToString(),
            ["iLvl"] = level,
            ["iCost"] = 100,
            ["tPurchase"] = "2020-01-01T00:00:00 " + SecretEmail,
            ["metaValues"] = new Dictionary<string, object?> { ["ref"] = SecretIp, ["chaos"] = 3 },
            ["bCoins"] = coins ? 1 : 0,
            ["bUpg"] = upgrade ? "1" : "0",
            ["bEquip"] = equip,
            ["bWear"] = wear ? 1 : 0,
            ["bQuest"] = quest ? 1 : 0,
            ["bBank"] = bank ? 1 : 0,
            ["bHouse"] = house ? 1 : 0,
            ["bTemp"] = temp ? 1 : 0,
            ["bStaff"] = 0,
        };
        if (classId is { } id)
        {
            item["ClassID"] = id.ToString();
            item["EnhLvl"] = 20;
        }
        return item;
    }

    /// <summary>A loadInventoryBig message with the allowed lists and disallowed siblings that carry marker secrets.</summary>
    public static Dictionary<string, object?> LoadInventoryBig(
        IEnumerable<object?>? factions = null,
        IEnumerable<object?>? items = null,
        IEnumerable<object?>? houseItems = null) =>
        Envelope("loadInventoryBig", new Dictionary<string, object?>
        {
            ["bankCount"] = 0,
            ["factions"] = (factions ?? []).ToList(),
            ["items"] = (items ?? []).ToList(),
            ["hitems"] = (houseItems ?? []).ToList(),
            ["procs"] = new Dictionary<string, object?> { ["1"] = new Dictionary<string, object?> { ["nam"] = SecretToken } },
        });

    /// <summary>
    /// The data of an own profile with fictional values, plus the sensitive fields (all carrying marker secrets).
    /// Pass <paramref name="own"/> false to leave out <c>intGold</c>, as the profiles of other players do.
    /// </summary>
    public static Dictionary<string, object?> ProfileData(string name = "Fictional Hero", bool own = true)
    {
        var data = new Dictionary<string, object?>
        {
            ["strUsername"] = name,
            ["strEmail"] = SecretEmail,
            ["strIP"] = SecretIp,
            ["UserID"] = 424242,
            ["CharID"] = "737373",
            ["dCreated"] = "2015-05-05T00:00:00",
            ["sCountry"] = "XX",
            ["bPermaMute"] = 0,
            ["strQuests"] = SecretToken,
            ["guild"] = new Dictionary<string, object?> { ["Name"] = SecretPlayer },
            ["intLevel"] = 60,
            ["intExp"] = 123456,
            ["intExpToLevel"] = "654321",
            ["intCoins"] = 777,
            ["iCP"] = 5000,
            ["intDaysPlayed"] = 300,
            ["iBagSlots"] = 90,
            ["iBankSlots"] = 80,
            ["iHouseSlots"] = 70,
            ["iLoadoutSlots"] = 5,
            ["iUpg"] = 1,
            ["iUpgDays"] = 25,
            ["iBoostXP"] = 10,
            ["iBoostG"] = 20,
            ["iBoostCP"] = 30,
            ["iBoostRep"] = 40,
        };
        if (own)
        {
            data["intGold"] = 9876543210L;
        }
        return data;
    }

    /// <summary>An initUserData message around the given profile data.</summary>
    public static Dictionary<string, object?> InitUserData(Dictionary<string, object?>? data = null) =>
        Envelope("initUserData", new Dictionary<string, object?>
        {
            ["uid"] = 12,
            ["strFrame"] = "Enter",
            ["strPad"] = "Spawn",
            ["data"] = data ?? ProfileData(),
        });

    /// <summary>The segments of one login: what the server sends, and what the client sends (with a fake password).</summary>
    internal sealed record LoginTraffic(List<TcpSegment> Server, List<TcpSegment> Client)
    {
        /// <summary>Server and client segments, server first.</summary>
        public List<TcpSegment> All => [.. Server, .. Client];

        /// <summary>The sequence number the next server data byte would have.</summary>
        public uint NextServerSequence { get; init; }
    }

    /// <summary>The faction/item content of the standard login.</summary>
    public static Dictionary<string, object?> StandardInventory() =>
        LoadInventoryBig(
            [Faction(5, "Fictional Order", 5000), Faction(6, "Other Order", 302500), Faction(7, "Fresh Order", 0)],
            [Item("Fictional Blade", "Sword", itemId: 11, equip: true, coins: true), Item("Fictional Class", "Class", itemId: 12, classId: 9)],
            [Item("Fictional Chair", "House Item", itemId: 13)]);

    /// <summary>
    /// One login: SYN-ACK, a loginResponse text message with a long marker field, initUserData, loadInventoryBig, other
    /// players' blobs carrying markers, a trailing monster message, and a client login message with a fake password.
    /// The server stream is cut into segments of <paramref name="segmentSize"/> bytes.
    /// </summary>
    public static LoginTraffic Connection(
        int clientPort = ClientPort,
        string name = "Fictional Hero",
        uint isn = 1000,
        bool inventoryFirst = false,
        bool includeInventory = true,
        bool includeProfile = true,
        int segmentSize = 1200,
        IEnumerable<byte[]>? extraServerMessages = null,
        IEnumerable<byte[]>? leadingServerMessages = null)
    {
        var profile = EncodeBlob(InitUserData(ProfileData(name)));
        var inventory = EncodeBlob(StandardInventory());
        var messages = new List<byte[]>
        {
            Bytes("%xt%loginResponse%-1%true%1234%" + new string('x', 30) + "%" + SecretToken + SecretToken + SecretToken + "%1.5%"),
            Bytes("<msg t='sys'><body action='joinOK' r='1'><vars/></body></msg>"),
            EncodeBlob(Envelope("initUserDatas", new Dictionary<string, object?>
            {
                ["a"] = new List<object?> { new Dictionary<string, object?> { ["uid"] = 3, ["data"] = ProfileData(SecretPlayer, own: false) } },
            })),
            EncodeBlob(InitUserData(ProfileData(SecretPlayer + "-2", own: false))),
        };
        var wanted = new List<byte[]>();
        if (includeProfile)
        {
            wanted.Add(profile);
        }
        if (includeInventory)
        {
            wanted.Add(inventory);
        }
        if (inventoryFirst)
        {
            wanted.Reverse();
        }
        messages.InsertRange(2, wanted);
        messages.InsertRange(0, leadingServerMessages ?? []);
        messages.Add(PlainJson(Envelope("mtls", new Dictionary<string, object?> { ["id"] = 1, ["o"] = new Dictionary<string, object?> { ["intHP"] = 5 } })));
        messages.AddRange(extraServerMessages ?? []);

        var stream = messages.SelectMany(m => m.Append((byte)0)).ToArray();
        var server = new List<TcpSegment> { SynAck(isn, clientPort) };
        var sequence = isn + 1;
        for (var offset = 0; offset < stream.Length; offset += segmentSize)
        {
            var chunk = stream[offset..Math.Min(stream.Length, offset + segmentSize)];
            server.Add(ServerData(sequence, chunk, clientPort));
            sequence += (uint)chunk.Length;
        }

        var client = new List<TcpSegment>
        {
            ClientData(5000, Terminated($"%xt%zm%login%1%{SecretPassword}%{SecretEmail}%"), clientPort),
            ClientData(5100, PlainJson(Envelope("login", new Dictionary<string, object?> { ["pass"] = SecretPassword })), clientPort),
        };
        return new LoginTraffic(server, client) { NextServerSequence = sequence };
    }
}
