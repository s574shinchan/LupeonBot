using Discord;
using Discord.Interactions;
using DiscordBot;
using LupeonBot.Client;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LupeonBot.Module
{
    public class ProfileModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("프로필", "로스트아크 캐릭터 프로필을 조회합니다.")]
        public async Task ProfileAsync([Summary(description: "캐릭터 이름")] string 캐릭터명)
        {
            // ✅ 슬래시는 3초 내 응답 필요 → 먼저 Defer(대기표시)
            await DeferAsync();

            try
            {
                Program.InitEdit();

                //  ✅ 로아 API 호출해서 Program 전역변수 채우기
                using var api = new LostArkApiClient(Program.LostArkJwt);
                await LostArkProfileMapper.FillProgramAsync(api, 캐릭터명);

                // ✅ Embed 구성
                var eb = new EmbedBuilder()
                    .WithTitle($"📌 {Program.m_캐릭터명} [{Program.m_서버}]")
                    .WithColor(Color.DarkBlue)
                    .AddField("원정대", $"{Program.m_원정대레벨}", true)
                    .AddField("길드", string.IsNullOrWhiteSpace(Program.m_길드) ? "-" : Program.m_길드, true)
                    .AddField("칭호", string.IsNullOrWhiteSpace(Program.m_칭호) ? "-" : Program.m_칭호, true)
                    .AddField("직업", Program.m_직업, true)
                    .AddField("아이템레벨", Program.m_아이템레벨, true)
                    .AddField("전투력", string.IsNullOrWhiteSpace(Program.m_전투력) ? "-" : Program.m_전투력, true)
                    .AddField("아크 패시브 : " + Program.m_각인, Program.m_아크패시브, false)
                    .WithFooter("Develop by. 갱프")
                    .WithThumbnailUrl(Program.m_ImgLink);

                // 보유 캐릭 리스트가 너무 길면 잘라서 출력(디스코드 제한 대비)
                if (!string.IsNullOrWhiteSpace(Program.m_보유캐릭))
                {
                    var text = Program.m_보유캐릭;
                    if (text.Length > 900) text = text.Substring(0, 900) + "\n...";
                    eb.AddField($"보유 캐릭 : {Program.m_보유캐릭수}", text, false);
                }

                await FollowupAsync(embed: eb.Build());
            }
            catch (Exception ex)
            {
                await FollowupAsync($"❌ 조회 실패: `{ex.Message}`");
            }
        }
    }
}
