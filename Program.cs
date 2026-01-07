using Discord;
using Discord.API;
using Discord.Commands;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using LupeonBot.Client;
using LupeonBot.Module;
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
        private static IServiceProvider _services;

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

            // 길드에 등록된 커맨드 목록 가져오기
            var guildCommands = await _client.Rest.GetGuildApplicationCommands(_client.CurrentUser.Id, guildId);
            
            // 이름이 "프로필"인 길드 커맨드만 삭제
            foreach (var cmd in guildCommands.Where(c => c.Name == "프로필"))
            {
                await cmd.DeleteAsync();
            }

            await lupeonSvc.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            await lupeonSvc.RegisterCommandsToGuildAsync(guildId, deleteMissing: true);

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

        public Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }
    }
}

