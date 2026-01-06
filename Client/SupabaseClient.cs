using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LupeonBot.Client
{
    public static class SupabaseClient
    {
        private static string _url = "";
        private static string _key = "";
        private static HttpClient? _client;

        // ✅ 초기화 함수 (한 번만 호출)
        public static void Init(string url, string key)
        {
            _url = url.TrimEnd('/');
            _key = key;

            _client = new HttpClient();
            _client.BaseAddress = new Uri(_url + "/");
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add("apikey", _key);
            _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _key);
            _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private static HttpClient Client => _client ?? throw new Exception("SupabaseClient.Init()가 호출되지 않았습니다.");

        public sealed class SignUpInfoRow
        {
            public string? UserId { get; set; }
            public string? StoveId { get; set; }
            public string? UserNm { get; set; }
            public List<string> Character { get; set; }
            public string? JoinDate { get; set; }
            public string? JoinTime { get; set; }
        }
        public sealed class CertInfoRow
        {
            public string? UserId { get; set; }
            public string? StoveId { get; set; }
            public string? UserNm { get; set; }
            public List<string> Character { get; set; }
            public string? JoinDate { get; set; }
            public string? JoinTime { get; set; }
            public string? CertDate { get; set; }
            public string? CertTime { get; set; }
        }

        public sealed class BanUserRow
        {
            public string? UserId { get; set; }
            public string? StoveId { get; set; }
            public string? UserNm { get; set; }
            public string? Character { get; set; }
        }

        /// <summary>
        /// 디스코드id로 가입정보 테이블 조회
        /// </summary>
        /// <param name="userId : 디스코드id"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<SignUpInfoRow?> GetSingUpByUserIdAsync(string userId)
        {
            var res = await Client.GetAsync($"rest/v1/signup?userid=eq.{Uri.EscapeDataString(userId)}&limit=1");

            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Supabase SELECT 실패\n{body}");

            var list = JsonSerializer.Deserialize<List<SignUpInfoRow>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (list != null && list.Count > 0) ? list[0] : null;
        }

        /// <summary>
        /// 디스코드 id랑 캐릭명으로 벤테이블조회
        /// 아직 사용 x
        /// </summary>
        /// <param name="userId : 디스코드 id"></param>
        /// <param name="character : 캐릭명"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<BanUserRow?> GetBanUserInfoAsync(string userId, string character)
        {
            var url = $"rest/v1/banuser?userid=eq.{Uri.EscapeDataString(userId)}&character=eq.{Uri.EscapeDataString(character)}";
            var res = await Client.GetAsync(url);

            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Supabase SELECT 실패\n{body}");

            var list = JsonSerializer.Deserialize<List<BanUserRow>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (list != null && list.Count > 0) ? list[0] : null;
        }

        /// <summary>
        /// 가입정보 테이블에 추가
        /// </summary>
        /// <param name="userId : 디스코드ID"></param>
        /// <param name="stoveId : stoveId"></param>
        /// <param name="userNm : 사용자명"></param>
        /// <param name="characters : 보유캐릭"></param>
        /// <param name="joinDate : 가입일"></param>
        /// <param name="joinTime : 가입시간"></param>
        /// <returns></returns>
        public static async Task<(bool ok, string body)> UpsertSingUpAsync(string userId, string stoveId, string userNm, List<string> characters, string joinDate, string joinTime)
        {
            // ✅ 컬럼명은 테이블 그대로 (UserId, UserUrl, ...)
            var payload = new[]
            {
                new {
                    userid = userId,
                    stoveid = stoveId,
                    usernm = userNm,
                    character = characters,
                    joindate = joinDate,
                    jointime = joinTime
                }
            };

            var json = JsonSerializer.Serialize(payload);

            var req = new HttpRequestMessage(HttpMethod.Post, "rest/v1/signup?on_conflict=userid,stoveid");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // ✅ Upsert + 결과 반환
            req.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");

            var res = await Client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            return (res.IsSuccessStatusCode, body);
        }

        /// <summary>
        /// 디스코드id로 인증정보 테이블 조회
        /// </summary>
        /// <param name="userId : 디스코드id"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<CertInfoRow?> GetCertInfoByUserIdAsync(string userId)
        {
            var res = await Client.GetAsync($"rest/v1/certinfo?userid=eq.{Uri.EscapeDataString(userId)}&limit=1");

            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Supabase SELECT 실패\n{body}");

            var list = JsonSerializer.Deserialize<List<CertInfoRow>>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return (list != null && list.Count > 0) ? list[0] : null;
        }

        /// <summary>
        /// 인증테이블에 데이터 추가
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="stoveId"></param>
        /// <param name="userNm"></param>
        /// <param name="characters"></param>
        /// <param name="joinDate"></param>
        /// <param name="joinTime"></param>
        /// <param name="certDate"></param>
        /// <param name="certTime"></param>
        /// <returns></returns>
        public static async Task<(bool ok, string body)> UpsertCertInfoAsync(string userId, string stoveId, string userNm, List<string> characters,
                                                                             string joinDate, string joinTime, string certDate, string certTime)
        {
            // ✅ 컬럼명은 테이블 그대로 (UserId, UserUrl, ...)
            var payload = new[]
            {
            new {
                userid = userId,
                stoveid = stoveId,
                usernm = userNm,
                character = characters,
                joindate = joinDate,
                jointime = joinTime,
                certdate = certDate,
                certtime = certTime
            }
        };

            var json = JsonSerializer.Serialize(payload);

            var req = new HttpRequestMessage(HttpMethod.Post, "rest/v1/certinfo?on_conflict=userid,stoveid");
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            // ✅ Upsert + 결과 반환
            req.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");

            var res = await Client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            return (res.IsSuccessStatusCode, body);
        }

        /// <summary>
        /// 인증갱신시 특정 컬럼만 업데이트
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="stoveId"></param>
        /// <param name="characters"></param>
        /// <param name="certDate"></param>
        /// <param name="certTime"></param>
        /// <returns></returns>
        public static async Task<(bool ok, string body)> UpdateCertOnlyAsync(string userId, string stoveId, List<string> characters, string certDate, string certTime)
        {
            var payload = new
            {
                character = characters,
                certdate = certDate,
                certtime = certTime
            };

            var json = JsonSerializer.Serialize(payload);
            var url = $"rest/v1/certinfo?userid=eq.{Uri.EscapeDataString(userId)}&stoveid=eq.{Uri.EscapeDataString(stoveId)}";

            var req = new HttpRequestMessage(HttpMethod.Patch, url);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            req.Headers.TryAddWithoutValidation("Prefer", "return=representation");

            var res = await Client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            return (res.IsSuccessStatusCode, body);
        }

        /// <summary>
        /// 서버나가기하면 가입정보 있으면 삭제처리
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="Exception"></exception>
        public static async Task<bool> DeleteSignUpByUserIdAsync(string userId)
        {
            if (_client == null)
                throw new InvalidOperationException("SupabaseClient.Init() 먼저 호출하세요.");

            var req = new HttpRequestMessage(HttpMethod.Delete, $"rest/v1/signup?userid=eq.{Uri.EscapeDataString(userId)}");

            var res = await _client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Supabase DELETE 실패\n{body}");

            return true;
        }

        /// <summary>
        /// 디스코드id 또는 캐릭터명으로 인증내역검색
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<CertInfoRow?> FindCertInfoAsync(string input)
        {
            input = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(input)) return null;

            HttpResponseMessage res;

            // 1) 디스코드ID면 userid로
            if (ulong.TryParse(input, out _))
            {
                res = await Client.GetAsync($"rest/v1/certinfo?userid=eq.{Uri.EscapeDataString(input)}&limit=1");
            }
            else
            {
                // 2) 캐릭터명이면 배열 contains로
                // cs.{...}는 {}가 특수라서 URL 인코딩 권장
                var name = input; // 표시용
                var encoded = Uri.EscapeDataString($"{{{name}}}");

                res = await Client.GetAsync($"rest/v1/certinfo?character=cs.{encoded}&limit=1");
            }

            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new Exception($"조회 실패\n{body}");

            var list = JsonSerializer.Deserialize<List<CertInfoRow>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return list?.FirstOrDefault();
        }
    }
}
