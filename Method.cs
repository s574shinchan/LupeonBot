using Discord;
using Discord.WebSocket;
using DiscordBot;
using LupeonBot.Client;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LupeonBot
{
    public class Method
    {
        public static string m_Url = string.Empty;
        public static string m_서버 = string.Empty;
        public static string m_아이템레벨 = string.Empty;
        public static string m_원정대레벨 = string.Empty;
        public static string m_전투력 = string.Empty;
        public static string m_아크패시브 = string.Empty;
        public static string m_길드 = string.Empty;
        public static string m_칭호 = string.Empty;
        public static string m_직업 = string.Empty;
        public static string m_각인 = string.Empty;
        public static string m_보유캐릭 = string.Empty;
        public static string m_보유캐릭수 = string.Empty;
        public static string m_캐릭터명 = string.Empty;
        public static string m_ImgLink = string.Empty;

        public const string StoveProfileImagePath = "https://raw.githubusercontent.com/s574shinchan/LupeonBot/main/image/StoveProfile.png";
        
        public static string GetSplitString(string source, char separator, int index)
        {
            if (string.IsNullOrEmpty(source)) return "";
            var parts = source.Split(separator);
            return index >= 0 && index < parts.Length ? parts[index] : "";
        }

        public static string DateFormat(string _date)
        {
            string Year = string.Empty;
            string Month = string.Empty;
            string Day = string.Empty;

            try
            {
                if (_date != string.Empty)
                {
                    _date = _date.Replace("-", "");
                    Year = _date.Substring(0, 4);
                    Month = _date.Substring(4, 2);
                    Day = _date.Substring(6, 2);
                    return Year + "-" + Month + "-" + Day;
                }
                else
                {
                    return "";
                }
            }
            catch
            {
                return "";
            }

        }

        public static bool IsNumberic(object strNumber)
        {

            try
            {
                if (strNumber != DBNull.Value && !strNumber.Equals(""))
                {
                    Convert.ToDouble(strNumber);
                    return true;
                }
                else
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------------------
        // 파서 유틸
        // ---------------------------
        public static bool TryParseItemLevel(string raw, out double lv)
        {
            lv = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            // "Lv.1640.00" / "1640.00" 등 대응
            var s = raw.Replace("Lv.", "", StringComparison.OrdinalIgnoreCase).Trim();
            s = s.Replace(",", "");

            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out lv)
            || double.TryParse(s, NumberStyles.Float, CultureInfo.GetCultureInfo("ko-KR"), out lv);
        }

        public static bool TryParseStdLevel(string raw, out double lv)
        {
            lv = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var s = raw.Trim().Replace(",", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out lv)
            || double.TryParse(s, NumberStyles.Float, CultureInfo.GetCultureInfo("ko-KR"), out lv);
        }

        public static bool TryExtractStoveId(string text, out string stoveId, out string url)
        {
            stoveId = "";
            url = "";

            // 메시지 안에 링크가 섞여있을 수 있으니 URL만 먼저 추출
            var mUrl = System.Text.RegularExpressions.Regex.Match(text ?? "", @"https?://\S+");
            if (!mUrl.Success) return false;

            url = mUrl.Value.Trim().TrimEnd('>'); // 가끔 <> 감싸는 경우 방어

            // onstove 프로필 링크인지 확인
            if (!url.Contains("profile.onstove.com/ko/", StringComparison.OrdinalIgnoreCase))
                return false;

            // 맨 끝 숫자 추출
            var mId = System.Text.RegularExpressions.Regex.Match(url, @"(\d+)\s*$");
            if (!mId.Success) return false;

            stoveId = mId.Groups[1].Value;
            return true;
        }

        // ---------------------------
        // 네 기존 메서드 (여기서 구현/호출)
        // ---------------------------
        public static async Task GetSimpleProfile(string nickName)
        {
            // TODO: 네 기존 로직 그대로
            //  ✅ 로아 API 호출해서 Program 전역변수 채우기
            using var api = new LostArkApiClient(Program.LostArkJwt);

            var prof = await api.GetArmoryProfilesAsync(nickName);
            if (prof == null) throw new Exception("프로필 응답이 비어있음");

            m_서버 = prof.ServerName ?? "";
            m_직업 = prof.CharacterClassName ?? "";
            m_아이템레벨 = prof.ItemMaxLevel ?? prof.ItemAvgLevel ?? "";
            m_ImgLink = prof.CharacterImage ?? "";

            var siblings = await api.GetSiblingsAsync(nickName) ?? new List<CharacterSibling>();

            // 문자열용 (로그 출력용)
            m_보유캐릭 = BuildSiblingsLineText(siblings, nickName);
        }

        public static async Task<List<CharacterSibling>> GetCertProfile(string nickName)
        {
            using var api = new LostArkApiClient(Program.LostArkJwt);

            var prof = await api.GetArmoryProfilesAsync(nickName);
            if (prof == null) throw new Exception("프로필 응답이 비어있음");

            m_서버 = prof.ServerName ?? "";
            m_아이템레벨 = prof.ItemMaxLevel ?? prof.ItemAvgLevel ?? "";
            m_전투력 = prof.CombatPower ?? "";
            m_캐릭터명 = prof.CharacterName ?? "";

            var siblings = await api.GetSiblingsAsync(nickName) ?? new List<CharacterSibling>();

            // 문자열용 (로그 출력용)
            m_보유캐릭 = BuildSiblingsLineText(siblings, nickName);

            return siblings;
        }

        private static string BuildSiblingsLineText(List<CharacterSibling> siblings, string excludeName = null)
        {
            if (siblings == null || siblings.Count == 0) return "";

            var list = siblings
                .Select(x => (x.CharacterName ?? "").Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Where(n => excludeName == null || !n.Equals(excludeName.Trim(), StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return string.Join("/", list);
        }

        public static async Task SendNoticeAsync(ISocketMessageChannel channel, string _mStdLv, ulong _CheckChannelId)
        {
            // 파일에서 기준레벨 다시 읽기(최신값)
            var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");
            var exchangeInfo = File.Exists(path) ? File.ReadAllText(path) : "";
            var mInfo = exchangeInfo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            var std = "";
            if (mInfo.Length > 0) std = Method.GetSplitString(mInfo[0], ':', 1).Trim();

            if (!string.IsNullOrWhiteSpace(std))
                _mStdLv = std; // ✅ 멤버에도 반영

            string mChValue = $"<#{_CheckChannelId}>";

            string m_emote = "<:pdiamond:907957436483248159>";
            string desc =
            "**[거래소 역할 신청]**\n" +
            $"{m_emote}1. 역할신청 버튼을 눌러서 개인 인증채널 생성\n" +
            $"{m_emote}2. 생성된 개인채널에 신청가이드에 따라 진행\n" +
            $"{m_emote}3. 역할지급 완료여부는 {mChValue}채널 에서 확인가능\n\n" +
            "**[유의사항]**\n" +
            $"{m_emote}1. 역할지급까지 최대 수시간 소요될 수 있습니다.\n" +
            $"{m_emote}2. 개인 인증채널이 삭제된 경우 신청가이드를 확인바랍니다.\n" +
            $"{m_emote}3. 인증신청한 캐릭터의 직업역할이 자동부여 됩니다.\n" +
            $"{m_emote}4. 발급절차를 틀린경우 따로 알려드리지 않습니다.\n" +
            $"{m_emote}5. 발급절차를 틀린경우 24시간 타임아웃 적용됩니다.";

            var embed = new EmbedBuilder()
            .WithAuthor("신청가이드", url: "https://discord.com/channels/513799663086862336/653484646260277248")
            .WithColor(Color.DarkOrange)
            .WithDescription(desc)
            .WithFooter("Develop by. 갱프");

            var buttons = new ComponentBuilder()
            .WithButton(label: "거래소신청", customId: "Cert", style: ButtonStyle.Primary);

            await channel.SendMessageAsync(embed: embed.Build(), components: buttons.Build());
        }

        public static async Task DeleteChannelAsync(SocketGuild guild, ITextChannel channel, string categoryName)
        {
            // 채널이 이미 삭제됐을 수도 있으니 try
            ulong? categoryId = channel.CategoryId;

            try
            {
                await channel.DeleteAsync();
            }
            catch
            {
                // 이미 삭제된 경우 등 무시
                return;
            }

            if (categoryId == null) return;

            var category = guild.GetCategoryChannel(categoryId.Value);
            if (category == null) return;

            // 너 로직: 같은 이름의 카테고리가 몇 개인지 카운트
            int sameNameCount = guild.CategoryChannels.Count(c => c.Name == categoryName);

            // 삭제하려는 채널의 카테고리 이름이 지정한 이름과 같고,
            // 카테고리에 남은 채널이 1개 미만이면(=거의 비었으면) + 같은 이름 카테고리가 2개 이상이면 삭제
            if (category.Name == categoryName && category.Channels.Count < 2 && sameNameCount > 1)
            {
                try { await category.DeleteAsync(); } catch { }
            }
        }
    }
}

