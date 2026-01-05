using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DiscordBot.Program;

public sealed class RoleSlashModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("역할신청", "직업역할 선택 슬롯 표시")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task RoleSelectAsync()
    {
        if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
        {
            await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
            return;
        }

        ITextChannel textChannel = admin.Guild.GetTextChannel(1000806935634919454);

        var embed = new EmbedBuilder()
            .WithTitle("🎮 직업 역할 선택")
            .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
                             $"\n\n역할이 받아졌는지 확인 하는 방법" +
                             $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
            .WithColor(Color.Green)
            .WithFooter("Develop by. 갱프");

        await Context.Channel.SendMessageAsync(embed: embed.Build(), components: RoleMenuUi.BuildMenus());
        await RespondAsync("표시완료", ephemeral: true);
    }

    //[SlashCommand("역할신청", "직업역할 선택 버튼 표시")]
    //[DefaultMemberPermissions(GuildPermission.Administrator)]
    //public async Task RoleButtonAsync()
    //{
    //    if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
    //    {
    //        await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
    //        return;
    //    }

    //    ITextChannel textChannel = admin.Guild.GetTextChannel(1000806935634919454);

    //    #region 직업이모지
    //    Emote? GetEmote(string name) => EmoteCache.Emotes.TryGetValue(name, out var e) ? e : null;

    //    var m_워로드 = GetEmote("emblem_warlord");
    //    var m_버서커 = GetEmote("emblem_berserker");
    //    var m_디스트로이어 = GetEmote("emblem_destroyer");
    //    var m_홀리나이트 = GetEmote("emblem_holyknight");
    //    var m_슬레이어 = GetEmote("emblem_slayer");
    //    var m_발키리 = GetEmote("emblem_holyknight_female");
    //    var m_배틀마스터 = GetEmote("emblem_battlemaster");
    //    var m_인파이터 = GetEmote("emblem_infighter");
    //    var m_기공사 = GetEmote("emblem_soulmaster");
    //    var m_창술사 = GetEmote("emblem_lancemaster");
    //    var m_스트라이커 = GetEmote("emblem_striker");
    //    var m_브레이커 = GetEmote("emblem_infighter_male");
    //    var m_데빌헌터 = GetEmote("emblem_devilhunter");
    //    var m_블래스터 = GetEmote("emblem_blaster");
    //    var m_호크아이 = GetEmote("emblem_hawkeye");
    //    var m_건슬링어 = GetEmote("emblem_gunslinger");
    //    var m_스카우터 = GetEmote("emblem_scouter");
    //    var m_아르카나 = GetEmote("emblem_arcana");
    //    var m_서머너 = GetEmote("emblem_summoner");
    //    var m_바드 = GetEmote("emblem_bard");
    //    var m_소서리스 = GetEmote("emblem_sorceress");
    //    var m_블레이드 = GetEmote("emblem_blade");
    //    var m_데모닉 = GetEmote("emblem_demonic");
    //    var m_리퍼 = GetEmote("emblem_reaper");
    //    var m_소울이터 = GetEmote("emblem_souleater");
    //    var m_도화가 = GetEmote("emblem_artist");
    //    var m_기상술사 = GetEmote("emblem_weather_artist");
    //    var m_환수사 = GetEmote("emblem_alchemist");
    //    var m_가디언나이트 = GetEmote("emblem_dragon_knight");
    //    #endregion 직업이모지

    //    SocketRole? GetRoles(string name) => RoleCache.SocketRoles.TryGetValue(name, out var e) ? e : null;

    //    #region 슈샤이어 | 로헨델
    //    var Embed1 = new EmbedBuilder()
    //        .WithTitle("🎮 직업 역할 선택 • 슈샤이어 | 로헨델")
    //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
    //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
    //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
    //        .WithColor(Color.Green)
    //        .WithFooter("Develop by. 갱프")
    //        .Build();

    //    var Component1 = new ComponentBuilder()
    //        .WithButton(label: "버서커", customId: $"role:{GetRoles("버서커").Id}", style: ButtonStyle.Secondary, emote: m_버서커)
    //        .WithButton(label: "워로드", customId: $"role:{GetRoles("워로드").Id}", style: ButtonStyle.Secondary, emote: m_워로드)
    //        .WithButton(label: "디스트로이어", customId: $"role:{GetRoles("디스트로이어").Id}", style: ButtonStyle.Secondary, emote: m_디스트로이어)
    //        .WithButton(label: "홀리나이트", customId: $"role:{GetRoles("홀리나이트").Id}", style: ButtonStyle.Secondary, emote: m_홀리나이트)
    //        .WithButton(label: "슬레이어", customId: $"role:{GetRoles("슬레이어").Id}", style: ButtonStyle.Secondary, emote: m_슬레이어)
    //        .WithButton(label: "발키리", customId: $"role:{GetRoles("발키리").Id}", style: ButtonStyle.Secondary, emote: m_발키리)
    //        .WithButton(label: "아르카나", customId: $"role:{GetRoles("아르카나").Id}", style: ButtonStyle.Secondary, emote: m_아르카나)
    //        .WithButton(label: "서머너", customId: $"role:{GetRoles("서머너").Id}", style: ButtonStyle.Secondary, emote: m_서머너)
    //        .WithButton(label: "바드", customId: $"role:{GetRoles("바드").Id}", style: ButtonStyle.Secondary, emote: m_바드)
    //        .WithButton(label: "소서리스", customId: $"role:{GetRoles("소서리스").Id}", style: ButtonStyle.Secondary, emote: m_소서리스)
    //        .Build();

    //    await Context.Channel.SendMessageAsync(embed: Embed1, components: Component1);
    //    #endregion 슈샤이어 | 로헨델

    //    #region 애니츠 | 페이튼
    //    var Embed2 = new EmbedBuilder()
    //        .WithTitle("🎮 직업 역할 선택 • 애니츠 | 페이튼")
    //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
    //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
    //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
    //        .WithColor(Color.Green)
    //        .WithFooter("Develop by. 갱프")
    //        .Build();

    //    var Component2 = new ComponentBuilder()
    //        .WithButton(label: "배틀마스터", customId: $"role:{GetRoles("배틀마스터").Id}", style: ButtonStyle.Secondary, emote: m_배틀마스터)
    //        .WithButton(label: "인파이터", customId: $"role:{GetRoles("인파이터").Id}", style: ButtonStyle.Secondary, emote: m_인파이터)
    //        .WithButton(label: "기공사", customId: $"role:{GetRoles("기공사").Id}", style: ButtonStyle.Secondary, emote: m_기공사)
    //        .WithButton(label: "창술사", customId: $"role:{GetRoles("창술사").Id}", style: ButtonStyle.Secondary, emote: m_창술사)
    //        .WithButton(label: "스트라이커", customId: $"role:{GetRoles("스트라이커").Id}", style: ButtonStyle.Secondary, emote: m_스트라이커)
    //        .WithButton(label: "브레이커", customId: $"role:{GetRoles("브레이커").Id}", style: ButtonStyle.Secondary, emote: m_브레이커)
    //        .WithButton(label: "블레이드", customId: $"role:{GetRoles("블레이드").Id}", style: ButtonStyle.Secondary, emote: m_블레이드)
    //        .WithButton(label: "데모닉", customId: $"role:{GetRoles("데모닉").Id}", style: ButtonStyle.Secondary, emote: m_데모닉)
    //        .WithButton(label: "리퍼", customId: $"role:{GetRoles("리퍼").Id}", style: ButtonStyle.Secondary, emote: m_리퍼)
    //        .WithButton(label: "소울이터", customId: $"role:{GetRoles("소울이터").Id}", style: ButtonStyle.Secondary, emote: m_소울이터)
    //        .Build();

    //    await Context.Channel.SendMessageAsync(embed: Embed2, components: Component2);
    //    #endregion 애니츠 | 페이튼

    //    #region 아르데타인 | 스페셜리스트
    //    var Embed3 = new EmbedBuilder()
    //        .WithTitle("🎮 직업 역할 선택 • 아르데타인 | 스페셜리스트")
    //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
    //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
    //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
    //        .WithColor(Color.Green)
    //        .WithFooter("Develop by. 갱프")
    //        .Build();

    //    var Component3 = new ComponentBuilder()
    //        .WithButton(label: "호크아이", customId: $"role:{GetRoles("호크아이").Id}", style: ButtonStyle.Secondary, emote: m_호크아이)
    //        .WithButton(label: "데빌헌터", customId: $"role:{GetRoles("데빌헌터").Id}", style: ButtonStyle.Secondary, emote: m_데빌헌터)
    //        .WithButton(label: "블래스터", customId: $"role:{GetRoles("블래스터").Id}", style: ButtonStyle.Secondary, emote: m_블래스터)
    //        .WithButton(label: "스카우터", customId: $"role:{GetRoles("스카우터").Id}", style: ButtonStyle.Secondary, emote: m_스카우터)
    //        .WithButton(label: "건슬링어", customId: $"role:{GetRoles("건슬링어").Id}", style: ButtonStyle.Secondary, emote: m_건슬링어)
    //        .WithButton(label: "도화가", customId: $"role:{GetRoles("도화가").Id}", style: ButtonStyle.Secondary, emote: m_도화가)
    //        .WithButton(label: "기상술사", customId: $"role:{GetRoles("기상술사").Id}", style: ButtonStyle.Secondary, emote: m_기상술사)
    //        .WithButton(label: "환수사", customId: $"role:{GetRoles("환수사").Id}", style: ButtonStyle.Secondary, emote: m_환수사)
    //        .Build();

    //    await Context.Channel.SendMessageAsync(embed: Embed3, components: Component3);
    //    #endregion 아르데타인 | 스페셜리스트

    //    #region 가디언나이트
    //    var GK = new EmbedBuilder()
    //        .WithTitle("🎮 직업 역할 선택 • 가디언나이트")
    //        .WithDescription($"아래 선택상자에서 원하는 직업 역할을 선택하세요." +
    //                         $"\n\n역할이 받아졌는지 확인 하는 방법" +
    //                         $"\n{textChannel.Mention} 채널에서 역할확인 버튼을 눌러서 확인가능")
    //        .WithColor(Color.Green)
    //        .WithFooter("Develop by. 갱프")
    //        .Build();

    //    var Cp_GK = new ComponentBuilder()
    //        .WithButton(label: "가디언나이트", customId: $"role:{GetRoles("가디언나이트").Id}", style: ButtonStyle.Secondary, emote: m_가디언나이트)
    //        .Build();

    //    await Context.Channel.SendMessageAsync(embed: GK, components: Cp_GK);
    //    #endregion 가디언나이트

    //    await RespondAsync("표시완료", ephemeral: true);
    //}
}

