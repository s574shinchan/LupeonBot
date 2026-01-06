using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using LupeonBot.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static LupeonBot.Client.SupabaseClient;

namespace LupeonBot.Module
{
    public class AdminModule : InteractionModuleBase<SocketInteractionContext>
    {
        private const ulong BanLogChannelId = 598534025380102169;

        [SlashCommand("추방", "추방대상과 사유를 입력하여 추방합니다. (관리자전용)")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task UserBanAsync(
            [Summary(description: "추방할 대상자")] string? 추방대상 = null,
            [Summary(description: "추방 사유")] string? 추방사유 = null)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            // ✅ 입력 검증
            if (string.IsNullOrWhiteSpace(추방대상))
            {
                await RespondAsync("추방대상은 반드시 입력해야 합니다.", ephemeral: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(추방사유))
            {
                await RespondAsync("추방사유는 반드시 입력해야 합니다.", ephemeral: true);
                return;
            }

            string reason = 추방사유.Trim();

            ulong targetId;
            string displayName;
            string mentionText;
            string? iconUrl = null;

            if (!ulong.TryParse(추방대상!.Trim(), out targetId))
            {
                await RespondAsync("유저ID는 숫자만 입력해주세요.", ephemeral: true);
                return;
            }

            // 가능하면 유저 정보도 가져와서 표시(실패해도 밴은 가능)
            var fetched = await Context.Client.GetUserAsync(targetId);
            if (fetched != null)
            {
                displayName = $"{fetched.Username}#{fetched.Discriminator}";
                mentionText = fetched.Mention;
                iconUrl = fetched.GetAvatarUrl(ImageFormat.Auto) ?? fetched.GetDefaultAvatarUrl();
            }
            else
            {
                displayName = targetId.ToString();
                mentionText = targetId.ToString();
            }

            // ✅ 핵심: ID로 밴 (서버에 없어도 100% 가능)
            await Context.Guild.AddBanAsync(targetId, pruneDays: 7, reason: reason);

            // ✅ 커맨드 응답
            await RespondAsync($"{displayName} 차단완료\n차단사유 : {reason}");

            // ✅ Embed 로그 (기존 포맷 유지)
            string s_disCord = "";
            s_disCord += $"**``유  저 : ``**{mentionText} ({displayName})\n";
            s_disCord += $"**``아이디 : ``**{targetId}";

            var banEmbed = new EmbedBuilder()
                .WithColor((Discord.Color)System.Drawing.Color.IndianRed)
                .WithAuthor(displayName, iconUrl)
                .WithTitle("**추방 및 차단(BAN)**")
                .AddField("ㅤ", s_disCord)
                .AddField("ㅤ", $"**``사유 : ``**{reason}")
                .AddField("ㅤ", "**``해당 조치에 대한 소명 및 이의제기는 문의 및 신고 채널을 이용해주시기 바랍니다. ``**", true)
                .WithFooter($"Develop by. 갱프　　　　　조치일시 : {DateTime.Now:yyyy-MM-dd HH:mm}");

            var logCh = Context.Guild.GetTextChannel(BanLogChannelId);
            if (logCh != null)
                await logCh.SendMessageAsync(embed: banEmbed.Build());

        }

        [SlashCommand("인증내역조회", "인증된 정보를 조회합니다. (관리자전용)")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task GetCertUserInfoAsync([Summary(description: "디스코드 ID 또는 캐릭터명")] string? 조회대상 = null)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            조회대상 = (조회대상 ?? "").Trim();
            if (string.IsNullOrWhiteSpace(조회대상))
            {
                await RespondAsync("조회할 **디스코드 ID** 또는 **캐릭터명**을 입력해주세요.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            CertInfoRow? row;

            try
            {
                row = await FindCertInfoAsync(조회대상);
            }
            catch (Exception ex)
            {
                await FollowupAsync($"조회 중 오류가 발생했습니다.\n```{ex.Message}```", ephemeral: true);
                return;
            }

            if (row == null)
            {
                await FollowupAsync($"조회 결과가 없습니다. (입력: `{조회대상}`)", ephemeral: true);
                return;
            }

            // 출력용 문자열 구성
            string userId = row.UserId ?? "(없음)";
            string userNm = string.IsNullOrWhiteSpace(row.UserNm) ? "(없음)" : row.UserNm!;
            string stoveId = string.IsNullOrWhiteSpace(row.StoveId) ? "(없음)" : row.StoveId!;
            string chars = (row.Character?.Any() == true) ? string.Join("/", row.Character) : "(없음)";

            string joinDt = $"{row.JoinDate ?? "-"} {row.JoinTime ?? ""}".Trim();
            string certDt = $"{row.CertDate ?? "-"} {row.CertTime ?? ""}".Trim();

            // 디스코드 멘션 가능한지 시도 (userid가 ulong이면)
            string mention = userId;
            if (ulong.TryParse(userId, out var uid))
            {
                mention = $"<@{uid}>";
            }

            var eb = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("✅ 인증내역 조회 결과")
                .AddField("캐릭터명", $"{조회대상}", inline: true)
                .AddField("StoveId", stoveId, inline: true)
                .AddField("\u200b", "\u200b", inline: false)
                .AddField("Discord", $"{mention}", inline: true)
                .AddField("사용자명", userNm, inline: true)
                .AddField("UserId", $"`{userId}`", inline: true)
                .AddField("가입일시", joinDt, inline: true)
                .AddField("인증일시", certDt, inline: true)
                .AddField("보유캐릭", chars, inline: false)
                .WithFooter("Develop by. 갱프");

            await FollowupAsync(embed: eb.Build(), ephemeral: true);
        }

        //[SlashCommand("역할일괄부여", "메인역할인 '루페온' 역할을 모든 유저에게 일괄로 부여합니다. (미인증제외, 관리자전용)")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SetMainRoleAddByAllUser()
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            const ulong excludeRole = 902213602889568316;
            const ulong targetRole = 1457383863943954512;

            // ✅ KST 타임존 (윈도우: Korea Standard Time / 리눅스: Asia/Seoul)
            TimeZoneInfo kst;
            try { kst = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"); }
            catch { kst = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul"); }

            string NowKst() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, kst).ToString("yyyy-MM-dd HH:mm:ss");

            List<SocketGuildUser> targetUsers = gu.Guild.Users
                .Where(u => !u.IsBot)
                .Where(u =>
                {
                    var roleIds = u.Roles.Select(r => r.Id).ToHashSet();
                    return !roleIds.Contains(excludeRole)
                        && !roleIds.Contains(targetRole);
                })
                .ToList();
            int total = targetUsers.Count;
            int processed = 0, added = 0, skipped = 0, failed = 0;

            // ✅ 실패 유저 모아두기 (멘션용)
            List<SocketGuildUser> failedUsers = new List<SocketGuildUser>();

            EmbedBuilder BuildProgressEmbed(string desc, Color color)
            {
                return new EmbedBuilder()
                    .WithTitle("🔄 루페온 역할 지급 진행중")
                    .WithColor(color)
                    .WithDescription(desc)
                    .AddField("전체 유저", total, true)
                    .AddField("처리됨", processed, true)
                    .AddField("지급 성공", added, true)
                    .AddField("스킵", skipped, true)
                    .AddField("실패", failed, true)
                    .WithFooter($"시간: {NowKst()}");
            }

            var msg = await Context.Channel.SendMessageAsync(embed: BuildProgressEmbed("시작합니다...", Color.Orange).Build());

            foreach (var user in targetUsers)
            {
                processed++;

                try
                {
                    await user.AddRoleAsync(targetRole);
                    added++;

                    // Rate limit 보호
                    await Task.Delay(500);
                }
                catch
                {
                    failed++;
                    failedUsers.Add(user);
                }

                // 5명마다 진행 로그 갱신
                if (processed % 5 == 0 || processed == total)
                {
                    await msg.ModifyAsync(m =>
                        m.Embed = BuildProgressEmbed($"처리 중... `{processed}/{total}`", Discord.Color.Orange).Build()
                    );
                }
            }

            // 완료
            var done = new EmbedBuilder()
                .WithTitle("✅ 루페온 역할 지급 완료")
                .WithColor(Color.Green)
                .WithDescription("모든 작업이 완료되었습니다.")
                .AddField("전체 유저", total, true)
                .AddField("지급 성공", added, true)
                .AddField("스킵", skipped, true)
                .AddField("실패", failed, true)
                .WithFooter($"완료: {NowKst()}")
                .Build();

            await msg.ModifyAsync(m => m.Embed = done);

            // ✅ 실패 유저 멘션을 특정 채널로 알림
            const ulong notifyChannelId = 1292061651092246580UL;
            var ch = Context.Guild.GetTextChannel(notifyChannelId);

            if (ch != null && failedUsers.Count > 0)
            {
                // 멘션 스팸 방지: 너무 많으면 여러 메시지로 쪼개기 (예: 20명 단위)
                const int chunkSize = 20;

                for (int i = 0; i < failedUsers.Count; i += chunkSize)
                {
                    var chunk = failedUsers.Skip(i).Take(chunkSize);
                    var mentions = string.Join(" ", chunk.Select(u => u.Mention));

                    await ch.SendMessageAsync(
                        $"⚠️ **루페온 역할 부여 실패 유저 목록** (KST {NowKst()})\n{mentions}"
                    );
                }
            }

            // 슬래시커맨드 응답(관리자에게만)
            await FollowupAsync(
                $"완료. 대상:{total}, 성공:{added}, 실패:{failed} (KST {NowKst()})",
                ephemeral: true
            );
        }

        // ✅ 채널 권한에서 제거할 역할들(예: 역할 ID로 관리)
        // HashTable 느낌으로 쓰고 싶으면 Dictionary<string, ulong> 로도 가능
        private static readonly HashSet<ulong> RolesToRemove = new()
        {
            557631665728389153,
            557631664986259472,
            557631664470360099,
            639121866992123974,
            1065618299116863508,
            1387703156833783888,
            557631664365371407,
            557631663102754817,
            855711579290075176,
            557631661525696522,
            557631661966229524,
            557631662284865537,
            571807949513687041,
            789750930811256882,
            1188409166793019513,
            601680900379377664,
            737845189640716319,
            1124738844135264266,
            557631659109908492,
            557628187870232577,
            557631620467916810,
            725431052495224854,
            789750805896495104,
            921699659498524722,
            995318441915461732,
            1317479085328306196,
            1449635262400299051,
            557631663576842241,
            601680858876739634
        };

        // ✅ 역할명(권장: 역할ID로 박아두는게 더 안전)
        private const ulong TargetRoleId = 1457383863943954512;       //루페온

        [SlashCommand("채널정리", "입력한 채널 직업역할제거, 루페온역할 부여")]
        [DefaultMemberPermissions(GuildPermission.Administrator)]
        public async Task SetChannelRoleAsync([Summary("카테고리id", "정리할 카테고리 ID")] string categoryId)
        {
            if (Context.User is not SocketGuildUser gu || !gu.GuildPermissions.Administrator)
            {
                await RespondAsync("❌ 관리자만 사용 가능합니다.", ephemeral: true);
                return;
            }

            await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(categoryId, out ulong catId))
            {
                await FollowupAsync("❌ 카테고리 ID는 숫자만 입력해주세요.", ephemeral: true);
                return;
            }

            var category = Context.Guild.GetCategoryChannel(catId);
            if (category == null)
            {
                await FollowupAsync($"❌ 카테고리 ID `{categoryId}` 를 찾을 수 없습니다.", ephemeral: true);
                return;
            }

            var guild = Context.Guild;
            var targetRole = guild.GetRole(TargetRoleId);
            var only3 = new OverwritePermissions(viewChannel: PermValue.Allow, 
                                                 sendMessages: PermValue.Allow, 
                                                 readMessageHistory: PermValue.Allow);
            
            int totalRemoved = 0;
            int okChannels = 0;

            // ⭐ 실패 채널 기록용
            List<string> failedChannels = new();

            var channels = category.Channels;
            foreach (var ch in channels)
            {
                try
                {
                    var roleOverwrites = ch.PermissionOverwrites
                        .Where(x => x.TargetType == PermissionTarget.Role)
                        .ToList();
                        
                    foreach (var ow in roleOverwrites)
                    {
                        if (!RolesToRemove.Contains(ow.TargetId))
                            continue;
                            
                        if (ow.TargetId == TargetRoleId)
                            continue;

                        var role = guild.GetRole(ow.TargetId);
                        if (role == null) continue;

                        await ch.RemovePermissionOverwriteAsync(role);
                        totalRemoved++;
                    }

                    await ch.AddPermissionOverwriteAsync(targetRole, only3);

                    okChannels++;
                }
                catch (Exception)
                {
                    // ❗ 실패한 채널 멘션 기록
                    failedChannels.Add($"<#{ch.Id}> ({ch.Name})");
                    // 계속 진행
                }
            }

            // 결과 메시지 구성
            string result =
                $"✅ 카테고리 `{category.Name}` 정리 완료\n" +
                $"- 대상 채널: {channels.Count}개\n" +
                $"- 처리 성공: {okChannels}개\n" +
                $"- 제거된 overwrite: {totalRemoved}개";

            if (failedChannels.Count > 0)
            {
                result +=
                    "\n\n⚠️ 실패한 채널:\n" +
                    string.Join(", ", failedChannels);
            }

            await FollowupAsync(result, ephemeral: true);
        }
    }
}



