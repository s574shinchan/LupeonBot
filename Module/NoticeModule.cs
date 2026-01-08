using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot;
using LupeonBot.Cache;
using LupeonBot.Client;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public sealed class NoticeModule
    {
        public static async Task<List<LostArkNotice>> FetchNewMaintenanceNoticesAsync()
        {
            using var api = new LostArkApiClient(Program.LostArkJwt);

            // 1) 공지 전체
            var notices = await api.GetNoticesAsync();

            // 2) 최근 N개 중에서 "점검"만 먼저 필터
            // 점검 중 최근 20개
            var recentMaintenance = notices.Where(IsMaintenanceNotice).Take(20).ToList();

            if (recentMaintenance.Count == 0)
                return new List<LostArkNotice>();

            // 3) 이 링크들 중 이미 본 것들 조회
            var links = recentMaintenance.Select(x => x.Link).ToList();
            var seen = await SupabaseClient.GetSeenLinksAsync(links);

            // 4) 신규(= DB에 없음)만
            var newNotices = recentMaintenance
                .Where(n => !seen.Contains(n.Link))
                .OrderBy(n => n.Date)   // 오래된 점검부터 알림
                .ToList();

            // 5) 신규만 저장
            if (newNotices.Count > 0)
                await SupabaseClient.InsertManyAsync(newNotices);

            return newNotices;
        }

        private static bool IsMaintenanceNotice(LostArkNotice n)
        {
            if (n == null) return false;

            // Type 또는 Title에 점검이라는 단어가 들어오는 경우가 많아서 둘 다 체크
            var type = (n.Type ?? "").Trim();
            var title = (n.Title ?? "").Trim();

            return type.Contains("점검", StringComparison.OrdinalIgnoreCase)
                || title.Contains("점검", StringComparison.OrdinalIgnoreCase);
        }
    }
}
