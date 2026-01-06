using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot;

using LupeonBot.Client;
using LupeonBot.Module;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using static DiscordBot.Program;

namespace LupeonBot
{
    public sealed class CertUpdateModule : InteractionModuleBase<SocketInteractionContext>
    {
        private string mStdLv = ""; // 파일에서 읽어온 값

        [SlashCommand("인증갱신공지", "기간내 거래소 인증데이터 갱신을 위한 버튼 표시")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task CertInfoUpdate()
        {
            string Emote = "<:pdiamond:907957436483248159>";
            string m_body = string.Empty;
            m_body += Emote + " 아래의 인증갱신 버튼을 눌러서 정보를 입력하시면 됩니다." + Environment.NewLine;
            m_body += Emote + " 별도의 인증채널이 생기지 않습니다." + Environment.NewLine;
            m_body += Emote + " 거래소 역할이 새로 부여되는 것이 아닙니다." + Environment.NewLine;
            m_body += Emote + " 인증된 데이터를 최신화 하기 위한 목적입니다." + Environment.NewLine + Environment.NewLine;
            m_body += "**[ 유의사항 ]**" + Environment.NewLine;
            m_body += Emote + " 기준레벨보다 낮은 경우 갱신되지 않습니다." + Environment.NewLine;
            m_body += Emote + " 미갱신자는 추후 거래소 역할이 회수될 예정입니다.";

            var embed = new EmbedBuilder()
              .WithTitle("거래소 인증갱신 • 루페온")
              .WithColor(Discord.Color.Green)
              .WithDescription(m_body)
              .WithImageUrl(Method.StoveProfileImagePath)
              .WithFooter("Develop by. 갱프");

            var component = new ComponentBuilder()
              .WithButton(label: "인증정보갱신", customId: "CertInfoUpdate", style: ButtonStyle.Success);

            await Context.Channel.SendMessageAsync(embed: embed.Build(), components: component.Build());
            await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
        }

        public class CertModalData : IModal
        {
            public string Title => "인증정보갱신";

            [InputLabel("로스트아크 캐릭터명을 입력해주세요.")]
            [ModalTextInput("NickName", placeholder: "인증받고자하는 캐릭터명", maxLength: 20)]
            public string NickName { get; set; } = "";

            [InputLabel("스토브 프로필 링크를 입력해주세요.")]
            [ModalTextInput("StoveUrl", placeholder: "예) https://profile.onstove.com/ko/123456", maxLength: 50)]
            public string StoveUrl { get; set; } = "";
        }

        [ComponentInteraction("CertInfoUpdate")]
        public async Task CertInfoUpdateAsync()
        {
            try
            {
                var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");
                var exchangeInfo = File.Exists(path) ? File.ReadAllText(path) : "";

                var mInfo = exchangeInfo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (mInfo.Length > 0)
                    mStdLv = Method.GetSplitString(mInfo[0], ':', 1).Trim();
                else
                    mStdLv = "";
            }
            catch
            {
                // 파일 IO 에러가 나도 아래에서 빈값 처리로 빠지게 둠
                mStdLv = "";
            }

            // 기준레벨 없으면 안내
            if (string.IsNullOrWhiteSpace(mStdLv))
            {
                await RespondAsync("관리자에게 문의해주세요.", ephemeral: true);
                return;
            }

            var guildUser = Context.User as SocketGuildUser;
            int mRoleYn = 0;
            foreach (var role in guildUser.Roles)
            {
                if (role.Id == 1264901726251647086)
                {
                    mRoleYn++;
                    break;
                }
            }

            if (mRoleYn == 0)
            {
                await RespondAsync("거래소 역할을 보유 중이지 않습니다. 인증갱신을 할 수 없습니다.", ephemeral: true);
                return;
            }

            // 모달 띄우기
            await Context.Interaction.RespondWithModalAsync<CertModalData>("CertInfoUpdateModal");
        }

        [ModalInteraction("CertInfoUpdateModal")]
        public async Task Modal_CertModal(CertModalData data)
        {
            string m_NickNm = "";
            string m_StoveId = "";

            m_NickNm = (data.NickName ?? "").Trim();

            if (Context.User is not SocketGuildUser user)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(m_NickNm))
            {
                await RespondAsync("❌ 캐릭터명을 입력해주세요.", ephemeral: true);
                return;
            }

            // 시간이 걸릴 수 있으니 defer
            await RespondAsync("인증 데이터를 확인 중입니다.", ephemeral: true);

            // 기준 충족 -> 프로필 조회 (네 기존 함수 그대로)
            var profile = await ProfileMethod.GetSimpleProfile(m_NickNm);
            // ===============================================

            if (Method.TryExtractStoveId(data.StoveUrl, out var stoveId, out var url))
            {
                m_StoveId = stoveId;
            }
            else
            {
                await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크가 올바르지 않습니다.");
                return;
            }

            try
            {
                var path = Path.Combine(Environment.CurrentDirectory, "ExchangeInfo.txt");
                var exchangeInfo = File.Exists(path) ? File.ReadAllText(path) : "";

                var mInfo = exchangeInfo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (mInfo.Length > 0)
                    mStdLv = Method.GetSplitString(mInfo[0], ':', 1).Trim();
                else
                    mStdLv = "";
            }
            catch
            {
                // 파일 IO 에러가 나도 아래에서 빈값 처리로 빠지게 둠
                mStdLv = "";
            }

            // 아이템레벨 파싱: "Lv.1640.00" 형태 대응
            if (!Method.TryParseItemLevel(profile.아이템레벨, out var itemLv))
            {
                await FollowupAsync($"❌ 아이템레벨을 파싱하지 못했습니다: `{profile.아이템레벨}`", ephemeral: true);
                return;
            }

            if (!Method.TryParseStdLevel(mStdLv, out var stdLv))
            {
                await FollowupAsync($"❌ 기준레벨 설정값이 올바르지 않습니다: `{mStdLv}`", ephemeral: true);
                return;
            }

            // 기준 미달
            if (itemLv < stdLv)
            {
                string failDesc = $"캐릭명 : {m_NickNm}\n" +
                                  $"아이템 : {profile.아이템레벨}\n" +
                                  $"해당 캐릭터는 인증 기준레벨 미달 입니다.\n" +
                                  $"거래소인증은 {mStdLv} 이상의 캐릭으로만 가능합니다.";

                var s_embed = new EmbedBuilder()
                    .WithAuthor("🚨 요청실패")
                    .WithDescription(failDesc);

                await FollowupAsync(embed: s_embed.Build(), ephemeral: true);
                return;
            }

            DateTime dt = DateTime.UtcNow.AddHours(9);
            string m_CertDate = dt.ToString("yyyy-MM-dd"); // 2026-01-06
            string m_CertTime = dt.ToString("HH:mm");      // 01:23

            var dbRow = await SupabaseClient.GetCertInfoByUserIdAsync(user.Id.ToString());

            if (dbRow != null)
            {
                var (ok, body) = await SupabaseClient.UpdateCertOnlyAsync(
                    userId: user.Id.ToString(),
                    stoveId: dbRow.StoveId,
                    characters: profile.보유캐릭_목록,
                    certDate: m_CertDate,
                    certTime: m_CertTime
                    );

                if (!ok)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ DB 업데이트 실패\n```{body}```");
                    return;
                }

                // StoveId 비교
                if (!string.Equals(dbRow.StoveId, m_StoveId, StringComparison.Ordinal))
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 저장된 정보와 신청자의 스토브 계정이 다릅니다.");
                    return;
                }
            }
            else
            {
                string joindate = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd") ?? "";
                string jointime = user.JoinedAt?.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm") ?? "";

                var (ok, body) = await SupabaseClient.UpsertCertInfoAsync(
                    userId: user.Id.ToString(),
                    stoveId: m_StoveId,
                    userNm: user.Username,
                    characters: profile.보유캐릭_목록,
                    joinDate: joindate,
                    joinTime: jointime,
                    certDate: m_CertDate,
                    certTime: m_CertTime
                    );

                if (!ok)
                {
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ DB 업데이트 실패\n```{body}```");
                    return;
                }
            }

            string m_Context = "";
            m_Context += "갱신대상 : " + user.Mention + "``(" + user.Id.ToString() + ")``" + Environment.NewLine + Environment.NewLine;
            m_Context += "갱신캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine;
            m_Context += "위 정보로 거래소 인증내역이 갱신되었습니다.";

            var ComPeleteEmbed = new EmbedBuilder()
                .WithAuthor("✅ 갱신완료")
                .WithDescription(m_Context)
                .WithColor(Color.Green)
                .WithThumbnailUrl(user.GetAvatarUrl(ImageFormat.Auto))
                .WithFooter("Develop by. 갱프　　　　　　　　　갱신일시 : " + m_CertDate + " " + m_CertTime);

            await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 처리되었습니다.");
            await ModifyOriginalResponseAsync(m => m.Embed = ComPeleteEmbed.Build());
        }
    }
}
