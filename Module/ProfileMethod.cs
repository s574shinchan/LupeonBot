using Discord;
using Discord.Interactions;
using DiscordBot;
using LupeonBot.Cache;
using LupeonBot.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public class ProfileMethod
    {
        public static async Task<SimpleProfile?> GetSimpleProfile(string 캐릭터명)
        {
            // TODO: 네 기존 로직 그대로
            //  ✅ 로아 API 호출해서 Program 전역변수 채우기
            using var api = new LostArkApiClient(Program.LostArkJwt);

            var prof = await api.GetArmoryProfilesAsync(캐릭터명);
            if (prof == null) return null;

            var siblings = await api.GetSiblingsAsync(캐릭터명) ?? new List<CharacterSibling>();

            var profile = new SimpleProfile
            {
                서버 = prof.ServerName ?? "",
                직업 = prof.CharacterClassName ?? "",
                아이템레벨 = prof.ItemMaxLevel ?? prof.ItemAvgLevel ?? "",
                캐릭터명 = 캐릭터명,
                ImgLink = prof.CharacterImage ?? "",
                보유캐릭 = BuildSiblingsLineText(siblings, 캐릭터명),
                보유캐릭_목록 = BuildSiblingsListText(siblings, 캐릭터명),
            };

            return profile;
        }

        public static async Task<SimpleProfile?> GetCertProfile(string 캐릭터명)
        {
            using var api = new LostArkApiClient(Program.LostArkJwt);

            var prof = await api.GetArmoryProfilesAsync(캐릭터명);
            if (prof == null) return null;

            var siblings = await api.GetSiblingsAsync(캐릭터명) ?? new List<CharacterSibling>();

            var profile = new SimpleProfile
            {
                캐릭터명 = 캐릭터명,
                ImgLink = prof.CharacterImage ?? "",
                보유캐릭 = BuildSiblingsLineText(siblings, 캐릭터명),
                보유캐릭_목록 = BuildSiblingsListText(siblings, 캐릭터명),
            };

            return profile;
        }

        private static string BuildSiblingsLineText(List<CharacterSibling> siblings, string excludeName = null)
        {
            if (siblings == null || siblings.Count == 0) return "";

            var target = (excludeName ?? "").Trim();

            var list = siblings
                .Select(x => (x.CharacterName ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Equals(target, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return string.Join("/", list);
        }

        private static List<string> BuildSiblingsListText(List<CharacterSibling> siblings, string excludeName = null)
        {
            if (siblings == null || siblings.Count == 0)
                return new List<string>();

            var target = (excludeName ?? "").Trim();

            var list = siblings
                .Select(x => (x.CharacterName ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Equals(target, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list;
        }

    }
}
