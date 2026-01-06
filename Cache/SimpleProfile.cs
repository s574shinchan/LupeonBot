using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LupeonBot.Module
{
    public sealed class SimpleProfile
    {
        public string Url { get; init; } = "";
        public string 서버 { get; init; } = "";
        public string 아이템레벨 { get; init; } = "";
        public string 원정대레벨 { get; init; } = "";
        public string 전투력 { get; init; } = "";
        public string 아크패시브 { get; init; } = "";
        public string 길드 { get; init; } = "";
        public string 칭호 { get; init; } = "";
        public string 직업 { get; init; } = "";
        public string 각인 { get; init; } = "";
        public string 보유캐릭 { get; init; } = "";
        public List<string> 보유캐릭_목록 { get; init; } = new();
        public string 보유캐릭수 { get; init; } = "";
        public string 캐릭터명 { get; init; } = "";
        public string ImgLink { get; init; } = "";
    }
}
