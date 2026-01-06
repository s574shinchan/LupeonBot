using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using LupeonBot.Client;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static LupeonBot.Client.SupabaseClient;

public static class CertDeletePagerCache
{
    public class PagerState
    {
        public ulong OwnerDiscordId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Keyword { get; set; } = "";
        public List<CertInfoRow> Rows { get; set; } = new();
        public int Index { get; set; } = 0;
    }

    public static ConcurrentDictionary<string, PagerState> Map { get; } = new();

    public static void Cleanup(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow;
        foreach (var kv in Map)
        {
            if (now - kv.Value.CreatedUtc > maxAge)
                Map.TryRemove(kv.Key, out _);
        }
    }
}
