using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LupeonBot.Client.SupabaseClient;

namespace LupeonBot.Cache
{
    // ====== 페이지/단건 상태 캐시 ======
    public static class CertDeleteCache
    {
        public class State
        {
            public ulong DiscordId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string Input { get; set; } = "";

            // 결과 rows (userid 단건도 rows로 담아 통일)
            public List<CertInfoRow> Rows { get; set; } = new();
            public int Index { get; set; } = 0;

            public bool IsSingleMode { get; set; } = false; // userid 입력 모드
        }

        public static ConcurrentDictionary<string, State> Map { get; } = new();

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
}