public sealed class RoleComponentModule : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("role:*")]
    public async Task HandleRoleButton(string roleIdText)
    {
        if (Context.User is not SocketGuildUser user)
        {
            await RespondAsync("서버에서만 사용 가능합니다.", ephemeral: true);
            return;
        }

        if (!ulong.TryParse(roleIdText, out var roleId))
        {
            await RespondAsync("역할 정보가 올바르지 않습니다.", ephemeral: true);
            return;
        }

        var role = user.Guild.GetRole(roleId);
        if (role == null)
        {
            await RespondAsync("해당 역할을 찾을 수 없습니다.", ephemeral: true);
            return;
        }

        var hasRole = user.Roles.Any(r => r.Id == roleId);

        if (hasRole)
        {
            // 보호 역할 자체는 제거 못하게 하고 싶으면 이것도 추가 가능
            if (ExcludedRoleIds.Contains(roleId))
            {
                await RespondAsync("❌ 이 역할은 해제할 수 없습니다.", ephemeral: true);
                return;
            }

            int remain = CountRemovableRoles(user, roleId);
            if (remain == 0)
            {
                await RespondAsync("❌ 최소 1개의 직업 역할은 유지해야 합니다.", ephemeral: true);
                return;
            }

            await user.RemoveRoleAsync(role);
            await RespondAsync($"❌ `{role.Name}` 역할이 제거되었습니다.", ephemeral: true);
            return;
        }

        await user.AddRoleAsync(role);
        await RespondAsync($"✅ `{role.Name}` 역할이 부여되었습니다.", ephemeral: true);
    }

    [ComponentInteraction("SelectRow:*")]
    public async Task SelectRowAsync(string values)
    {
        await DeferAsync(ephemeral: true); // ✅ 필수

        if (Context.User is not SocketGuildUser user)
        {
            await FollowupAsync("❌ 길드 유저만 사용 가능합니다.", ephemeral: true);
            return;
        }

        // ✅ 선택값 꺼내기 (SelectMenu는 SocketMessageComponent로 들어옴)
        if (Context.Interaction is not SocketMessageComponent smc)
        {
            await FollowupAsync("❌ 컴포넌트 상호작용이 아닙니다.", ephemeral: true);
            return;
        }

        var picked = smc.Data.Values.FirstOrDefault(); // MaxValues(1)이면 1개만 들어옴
        if (string.IsNullOrWhiteSpace(picked))
        {
            await FollowupAsync("❌ 선택값이 없습니다.", ephemeral: true);
            return;
        }

        // ✅ 역할 이름 = 직업명 으로 바로 찾기
        var role = Context.Guild.Roles.FirstOrDefault(r => r.Name.Equals(picked, StringComparison.OrdinalIgnoreCase));

        // ✅ 있으면 제거 / 없으면 부여
        if (user.Roles.Any(r => r.Id == role.Id))
        {
            await user.RemoveRoleAsync(role);
            await FollowupAsync($"❌ `{role.Name}` 역할이 제거되었습니다.", ephemeral: true);
        }
        else
        {
            await user.AddRoleAsync(role);
            await FollowupAsync($"✅ `{role.Name}` 역할이 부여되었습니다.", ephemeral: true);
        }
    }

    // ✅ 삭제 제한 계산에서 제외할 역할들 (예: 인증/필수/운영진 등)
    // 여기에 네가 지정한 역할 ID를 넣어.
    private static readonly HashSet<ulong> ExcludedRoleIds = new()
    {
        653491548482174996,  // 메인관리자
        557635038607573002,  // 관리자
        667614334998020096,  // 봇
        688802446943715404,  // 작대기1
        688803133153214536,  // 작대기2
        1264901726251647086, // 거래소
        58000335490252801,   // 거래인증
        595607967030837285,  // 판매인증
        602169127926366239,  // 작대기3
        600948355501260800,  // 니트로
        1190024494144831589, // 공란
        893431274964922380,  // 하트
        900235242219118592,  // 별표
        999954837301116988,  // 치타
        1407670667670716497, // 노랑
        1370337289213050930, // OrangeYellow
        900240165598031932,  // Emerald
        1370336719941144676, // SkyBlue
        900236308356669440,  // Purple
        914463919567945759,  // RoseGold
        1370336310119890984, // Silver
        1299736324890431518, // 임시역할
    };

    private static int CountRemovableRoles(SocketGuildUser user, ulong roleIdToRemove)
    {
        // @everyone(=guild.Id)는 항상 있으니 제외
        // ExcludedRoleIds는 제외
        // 지금 삭제하려는 roleIdToRemove도 제외하고 나머지 역할이 몇 개인지 센다
        return user.Roles.Count(r =>
            r.Id != user.Guild.Id &&              // @everyone 제외
            !ExcludedRoleIds.Contains(r.Id) &&    // 보호 역할 제외
            r.Id != roleIdToRemove                // 지금 삭제하려는 역할 제외
        );
    }
}

