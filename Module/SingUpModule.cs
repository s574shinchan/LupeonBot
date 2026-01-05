using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LupeonBot.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public class SingUpModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("가입공지", "서버가입버튼 표시")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SignUpNoticeAsync()
        {
            if (Context.User is not SocketGuildUser user || !user.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            string m_body = string.Empty;
            string Emote = "<:pdiamond:907957436483248159>";

            m_body += "**[ 서버가입 ]**" + Environment.NewLine;
            m_body += Emote + " 1. 서버가입 버튼클릭" + Environment.NewLine;
            m_body += Emote + " 2. 캐릭터명입력" + Environment.NewLine;
            m_body += Emote + " 3. 아래 이미지 참고하여 스토브 링크 입력 후 완료" + Environment.NewLine + Environment.NewLine;
            m_body += "**[ 유의사항 ]**" + Environment.NewLine;
            m_body += Emote + " 서버 내 채널이용은 기본인증을 완료해야 이용가능합니다." + Environment.NewLine;
            m_body += Emote + " 매주 수요일 정기점검 시간(~ 10:00)" + Environment.NewLine;
            m_body += Emote + " 해당 시간에는 서버가입이 되지 않습니다." + Environment.NewLine;
            m_body += Emote + " 점검이 끝난 후 공식홈페이지가 접속가능한 경우" + Environment.NewLine;
            m_body += Emote + " 가입절차를 재진행하시면 됩니다.";

            var embed = new EmbedBuilder()
                .WithColor((Discord.Color)System.Drawing.Color.FromArgb(240, 189, 109))
                .WithDescription(m_body)
                //.WithImageUrl("attachment://스토브프로필.png")
                .WithImageUrl(Method.StoveProfileImagePath)
                .WithFooter("Develop by. 갱프")
                .Build();

            var component = new ComponentBuilder()
                .WithButton(label: "서버가입", customId: "SignUp", style: ButtonStyle.Success)
                .Build();

            //await Context.Channel.SendFileAsync(Method.StoveProfileImagePath, embed: embed, components: component);
            await Context.Channel.SendMessageAsync(embed: embed, components: component);
            await RespondAsync("표시완료", ephemeral: true);
        }

        [ComponentInteraction("SignUp")]
        public async Task SignUpAsync()
        {
            await Context.Interaction.RespondWithModalAsync<SingUpModalData>("SignUpModal");
        }

        public class SingUpModalData : IModal
        {
            public string Title => "가입정보입력";

            [InputLabel("로스트아크 캐릭터명을 입력해주세요.")]
            [ModalTextInput("NickName", placeholder: "본캐, 부캐 상관없습니다.", maxLength: 20)]
            public string NickName { get; set; } = "";

            [InputLabel("스토브 프로필 링크를 입력해주세요.")]
            [ModalTextInput("StoveUrl", placeholder: "예) https://profile.onstove.com/ko/123456", maxLength: 50)]
            public string StoveUrl { get; set; } = "";
        }

        [ModalInteraction("SignUpModal")]
        public async Task Modal_CertModal(SingUpModalData data)
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
            await RespondAsync("서버가입에 필요한 데이터를 확인 중입니다.", ephemeral: true);

            // 기준 충족 -> 프로필 조회 (네 기존 함수 그대로)
            var profile = await ProfileModule.GetSimpleProfile(m_NickNm);
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

            //var dbBanRow = await SupabaseClient.GetBanUserInfoAsync(user.Id.ToString(), m_NickNm);

            //if (dbBanRow != null)
            //{
            //    await ModifyOriginalResponseAsync(m => m.Content = "가입이 불가능한 계정입니다.");
            //    await user.KickAsync();
            //    return;
            //}

            DateTime dt = DateTime.UtcNow.AddHours(9);
            string m_joinDate = dt.ToString("yyyy-MM-dd"); // 2026-01-06
            string m_joinTime = dt.ToString("HH:mm");      // 01:23

            if (profile.보유캐릭_목록 == null)
            {
                await ModifyOriginalResponseAsync(m => m.Content = $"❌ 가입실패, 캐릭터명을 확인해주세요.");
                return;
            }

            var dbRow = await SupabaseClient.GetSingUpByUserIdAsync(Context.User.Id.ToString());

            if (dbRow == null)
            {
                var (ok, body) = await SupabaseClient.UpsertSingUpAsync(
                    userId: user.Id.ToString(),
                    stoveId: m_StoveId,
                    userNm: user.Username,
                    characters: profile.보유캐릭_목록,
                    joinDate: m_joinDate, // 2026-01-06
                    joinTime: m_joinTime // 오전 01:23
                    );

                if (!ok)
                {
                    var nosign = user.Guild.GetTextChannel(932836388217450556);
                    await ModifyOriginalResponseAsync(m => m.Content = $"❌ 가입실패, {nosign.Mention} 채널로 이동하여 문의해주세요.");
                    return;
                }

                foreach (var role in user.Guild.Roles)
                {
                    if (role.Name == profile.직업)
                    {
                        await user.AddRoleAsync(role);
                        break;
                    }
                }
                await user.AddRoleAsync(1457383863943954512);   // 루페온
                await user.RemoveRoleAsync(902213602889568316); // 미인증
                await ModifyOriginalResponseAsync(m => m.Content = "정상적으로 가입처리 되었습니다.");

                //#region 유저정보
                ////계정생성일
                //string creatDate = user.CreatedAt.ToString("yyyy-MM-dd");
                ////서버가입일
                //string JoinDate = user.JoinedAt.ToString();
                //DateTime dt = DateTime.Parse(JoinDate);
                //JoinDate = dt.ToShortDateString();

                ////디스코드정보
                //string s_disCord = string.Empty;
                //s_disCord = "``유저정보 :``" + user.Mention + " (" + user.Username + ")" + Environment.NewLine;
                //s_disCord += "``아 이 디 :``" + user.Id + Environment.NewLine;
                //s_disCord += "``계정생성일 :``" + creatDate + Environment.NewLine;
                //s_disCord += "``서버가입일 :``" + JoinDate;

                ////로아정보
                //string m_lostArk = string.Empty;
                //m_lostArk = "``레벨 :``" + Method.m_아이템레벨 + Environment.NewLine;
                //m_lostArk += "``캐릭터 :``" + m_NickNm + Environment.NewLine;
                //m_lostArk += "``클래스 :``" + Method.m_직업 + Environment.NewLine;
                //m_lostArk += "``서버 :``" + Method.m_서버;

                //string m_CharList = string.Empty;
                //m_CharList = "``보유캐릭 :``" + Method.m_보유캐릭;

                ////갱신정보
                //string m_renewal = string.Empty;
                //m_renewal = "가입일시 : " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString();
                //#endregion 유저정보

                //var Embed = new EmbedBuilder();
                //Embed.WithTitle("서버가입정보");
                //Embed.WithColor(Discord.Color.DarkTeal);
                //Embed.AddField("**Discord**", s_disCord, true);
                //Embed.AddField("**LostArk**", m_lostArk, true);
                //Embed.AddField("**CharList**", m_CharList, false);
                //Embed.WithFooter(m_renewal);

                //await user.Guild.GetTextChannel(903242262677454958).SendMessageAsync(embed: Embed.Build());
            }
        }
    }
}