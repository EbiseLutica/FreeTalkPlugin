using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BotBone.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreeTalkPlugin
{
    /// <summary>
    /// トークパターンをNotionから取得し、管理するクラス。
    /// </summary>
    public static class Topics
    {
        public static string[] Generics { get; private set; } = {};
        public static string[] SpringSeason { get; private set; } = {};
        public static string[] RainySeason { get; private set; } = {};
        public static string[] SummerSeason { get; private set; } = {};
        public static string[] AutumnSeason { get; private set; } = {};
        public static string[] WinterSeason { get; private set; } = {};
        public static string[] TanabataSeason { get; private set; } = {};
        public static string[] HalloweenSeason { get; private set; } = {};
        public static string[] HolidaySeason { get; private set; } = {};
        public static string[] YearEndSeason { get; private set; } = {};
        public static string[] NewYearSeason { get; private set; } = {};
        public static string[] Learned { get; private set; } = {};
        public static string[] FridayDayTopic { get; private set; } = {};
        public static string[] FridayNightTopic { get; private set; } = {};
        public static string[] SaturdayDayTopic { get; private set; } = {};
        public static string[] SaturdayNightTopic { get; private set; } = {};
        public static string[] SundayDayTopic { get; private set; } = {};
        public static string[] SundayNightTopic { get; private set; } = {};
        public static string[] MondayDayTopic { get; private set; } = {};

        public static int Length =>
            AutumnSeason.Length +
            Generics.Length +
            HalloweenSeason.Length +
            HolidaySeason.Length +
            NewYearSeason.Length +
            RainySeason.Length +
            SpringSeason.Length +
            SummerSeason.Length +
            TanabataSeason.Length +
            WinterSeason.Length +
            YearEndSeason.Length +
            FridayDayTopic.Length +
            FridayNightTopic.Length +
            SaturdayDayTopic.Length +
            SaturdayNightTopic.Length +
            SundayDayTopic.Length +
            SundayNightTopic.Length +
            MondayDayTopic.Length;
        
        public static async ValueTask FetchAsync()
        {
            if (isFetched) return;
            isFetched = true;
            var NOTION_API_TOKEN = Environment.GetEnvironmentVariable("NOTION_API_TOKEN") ?? throw new InvalidOperationException("Notion API Token を指定しろ");
            var DATABASE_ID = Environment.GetEnvironmentVariable("DATABASE_ID") ?? throw new InvalidOperationException("Database ID を指定しろ");

            var cli = new HttpClient();
            cli.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NOTION_API_TOKEN);
            cli.DefaultRequestHeaders.Add("Notion-Version", "2021-05-13");

            var nextCursor = default(string);
            var records = new List<PatternRecord>();
            
            while (true)
            {
                var sorts = new []{
                    new {
                        property = "タイプ",
                        direction = "ascending"
                    }
                };
                var body = JsonConvert.SerializeObject(nextCursor == null ? new { sorts } : new {
                    sorts,
                    start_cursor = nextCursor
                });
                var content = new StringContent(body, new UTF8Encoding(), "application/json");

                var res = await cli.PostAsync($"https://api.notion.com/v1/databases/{DATABASE_ID}/query", content);
                var dbText = await res.Content.ReadAsStringAsync();
                var dbJson = JObject.Parse(dbText);

                dbJson["results"].AsJEnumerable().OfType<JObject>().ToList().ForEach(data => {
                    var isEnabled = data["properties"]["有効化"]["checkbox"].Value<bool>();
                    if (!isEnabled) return;

                    var type = data["properties"]["タイプ"]["select"]["name"].Value<string>();
                    var message = data["properties"]["メッセージ"]["title"][0]["text"]["content"].Value<string>();
                    records.Add(new PatternRecord(message, type));
                });

                if (!dbJson["has_more"].Value<bool>()) break;

                nextCursor = dbJson["next_cursor"].Value<string>();
            };

            Generics = records.Where(r => r.type == "標準").Select(r => r.message).ToArray();
            SpringSeason = records.Where(r => r.type == "春").Select(r => r.message).ToArray();
            RainySeason = records.Where(r => r.type == "梅雨").Select(r => r.message).ToArray();
            SummerSeason = records.Where(r => r.type == "夏").Select(r => r.message).ToArray();
            AutumnSeason = records.Where(r => r.type == "秋").Select(r => r.message).ToArray();
            WinterSeason = records.Where(r => r.type == "冬").Select(r => r.message).ToArray();
            TanabataSeason = records.Where(r => r.type == "七夕").Select(r => r.message).ToArray();
            HalloweenSeason = records.Where(r => r.type == "ハロウィン").Select(r => r.message).ToArray();
            HolidaySeason = records.Where(r => r.type == "クリスマス").Select(r => r.message).ToArray();
            YearEndSeason = records.Where(r => r.type == "年末").Select(r => r.message).ToArray();
            NewYearSeason = records.Where(r => r.type == "年始").Select(r => r.message).ToArray();
            Learned = records.Where(r => r.type == "言葉を覚えた").Select(r => r.message).ToArray();
            FridayDayTopic = records.Where(r => r.type == "金曜昼").Select(r => r.message).ToArray();
            FridayNightTopic = records.Where(r => r.type == "金曜夜").Select(r => r.message).ToArray();
            SaturdayDayTopic = records.Where(r => r.type == "土曜昼").Select(r => r.message).ToArray();
            SaturdayNightTopic = records.Where(r => r.type == "土曜夜").Select(r => r.message).ToArray();
            SundayDayTopic = records.Where(r => r.type == "日曜昼").Select(r => r.message).ToArray();
            SundayNightTopic = records.Where(r => r.type == "日曜夜").Select(r => r.message).ToArray();
            MondayDayTopic = records.Where(r => r.type == "月曜昼").Select(r => r.message).ToArray();
        }

        private static bool isFetched = false;
    }
}