public sealed class RoleCheckModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("역할확인", "본인이 가지고 있는 역할들을 확인 할 수 있는 버튼표시")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task RoleCheck()
    {
        if (Context.User is not SocketGuildUser admin || !admin.GuildPermissions.Administrator)
        {
            await RespondAsync("❌ 관리자만 사용할 수 있습니다.", ephemeral: true);
            return;
        }

        string mValue = string.Empty;
        string Emote = "<:pdiamond:907957436483248159>";

        mValue = Emote + "아래의 역할확인 버튼을 클릭" + Environment.NewLine
               + Emote + "본인이 가지고 있는 역할들을 확인 할 수 있습니다.";

        var Embed = new EmbedBuilder()
            .WithAuthor("[역할확인]")
            .WithColor(Discord.Color.LightOrange)
            .WithDescription(mValue)
            .WithFooter("Develop by. 갱프")
            .Build(); ;

        var component = new ComponentBuilder()
            .WithButton(label: "역할확인", customId: "ChkRoles", style: ButtonStyle.Success)
            .Build();

        await Context.Channel.SendMessageAsync(embed: Embed, components: component);
        await RespondAsync("정상적으로 공지표시완료", ephemeral: true);
    }

    [ComponentInteraction("ChkRoles")] // 버튼 customId 예시
    public async Task ShowMyRolesAsync()
    {
        if (Context.User is not SocketGuildUser gu)
        {
            await RespondAsync("길드 유저만 가능합니다.", ephemeral: true);
            return;
        }

        // ✅ 예전 코드와 동일한 리스트들
        var 슈샤이어 = new List<string>();
        var 로헨델 = new List<string>();
        var 애니츠 = new List<string>();
        var 아르데타인 = new List<string>();
        var 페이튼 = new List<string>();
        var 스페셜리스트 = new List<string>();
        var 가디언나이트 = new List<string>();

        var 거래역할 = new List<string>();
        var 관리역할 = new List<string>();
        var 그외역할 = new List<string>();

        foreach (var role in gu.Roles)
        {
            if (role.IsEveryone) continue;
            if (IgnoreRoleIds.Contains(role.Id)) continue;

            // ✅ 직업 분류
            if (Job_Shushaire.Contains(role.Id))
                슈샤이어.Add(role.Mention);
            else if (Job_Rohendel.Contains(role.Id))
                로헨델.Add(role.Mention);
            else if (Job_Anihc.Contains(role.Id))
                애니츠.Add(role.Mention);
            else if (Job_Arthetine.Contains(role.Id))
                아르데타인.Add(role.Mention);
            else if (Job_Faten.Contains(role.Id))
                페이튼.Add(role.Mention);
            else if (Job_Specialist.Contains(role.Id))
                스페셜리스트.Add(role.Mention);
            else if (Job_DragonKnight.Contains(role.Id))
                가디언나이트.Add(role.Mention);

            // ✅ 거래/관리/그외
            else if (TradeRoleIds.Contains(role.Id) || string.Equals(role.Name, "거래소", StringComparison.OrdinalIgnoreCase))
                거래역할.Add(role.Mention);
            else if (AdminRoleIds.Contains(role.Id))
                관리역할.Add(role.Mention);
            else
                그외역할.Add(role.Mention);
        }

        // ✅ 예전 코드와 “출력 규칙 동일”하게 문자열 만들기
        string mJob = BuildLikeLegacy(슈샤이어)
                    + BuildLikeLegacy(로헨델)
                    + BuildLikeLegacy(애니츠)
                    + BuildLikeLegacy(아르데타인)
                    + BuildLikeLegacy(페이튼)
                    + BuildLikeLegacy(스페셜리스트)
                    + BuildLikeLegacy(가디언나이트);

        string mRole = BuildLikeLegacy(거래역할);
        string mEtc = BuildLikeLegacy(그외역할);
        string mAdmin = BuildLikeLegacy(관리역할);

        string mValue = "";

        if (!string.IsNullOrEmpty(mJob))
            mValue = "직업역할" + Environment.NewLine + TrimLegacy(mJob);

        if (!string.IsNullOrEmpty(mRole))
            mValue += Environment.NewLine + Environment.NewLine + "거래역할" + Environment.NewLine + TrimLegacy(mRole);

        if (!string.IsNullOrEmpty(mEtc))
            mValue += Environment.NewLine + Environment.NewLine + "그외역할" + Environment.NewLine + TrimLegacy(mEtc);

        if (!string.IsNullOrEmpty(mAdmin))
            mValue += Environment.NewLine + Environment.NewLine + "관리역할" + Environment.NewLine + TrimLegacy(mAdmin);

        var embed = new EmbedBuilder()
            .WithAuthor("보유 중인 역할")
            .WithDescription(mValue)
            .WithColor(Color.Purple)
            .WithFooter($"Develop by. 갱프　　　　　확인일시: {DateTime.Now:yyyy-MM-dd HH:mm}")
            .Build();

        await RespondAsync(embed: embed, ephemeral: true);
    }

    private static readonly HashSet<ulong> IgnoreRoleIds = new()
    {
        //513799663086862336,
        688802446943715404,
        688803133153214536,
        602169127926366239
    };

    // ✅ 직업/거래/관리 ID를 그대로 switch 대신 HashSet으로 분류
    private static readonly HashSet<ulong> Job_Shushaire = new()
    {
        557631665728389153, // 버서커
        557631664986259472, // 디트
        557631664470360099, // 워로드
        639121866992123974, // 홀리나이트
        1065618299116863508,// 슬레이어
        1387703156833783888,// 발키리
    };

    private static readonly HashSet<ulong> Job_Rohendel = new()
    {
        557631664365371407, // 바드
        557631663102754817, // 서머너
        557631663576842241, // 아르카나
        855711579290075176, // 소서리스
    };

    private static readonly HashSet<ulong> Job_Anihc = new()
    {
        557631661525696522, // 배마
        557631661966229524, // 인파
        557631662284865537, // 기공
        571807949513687041, // 창술
        789750930811256882, // 스트
        1188409166793019513,// 브커
    };

    private static readonly HashSet<ulong> Job_Specialist = new()
    {
        921699659498524722, // 도화가
        995318441915461732, // 기상술사
        1317479085328306196,// 환술사
    };

    private static readonly HashSet<ulong> Job_Faten = new()
    {
        601680900379377664, // 블레이드
        601680858876739634, // 데모닉
        737845189640716319, // 리퍼
        1124738844135264266,// 소울이터
    };

    private static readonly HashSet<ulong> Job_Arthetine = new()
    {
        789750805896495104, // 데빌헌터
        557628187870232577, // 블래스터
        557631620467916810, // 호크
        725431052495224854, // 스카
        557631659109908492, // 건슬
    };

    private static readonly HashSet<ulong> Job_DragonKnight = new()
    {
        1449635262400299051, // 가디언나이트
    };

    private static readonly HashSet<ulong> TradeRoleIds = new()
    {
        1056749315818782761,
        580003354902528011,
        595607967030837285
    };

    private static readonly HashSet<ulong> AdminRoleIds = new()
    {
        653491548482174996,
        557635038607573002,
        900235242219118592,
        667614334998020096,
        999954837301116988
    };

    private static string BuildLikeLegacy(List<string> list)
    {
        if (list == null || list.Count == 0) return "";

        var sb = new StringBuilder();
        for (int idx = 1; idx <= list.Count; idx++)
        {
            if (idx == list.Count)
                sb.Append(list[idx - 1]).Append(", ").Append(Environment.NewLine);
            else
                sb.Append(list[idx - 1]).Append(", ");
        }
        return sb.ToString();
    }

    // ✅ 예전: Substring(0, Length-2) 와 동일 효과
    // 마지막에 붙은 ", \n" 또는 ", " 를 제거
    private static string TrimLegacy(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // 예전코드가 Length-2라서 \r\n 환경에 따라 애매했음.
        // 여기선 안전하게 끝의 ", " 와 줄바꿈을 제거
        return s.TrimEnd('\r', '\n', ' ', ',');
    }

}

