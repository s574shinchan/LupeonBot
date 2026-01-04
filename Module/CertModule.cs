using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot;
using LupeonBot.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Markup;


namespace LupeonBot.Module
{
    public partial class CertModule : InteractionModuleBase<SocketInteractionContext>
    {
        [ComponentInteraction("Complete")]
        public async Task CompleteAsync()
        {
            // 0) 관리자 체크
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            // 1) 버튼 응답 선점(타임아웃 방지) - 네가 하던 "진행중" 멘트
            await RespondAsync("인증에 필요한 작업이 진행 중입니다.", ephemeral: true);

            // 2) 대상 userid: 채널명 "인증채널_1234..."
            ulong s_userid = Convert.ToUInt64(Context.Channel.Name.Replace("인증채널_", string.Empty));
            SocketGuildUser target = admin.Guild.GetUser(s_userid);

            if (target == null)
            {
                await ModifyOriginalResponseAsync(m => m.Content = "대상 유저를 찾을 수 없습니다.");
                return;
            }

            // 3) 디스코드 표기명 (네 코드 그대로, 단 검사 대상은 target)
            string m_disCord = target.Username;
            string userNmTag = target.Mention;

            // 4) 생성일(계정 생성일) & 날짜/시간 포맷
            DateTime createDate = new DateTime(target.CreatedAt.Year, target.CreatedAt.Month, target.CreatedAt.Day);

            string m_시간 = DateTime.Now.Hour.ToString().PadLeft(2, '0') + ":" + DateTime.Now.Minute.ToString().PadLeft(2, '0');
            string m_일자 = DateTime.Now.Year + DateTime.Now.Month.ToString().PadLeft(2, '0') + DateTime.Now.Day.ToString().PadLeft(2, '0');
            m_일자 = Method.DateFormat(m_일자);

            DateTime toDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
            TimeSpan tmGap = toDay.Subtract(createDate);

            if (tmGap.TotalDays < 7)
            {
                await ModifyOriginalResponseAsync(m => m.Content = "❌ 계정 생성 7일 미만이라 인증 불가");
                return;
            }

            try
            {
                // 5) 신청캐릭 찾기: 최근 메시지에서 봇 메시지 "신청캐릭 :" 파싱
                string m_NickNm = "";
                string m_UserUrl = "";
                string m_StoveId = "";

                await foreach (RestMessage msg in Context.Channel.GetMessagesAsync(99).Flatten())
                {
                    // 신청자 링크 파싱 (유저가 보낸 메시지)
                    if (!msg.Author.IsBot && (msg.Content?.Contains("profile.onstove.com/ko/") ?? false))
                    {
                        if (Method.TryExtractStoveId(msg.Content, out var stoveId, out var url))
                        {
                            m_StoveId = stoveId;
                            m_UserUrl = url; // 실제 URL 저장
                        }
                        else
                        {
                            await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크가 올바르지 않습니다.");
                            return;
                        }
                    }

                    if (msg.Author.IsBot)
                    {
                        if (msg.Content.Contains("신청캐릭 :"))
                        {
                            string tmpMsg = msg.Content.Replace("\n", "^").Replace("신청캐릭 : ", string.Empty).Trim();
                            m_NickNm = Method.GetSplitString(tmpMsg, '^', 1);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(m_NickNm))
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 신청캐릭 정보를 찾지 못했습니다.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(m_UserUrl) || string.IsNullOrWhiteSpace(m_StoveId))
                {
                    // ✅ 너 조건: StoveId 없으면 저장 안함
                    await ModifyOriginalResponseAsync(m => m.Content = "❌ 스토브 프로필 링크(끝 숫자)를 찾지 못했습니다. 링크를 채널에 다시 올려주세요.");
                    return;
                }

                // 6) 프로필 + 보유캐릭 가져오기
                var siblings = await Method.GetCertProfile(m_NickNm);
                
                // 7) DB 기존 데이터 조회 (UserId 기준)
                var dbRow = await SupabaseClient.GetCertInfoByUserIdAsync(s_userid.ToString());

                if (dbRow != null)
                {
                    // 1) StoveId 비교
                    if (!string.Equals(dbRow.StoveId, m_StoveId, StringComparison.Ordinal))
                    {
                        await ModifyOriginalResponseAsync(m => m.Content = "❌ 저장된 정보와 신청자의 스토브 계정이 다릅니다.");
                        return;
                    }

                    // 2) DB 캐릭 배열
                    var dbChars = (dbRow.Character ?? "")
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // 3) 신청자 캐릭이 DB에 모두 포함되어 있는지 확인
                    foreach (var ch in dbChars)
                    {
                        if (!m_NickNm.Contains(ch))
                        {
                            await ModifyOriginalResponseAsync(m => m.Content = $"❌ 디스코드 정보는 일치, 신청캐릭 `{m_NickNm}` 이(가) DB 캐릭 목록에 존재하지 않습니다.");
                            return;
                        }
                    }

                    // 5) 전부 통과 → 이미 가입
                    await ModifyOriginalResponseAsync(m => m.Content = "❗ 이미 가입된 정보입니다.");
                    return;
                }

                string m_Context = string.Empty;
                m_Context += "인증대상 : ``'" + m_disCord + "(" + s_userid.ToString() + ")'``" + Environment.NewLine + Environment.NewLine;
                m_Context += "인증캐릭 : ``'" + m_NickNm + "'``" + Environment.NewLine + Environment.NewLine;
                m_Context += "위 정보로 거래소 인증이 완료되었습니다.";

                var s_embed = new EmbedBuilder();
                s_embed.WithAuthor("✅ 인증완료");
                s_embed.WithDescription(m_Context);
                s_embed.WithColor(Color.Green);
                s_embed.WithThumbnailUrl(target.GetAvatarUrl(ImageFormat.Auto));
                s_embed.WithFooter("ㆍ인증일시 : " + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString());

                // ✅ 2) 먼저 원본 응답 수정
                await ModifyOriginalResponseAsync(m => m.Content = "인증완료");
                await ModifyOriginalResponseAsync(m => m.Embed = s_embed.Build());

                if (dbRow == null)
                {
                    // ✅ 여기 도달하면 DB에 없음 → 추가(Upsert로 넣으면 됨)
                    var (ok, body) = await SupabaseClient.UpsertCertInfoAsync(
                        userId: s_userid.ToString(),
                        stoveId: m_StoveId,
                        userNm: m_disCord,
                        characters: Method.m_보유캐릭_배열,
                        joinDate: target.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"),
                        joinTime: target.CreatedAt.ToLocalTime().ToString("HH:mm"),
                        certDate: DateTime.Now.ToString("yyyy-MM-dd"),
                        certTime: DateTime.Now.ToString("HH:mm")
                    );

                    if (!ok)
                    {
                        await admin.Guild.GetTextChannel(693460815067611196).SendMessageAsync($"❌ DB 저장 실패\n```{body}```");

                        await ModifyOriginalResponseAsync(m => m.Content = $"❌ DB 저장 실패\n```{body}```");
                        return;
                    }

                    // ✅ 4) DB 저장 성공했으면 역할 부여
                    SocketRole mRole = admin.Guild.GetRole(Convert.ToUInt64(1264901726251647086));
                    await target.AddRoleAsync(mRole);

                    //// ✅ 5) 인증성공 메세지 로그(너 기존)
                    //string Safe(string v) => string.IsNullOrWhiteSpace(v) ? "-" : v;

                    //var embed = new EmbedBuilder()
                    //    .WithAuthor("✅ 인증 완료")
                    //    .WithColor(Color.Blue)
                    //    .AddField("닉네임", userNmTag, true)
                    //    .AddField("이름", $"`{Safe(m_disCord)}`", true)
                    //    .AddField("ID", $"`{s_userid}`", true)                        
                    //    .AddField("캐릭터", $"`{Safe(m_NickNm)}`", true)
                    //    .AddField("아이템 레벨", $"`{Safe(Method.m_아이템레벨)}`", true)
                    //    .AddField("전투력", $"`{Safe(Method.m_전투력)}`", true)
                    //    .AddField("인증일", $"`{DateTime.Now.ToString("yyyy-MM-dd")}`", true)
                    //    .AddField("인증시간", $"`{DateTime.Now.ToString("HH:mm")}`", true)
                    //    .AddField("인증자", $"`{admin}`", true)
                    //    .WithFooter("Develop by. 갱프");

                    //string m_complete = userNmTag + "인증목록에 추가 및 역할부여완료";
                    //await admin.Guild.GetTextChannel(693407092056522772).SendMessageAsync(text: m_complete, embed: embed.Build());
                    await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, Context.Channel.Name);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                try
                {
                    await ModifyOriginalResponseAsync(m => m.Content = "처리 중 오류가 발생했습니다.");
                }
                catch { }
            }
        }

        [ComponentInteraction("ExitCert")]
        public async Task CloseChannel()
        {
            // 0) 관리자 체크
            if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);
            await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, Context.Channel.Name);
        }

