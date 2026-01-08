using DiscordBot;
using LupeonBot.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public class EventModule
    {
        public static async Task<List<LoaEventItem>> FetchNewEventsAsync()
        {
            using var api = new LostArkApiClient(Program.LostArkJwt);

            var list = await api.GetEventsAsync();
            if (list == null || list.Count == 0) return new List<LoaEventItem>();

            // ✅ 이미 보낸 링크 저장 파일(또는 기존 공지 저장 로직 재사용)
            var sent = LoadSentKeys("data/loa_events_sent.txt");

            // ✅ 새 이벤트만
            var newOnes = list
                .Where(e => !string.IsNullOrWhiteSpace(e.Link))
                .Where(e => !sent.Contains(e.Link.Trim()))
                .ToList();

            // ✅ 새로 보낸 것 저장
            foreach (var e in newOnes)
                sent.Add(e.Link!.Trim());

            SaveSentKeys("data/loa_events_sent.txt", sent);

            return newOnes;
        }

        private static HashSet<string> LoadSentKeys(string path)
        {
            if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return File.ReadAllLines(path)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveSentKeys(string path, HashSet<string> keys)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, keys.OrderBy(x => x));
        }

    }
}