/// <summary>
/// 메뉴 생성 코드를 공용으로 빼둔 곳 (핵심)
/// </summary>
public static class RoleMenuUi
{
    public static MessageComponent BuildMenus()
    {
        Emote? GetEmote(string name) => EmoteCache.Emotes.TryGetValue(name, out var e) ? e : null;

        var m_워로드 = GetEmote("emblem_warlord");
        var m_버서커 = GetEmote("emblem_berserker");
        var m_디스트로이어 = GetEmote("emblem_destroyer");
        var m_홀리나이트 = GetEmote("emblem_holyknight");
        var m_슬레이어 = GetEmote("emblem_slayer");
        var m_발키리 = GetEmote("emblem_holyknight_female");
        var m_배틀마스터 = GetEmote("emblem_battlemaster");
        var m_인파이터 = GetEmote("emblem_infighter");
        var m_기공사 = GetEmote("emblem_soulmaster");
        var m_창술사 = GetEmote("emblem_lancemaster");
        var m_스트라이커 = GetEmote("emblem_striker");
        var m_브레이커 = GetEmote("emblem_infighter_male");
        var m_데빌헌터 = GetEmote("emblem_devilhunter");
        var m_블래스터 = GetEmote("emblem_blaster");
        var m_호크아이 = GetEmote("emblem_hawkeye");
        var m_건슬링어 = GetEmote("emblem_gunslinger");
        var m_스카우터 = GetEmote("emblem_scouter");
        var m_아르카나 = GetEmote("emblem_arcana");
        var m_서머너 = GetEmote("emblem_summoner");
        var m_바드 = GetEmote("emblem_bard");
        var m_소서리스 = GetEmote("emblem_sorceress");
        var m_블레이드 = GetEmote("emblem_blade");
        var m_데모닉 = GetEmote("emblem_demonic");
        var m_리퍼 = GetEmote("emblem_reaper");
        var m_소울이터 = GetEmote("emblem_souleater");
        var m_도화가 = GetEmote("emblem_artist");
        var m_기상술사 = GetEmote("emblem_weather_artist");
        var m_환수사 = GetEmote("emblem_alchemist");
        var m_가디언나이트 = GetEmote("emblem_dragon_knight");

        var selectMenu = new SelectMenuBuilder()
            .AddOption(emote: m_버서커, label: "버서커", value: "버서커")
            .AddOption(emote: m_디스트로이어, label: "디스트로이어", value: "디스트로이어")
            .AddOption(emote: m_워로드, label: "워로드", value: "워로드")
            .AddOption(emote: m_홀리나이트, label: "홀리나이트", value: "홀리나이트")
            .AddOption(emote: m_슬레이어, label: "슬레이어", value: "슬레이어")
            .AddOption(emote: m_발키리, label: "발키리", value: "발키리")
            .AddOption(emote: m_아르카나, label: "아르카나", value: "아르카나")
            .AddOption(emote: m_서머너, label: "서머너", value: "서머너")
            .AddOption(emote: m_바드, label: "바드", value: "바드")
            .AddOption(emote: m_소서리스, label: "소서리스", value: "소서리스")
            .AddOption(emote: m_배틀마스터, label: "배틀마스터", value: "배틀마스터")
            .AddOption(emote: m_인파이터, label: "인파이터", value: "인파이터")
            .AddOption(emote: m_기공사, label: "기공사", value: "기공사")
            .AddOption(emote: m_창술사, label: "창술사", value: "창술사")
            .AddOption(emote: m_스트라이커, label: "스트라이커", value: "스트라이커")
            .AddOption(emote: m_브레이커, label: "브레이커", value: "브레이커")
            .WithCustomId("SelectRow:1")
            .WithMinValues(1)
            .WithMaxValues(1)
            .WithPlaceholder("원하는 직업을 선택하여 역할을 받을 수 있습니다.");

        var selectMenu2 = new SelectMenuBuilder()
            .AddOption(emote: m_블레이드, label: "블레이드", value: "블레이드")
            .AddOption(emote: m_데모닉, label: "데모닉", value: "데모닉")
            .AddOption(emote: m_리퍼, label: "리퍼", value: "리퍼")
            .AddOption(emote: m_소울이터, label: "소울이터", value: "소울이터")
            .AddOption(emote: m_호크아이, label: "호크아이", value: "호크아이")
            .AddOption(emote: m_데빌헌터, label: "데빌헌터", value: "데빌헌터")
            .AddOption(emote: m_블래스터, label: "블래스터", value: "블래스터")
            .AddOption(emote: m_스카우터, label: "스카우터", value: "스카우터")
            .AddOption(emote: m_건슬링어, label: "건슬링어", value: "건슬링어")
            .AddOption(emote: m_도화가, label: "도화가", value: "도화가")
            .AddOption(emote: m_기상술사, label: "기상술사", value: "기상술사")
            .AddOption(emote: m_환수사, label: "환수사", value: "환수사")
            .AddOption(emote: m_가디언나이트, label: "가디언나이트", value: "가디언나이트")
            .WithCustomId("SelectRow:2")
            .WithMinValues(1)
            .WithMaxValues(1)
            .WithPlaceholder("원하는 직업을 선택하여 역할을 받을 수 있습니다.");

        return new ComponentBuilder()
            .WithSelectMenu(selectMenu)
            .WithSelectMenu(selectMenu2)
            .Build();
    }
}