        [ComponentInteraction("CertTimeOut")]
        public async Task TimeOutAsync()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true); // 메시지 수정할 거라 defer

            var selectMenu = new SelectMenuBuilder()
                .WithCustomId("SelectRow")
                .WithPlaceholder("타임아웃 사유를 선택해주세요(최대 1개)")
                .WithMinValues(1)
                .WithMaxValues(1)
                .AddOption("무응답", "채널생성 후 10분 이상 무응답")
                .AddOption("장소틀림", "스샷 장소가 트리시온이 아님")
                .AddOption("시간틀림", "스샷의 시간이 신청시간 보다 지나치게 과거임")
                .AddOption("채팅틀림", "스샷에 인게임채팅을 잘못 입력함")
                .AddOption("잘못누름", "Miss")
                .AddOption("직접입력", "Self");

            var component = new ComponentBuilder()
                .WithSelectMenu(selectMenu)
                .WithButton("인증완료", customId: "Complete", style: ButtonStyle.Success)
                .WithButton("채널종료", customId: "ExitCert", style: ButtonStyle.Danger)
                .WithButton("타임아웃 확정", customId: "TimeOutConfirm", style: ButtonStyle.Primary);

            // ✅ "버튼을 눌렀던 그 메시지"를 수정해야 하니까:
            // Context.Interaction은 SocketMessageComponent로 들어온다.
            if (Context.Interaction is SocketMessageComponent smc)
                await smc.Message.ModifyAsync(m => m.Components = component.Build());
        }
        
        // values[0] 에 선택된 value가 들어옴
        [ComponentInteraction("SelectRow")]
        public async Task SelectRowAsync(string[] values)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            string reason = values?.FirstOrDefault() ?? "";

            if (reason == "Miss")
            {
                // 셀렉트 숨기기: 컴포넌트를 아예 없애버리면 됨
                if (Context.Interaction is SocketMessageComponent smc)
                    await smc.Message.ModifyAsync(m => m.Components = new ComponentBuilder()
                        .WithButton("인증완료", customId: "Complete", style: ButtonStyle.Success)
                        .WithButton("채널종료", customId: "ExitCert", style: ButtonStyle.Danger)
                        .WithButton("타임아웃 확정", customId: "TimeOutConfirm", style: ButtonStyle.Primary)
                        .Build());

                await RespondAsync("✅ 잘못누름 처리됨(메뉴 숨김)", ephemeral: true);
                return;
            }

            if (reason == "Self")
            {
                //var modal = new ModalBuilder()
                //    .WithTitle("타임아웃 사유 직접 입력")
                //    .WithCustomId("TimeoutReasonModal") // 모달 핸들러 id
                //    .AddTextInput(label: "사유", 
                //                  customId: "reason", 
                //                  placeholder: "타임아웃 사유를 입력하세요", 
                //                  style: TextInputStyle.Paragraph, 
                //                  maxLength: 200, 
                //                  required: true);

                //await RespondWithModalAsync(modal.Build());
                await Context.Interaction.RespondWithModalAsync<TimeoutReasonModal>("TimeoutReasonModal");
                return;
            }

            // ✅ 일반 사유(문장 value) → 저장
            CertState.TimeoutReasonByChannel[Context.Channel.Id] = reason;
            await RespondAsync($"✅ 타임아웃 사유 선택됨: `{reason}`", ephemeral: true);
        }

        [ModalInteraction("TimeoutReasonModal")]
        public async Task TimeoutReasonModalAsync(TimeoutReasonModal modal)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            string reason = (modal.Reason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                await RespondAsync("사유가 비어있습니다.", ephemeral: true);
                return;
            }

            CertState.TimeoutReasonByChannel[Context.Channel.Id] = reason;

            await RespondAsync($"✅ 직접입력 사유 저장됨: `{reason}`", ephemeral: true);
        }
        
        // 모달 데이터 바인딩용
        public class TimeoutReasonModal : IModal
        {
            public string Title => "타임아웃 사유 직접 입력";

            [InputLabel("사유")]
            [ModalTextInput("reason", TextInputStyle.Paragraph, placeholder: "타임아웃 사유를 입력하세요", maxLength: 200)]
            public string Reason { get; set; }
        }

        [ComponentInteraction("TimeOutConfirm")]
        public async Task TimeOutConfirmAsync()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 가능", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            var chId = Context.Channel.Id;
            CertState.TimeoutReasonByChannel.TryGetValue(chId, out var reason);
            reason ??= "(사유없음)";

            // ✅ 여기서 타임아웃/로그/역할회수/DM 등 네 처리 로직 수행
            // 예: 로그 채널에 reason 전송

            // 마지막에 채널 삭제
            await Method.DeleteChannelAsync(Context.Guild, (ITextChannel)Context.Channel, /*카테고리명*/ Context.Guild.GetCategoryChannel(((ITextChannel)Context.Channel).CategoryId ?? 0)?.Name ?? "");
            await FollowupAsync($"⛔ 타임아웃 처리 완료: `{reason}`", ephemeral: true);
        }

        public static class CertState
        {
            // 채널별 선택된 사유 저장
            public static ConcurrentDictionary<ulong, string> TimeoutReasonByChannel = new();
        }
    }
}
