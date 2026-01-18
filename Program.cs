using Discord;
using Discord.API;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using LupeonBot.Client;
using LupeonBot.Module;
using LupeonBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Supabase.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;


namespace DiscordBot
{
    public class Program
    {
        private static readonly DiscordSocketConfig config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMembers,
            AlwaysDownloadUsers = true,
            LogLevel = LogSeverity.Verbose
        };

        public static DiscordSocketClient client = new DiscordSocketClient(config);

        public static void Main() => new Program().MainAsync().GetAwaiter().GetResult();

        // ✅ 슬래시 커맨드 서비스
        InteractionService? publicSvc;
        InteractionService? lupeonSvc;
        private static IServiceProvider? _services;
        private StickyRefreshService? _sticky;
        private bool _stickyInitialized;

        public static string BotToken = string.Empty;
        public static string LostArkJwt = string.Empty; // ✅ 로아 Open API JWT

        private bool _registered;

        public async Task MainAsync()
        {
            client.Log += Log;
            client.Ready += Ready;

            // 봇 토큰
            BotToken = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "LupeonBot_Token.txt")).Trim();

            // ✅ 로아 JWT 토큰 (추가)
            LostArkJwt = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "LostArkToken.txt")).Trim();

            SupabaseClient.Init(
                File.ReadAllText("SupabaseUrl.txt").Trim(),
                File.ReadAllText("SupabaseServiceRole.txt").Trim()
            );

            // ✅ InteractionService 생성
            publicSvc = new InteractionService(client.Rest);
            lupeonSvc = new InteractionService(client.Rest);

            // ✅ Interaction 처리 이벤트 연결
            client.InteractionCreated += HandleInteraction;

            client.UserJoined += UserJoined;
            client.UserLeft += UserLeft;

            _services = ConfigureServices();

            await client.LoginAsync(TokenType.Bot, BotToken);
            await client.StartAsync();
            await client.SetGameAsync(string.Empty, type: ActivityType.Playing);
            await Task.Delay(-1); // 프로그램 종료시까지 태스크 유지
        }

        private async Task UserLeft(SocketGuild arg1, SocketUser arg2)
        {
            var guild = client.GetGuild(513799663086862336);
            var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
            await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);

            var dbRow = await SupabaseClient.GetSingUpByUserIdAsync(arg2.Id.ToString());

            if (dbRow != null)
            {
                await SupabaseClient.DeleteSignUpByUserIdAsync(arg2.Id.ToString());
            }
        }

        private async Task UserJoined(SocketGuildUser arg)
        {
            var guild = client.GetGuild(513799663086862336);

            var m_UnSignUp = guild.GetRole(902213602889568316);
            await arg.AddRoleAsync(m_UnSignUp);

            var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
            await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
        }

        public async Task Ready()
        {
            if (_registered) return; // ✅ Ready 중복 방지
            _registered = true;

            await publicSvc.AddModuleAsync<ProfileSerachModule>(_services);
            await publicSvc.RegisterCommandsGloballyAsync();

            ulong guildId = 513799663086862336;
            var asm = Assembly.GetEntryAssembly()!;
            var moduleTypes = asm.GetTypes()
                .Where(t => !t.IsAbstract)
                .Where(t => typeof(InteractionModuleBase<SocketInteractionContext>).IsAssignableFrom(t))
                .Where(t => t != typeof(ProfileSerachModule));
            
            foreach (var t in moduleTypes)
            {
                await lupeonSvc.AddModuleAsync(t, _services);
            }

            await lupeonSvc.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);

            var maintSvc = new MaintenanceNoticeService(client);
            maintSvc.Start();

            var eventSvc = new EventNoticeService(client);
            eventSvc.Start();


            // ✅ 그 길드에 봇이 들어가 있을 때만
            if (client.GetGuild(guildId) == null)
                return;

            InitStickyIfNeeded();

            //ulong[] fullGuilds = { 513799663086862336, 222222222222222222 }; // 전부 보일 서버들            
            //foreach (var gid in fullGuilds)
            //    await fullSvc.RegisterCommandsToGuildAsync(gid);
            //await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services: null);
            //var modules = _interactions.Modules.Select(m => m.Name);

            //ulong guildId = 513799663086862336;
            //await _interactions.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);

            foreach (var guild in client.Guilds)
            {
                switch (guild.Id)
                {
                    case 513799663086862336:
                        RoleCache.SocketRoles.Clear();

                        foreach (var role in guild.Roles)
                        {
                            RoleCache.SocketRoles[role.Name] = role;
                        }

                        var voiceChannel = guild.GetVoiceChannel(1457106002553081958);
                        await voiceChannel.ModifyAsync(x => x.Name = "All Members : " + guild.MemberCount);
                        break;
                    case 624936203229069344:
                        EmoteCache.Emotes.Clear();

                        foreach (var emote in guild.Emotes)
                        {
                            EmoteCache.Emotes[emote.Name] = emote;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private async Task HandleInteraction(SocketInteraction interaction)
        {
            try
            {
                var ctx = new SocketInteractionContext(client, interaction);
                var r1 = await lupeonSvc.ExecuteCommandAsync(ctx, _services);
                if (!r1.IsSuccess && r1.Error == InteractionCommandError.UnknownCommand)
                    await publicSvc.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                try
                {
                    if (!interaction.HasResponded)
                        await interaction.RespondAsync("처리 중 오류가 발생했습니다.", ephemeral: true);
                }
                catch { }
            }
        }

        public static class EmoteCache
        {
            public static Dictionary<string, Emote> Emotes { get; } = new();
        }

        public static class RoleCache
        {
            public static Dictionary<string, SocketRole> SocketRoles { get; } = new();
        }

        private static IServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton<InteractionService>()
                .AddSingleton<HttpClient>()
                // 기타 싱글톤
                .BuildServiceProvider();
        }

        private void InitStickyIfNeeded()
        {
            if (_stickyInitialized) return;
            _stickyInitialized = true;

            ulong TARGET_GUILD_ID = 513799663086862336;
            _sticky ??= new StickyRefreshService(client, TARGET_GUILD_ID);

            #region 아이템팝니다, 골드팝니다.
            string mAutoMsg =
                $"<#1058371903762468934>을 확인 후 반드시 지켜주세요.\n\n" +
                $"- 거래시 판매자가 골드 및 아이템을 보유 중인지 확인 후 거래하시기 바랍니다.\n\n" +
                $"- 거래도중 의심이 든다면 <#884395336959918100>로 신고해주시기 바랍니다.\n\n" +
                $"- 판매글은 3줄 이하로 작성해주세요.\n\n"+
                $"- 거래소 갱신이 진행 중입니다. 미갱신자는 확인 후 갱신하시기 바랍니다.";

            // 아이템팝니다.
            _sticky.UpsertChannel(
                channelId: 661860451323215873UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mAutoMsg)
                    .WithColor(Color.Blue)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );

            // 골드팝니다.
            _sticky.UpsertChannel(
                channelId: 693357562044874802UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mAutoMsg)
                    .WithColor(Color.Blue)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );
            #endregion

            #region 보석교환
            string mJemMsg =
                $"- 빈줄 포함 10줄 이하로 글을 작성해주세요.\n" +
                $"- 보석 변환 글 작성 시 아래의 5가지를 반드시 포함해야합니다.\n\n" +
                $"- 본캐 레벨 / 원정대 레벨\n" +
                $"- 담보 유무\n" +
                $"- 보석 변환 가능한 티어 / 레벨\n" +
                $"- 본캐 레벨 / 원정대 레벨\n" +
                $"- 보석 변환 비용\n\n" +
                $"- 보석 변환 글 재작성 시 이전 글을 반드시 삭제하고 올려주세요.\n\n"+
                $"- 거래소 갱신이 진행 중입니다. 미갱신자는 확인 후 갱신하시기 바랍니다.";

            _sticky.UpsertChannel(
                channelId: 837673368945557535UL,
                embedFactory: () => new EmbedBuilder()
                    .WithTitle("📌 자동공지")
                    .WithDescription(mJemMsg)
                    .WithColor(Color.Orange)
                    .WithFooter("Develop by. 갱프")
                    .Build()
            );
            #endregion

            _sticky.Start();
        }

        public Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}
