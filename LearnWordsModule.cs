using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using BotBone.Core;
using BotBone.Core.Api;
using BotBone.Core.Modules;
using Citrine.Core.Modules;

namespace FreeTalkPlugin
{
    public class LearnWordsModule : ModuleBase, ICommand
    {
        public List<string> NgWords { get; }

        public string Name => "free-talk";

        public string[] Aliases => new string[0];

        public bool IgnoreCase => true;

        public PermissionFlag Permission => PermissionFlag.Any;

        public string Usage => "/free-talk gen\n　Generate a text\n/free-talk config get <key>\n/free-talk config set <key> <value>";

        public string Description => "FreeTalkPlugin Utility Command for administrators.";

        public LearnWordsModule()
        {
            // 下ネタ回避のために Citrine を参照する
            NgWords = new HarassmentHandlerModule().NgWords.ToList();
            // 15分に1投稿
            timer = new Timer(1000 * 60 * 30);
            timer.Elapsed += OnElapsed;
            timer.Start();
            logger.Info($"Installed LearnWordsModule with {Topics.Length} sentences");
        }

        public async Task<string> OnActivatedAsync(ICommandSender sender, Server core, IShell shell, string[] args, string body)
        {
            this.shell = this.shell ?? shell;
            this.core = this.core ?? core;

            var subCommand = args.Length >= 1 ? args[0] : throw new CommandException();
            var myStorage = core.GetMyStorage();
            switch (subCommand.ToLowerInvariant())
            {
                case "gen":
                case "generate":
                    return MapVariables(GenerateText());
                case "zoniac":
                    return GetJapaneseZodiacOf(DateTime.Now.Year);
                case "word":
                    return string.Join(", ", GenerateChoices(myStorage));
                case "config":
                    {
                        if (!sender.IsAdmin) throw new AdminOnlyException();
                        var getset = args.Length >= 2 ? args[1].ToLowerInvariant() : throw new CommandException();
                        var key = args.Length >= 3 ? args[2] : throw new CommandException();
                        var value = args.Length >= 4 ? args[3] : "";

                        switch (key.ToLowerInvariant())
                        {
                            case "pollratio":
                                if (getset == "set")
                                {
                                    if (int.TryParse(value, out var i))
                                    {
                                        myStorage.Set("freetalk.config.pollRatio", i);
                                        return "ok";
                                    }
                                    return "interger value is required";
                                }
                                return myStorage.Get("freetalk.config.pollRatio", 30).ToString();
                            default:
                                return $"{key} is not a valid sub-command";
                        }
                    }
                default:
                    throw new CommandException();
            }
        }

        private async void OnElapsed(object sender, ElapsedEventArgs e)
        {
            if (shell == null || core == null) return;
            var storage = core.GetMyStorage();

            var recent = storage.Get("freetalk.recent", new List<string>());
            var pollRatio = storage.Get("freetalk.config.pollRatio", 30);
            var now = DateTime.Now;
            var today = now.Date;
            var hour = now.Hour;
            var lastBreakfastAt = storage.Get("freetalk.lastBreakfastAt", DateTime.MinValue.Date);
            var lastLunchAt = storage.Get("freetalk.lastLunchAt", DateTime.MinValue.Date);
            var lastSnackTimeAt = storage.Get("freetalk.lastSnackTimeAt", DateTime.MinValue.Date);
            var lastDinnerAt = storage.Get("freetalk.lastDinnerAt", DateTime.MinValue.Date);
            // ごはん投票は、毎tickごと抽選する
            var win = core.Random.Next(100) < pollRatio;

            if (win && lastBreakfastAt != today && hour >= 7 && hour <= 10)
            {
                await shell.PostAsync("朝ごはんどうしよ", choices: GenerateChoices(storage));
                storage.Set("freetalk.lastBreakfastAt", now.Date);
            }
            else if (win && lastLunchAt != today && hour >= 11 && hour <= 13)
            {
                await shell.PostAsync("昼ごはんが決まらないので投票", choices: GenerateChoices(storage));
                storage.Set("freetalk.lastLunchAt", now.Date);
            }
            else if (win && lastSnackTimeAt != today && hour == 15)
            {
                await shell.PostAsync("おやつの時間〜. 何食べよう", choices: GenerateChoices(storage));
                storage.Set("freetalk.lastSnackTimeAt", now.Date);
            }
            else if (win && lastDinnerAt != today && hour >= 17 && hour <= 19)
            {
                await shell.PostAsync("夜, 何食べようかな", choices: GenerateChoices(storage));
                storage.Set("freetalk.lastDinnerAt", now.Date);
            }
            else
            {
                string s;
                do
                {
                    s = GenerateText();
                } while (recent.Contains(s));

                await shell.PostAsync(MapVariables(s));

                recent.Add(s);
                storage.Set("freetalk.recent", recent.TakeLast(Topics.Generics.Length).ToList());
            }
        }

        private List<string> GenerateChoices(UserStorage.UserRecord storage)
        {
            var nouns = storage.Get("freetalk.nouns", new List<string>()).ToList();
            var verbs = storage.Get("freetalk.verbs", new List<string>()).ToList();
            var adjectives = storage.Get("freetalk.adjectives", new List<string>()).ToList();

            return Enumerable.Range(0, core.Random.Next(2, 5)).Select(_ =>
            {
                var dice = core.Random.Next(100);
                return
                    dice < 50 ? nouns.Random() :
                    dice < 75 ? adjectives.Random() + nouns.Random() :
                    verbs.Random() + nouns.Random();
            }).ToList();
        }

        public override async Task<bool> OnTimelineAsync(IPost n, IShell shell, Server core)
        {
            this.shell = this.shell ?? shell;
            this.core = this.core ?? core;

            var key = Environment.GetEnvironmentVariable("YAHOO_API_KEY");
            var t = n.Text;

            var now = DateTimeOffset.UtcNow;

            var myStorage = core.GetMyStorage();
            var last = myStorage.Get("freetalk.lastLearnedAt", DateTimeOffset.MinValue);

            // 前回学習から30秒経過していなければ学習しない
            if ((now - last).TotalSeconds < 30) return false;

            // 本文無し / メンションを含む / NGワードを含む　なら学習しない
            if (t == null || t.ContainsMentions() || ContainsNgWord(t)) return false;
            // フォロワー限定/ダイレクトなどであれば学習しない
            if (n.Visiblity != Visibility.Public && n.Visiblity == Visibility.Limited) return false;
            // スラッシュコマンドであれば学習しない
            if (t.StartsWith("/")) return false;

            // URLを除外
            t = Regex.Replace(t, @"^https?\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$", "");

            if (key == null)
            {
                logger.Warn("Yahoo API Key is not set!");
                return false;
            }

            // 形態素解析 API を呼ぶ
            var res = await Server.Http.PostAsync("https://jlp.yahooapis.jp/MAService/V1/parse", new FormUrlEncodedContent(Helper.BuildKeyValues(
                ("appId", key),
                ("sentence", t),
                ("response", "feature")
            )));
            
            var text = await res.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(text);
            myStorage.Set("freetalk.lastLearnedAt", now);

            // XML を解析しデータ処理
            (string? surface, string? reading, string? pos, string? baseform, string? group1, string? group2)[] result = doc.Descendants("{urn:yahoo:jp:jlp}word")
                .Select(w =>
                {
                    var feature = w.Element("{urn:yahoo:jp:jlp}feature")?.Value ?? "";
                    var splitted = feature.Split(',');

                    return (
                        splitted.Length >= 4 && splitted[3] == "*" ? null : splitted[3],
                        splitted.Length >= 5 && splitted[4] == "*" ? null : splitted[4],
                        splitted.Length >= 1 && splitted[0] == "*" ? null : splitted[0],
                        splitted.Length >= 6 && splitted[5] == "*" ? null : splitted[5],
                        splitted.Length >= 2 && splitted[1] == "*" ? null : splitted[1],
                        splitted.Length >= 3 && splitted[2] == "*" ? null : splitted[2]
                    );
                }).ToArray();

            var nouns = myStorage.Get("freetalk.nouns", new List<string>()).ToList();
            var verbs = myStorage.Get("freetalk.verbs", new List<string>()).ToList();
            var adjectives = myStorage.Get("freetalk.adjectives", new List<string>()).ToList();
            string? prefix = null;
            string? noun = null;

            for (var i = 0; i < result.Length; i++)
            {
                // ルール1: 名詞が連続する場合、その連続する名詞は1語として記憶する
                // ルール2: 接頭辞がある場合、登録時にくっつけて登録する
                // ルール3: next が接尾辞を指す場合、登録時にくっつけて登録する
                // ルール4: 名詞+助動詞(する)を検出した場合、動詞として登録する
                var current = result[i];
                var next = i < result.Length - 1 ? result[i + 1] : default;

                void RegisterNoun()
                {
                    if (noun != null)
                    {
                        noun += current.pos == "接尾辞" ? current.baseform : null;
                        nouns.Add(noun);
                        logger.Info($"Remembered '{noun}' as a noun.");
                        prefix = null;
                        noun = null;
                    }
                }

                if (current.pos == "形容詞" && current.baseform != null)
                {
                    RegisterNoun();
                    adjectives.Add(current.baseform);
                    logger.Info($"Remembered '{current.baseform}' as an adjective.");
                }
                else if (current.pos == "接頭辞")
                {
                    prefix = (prefix ?? "") + current.baseform;
                }
                else if (current.pos == "名詞")
                {
                    noun = noun + current.baseform;
                }
                else if (current.pos == "助動詞" && current.baseform == "する")
                {
                    noun = noun + current.baseform;
                    verbs.Add("サ変する," + noun);
                    logger.Info($"Remembered '{noun}' as a verb.");
                    prefix = null;
                    noun = null;
                }
                else if (current.pos == "動詞")
                {
                    RegisterNoun();
                    var verb = prefix + current.baseform;
                    verbs.Add(current.group1 + "," + verb);
                    logger.Info($"Remembered '{verb}' as a verb.");
                    prefix = null;
                    noun = null;
                }
                else
                {
                    RegisterNoun();
                }
            }

            if (noun != null)
            {
                nouns.Add(noun);
                logger.Info($"Remembered '{noun}' as a noun.");
            }

            myStorage.Set("freetalk.nouns", nouns.Distinct().ToList());
            myStorage.Set("freetalk.verbs", verbs.Distinct().ToList());
            myStorage.Set("freetalk.adjectives", adjectives.Distinct().ToList());

            return false;
        }

        private string GenerateText()
        {
            var now = DateTime.Now;
            var year = now.Year;
            var month = now.Month;
            var day = now.Day;

            // 特殊トピック
            var specialTopic =
                month == 7 && day <= 7 ? Topics.TanabataSeason :
                month == 10 && day == 31 ? Topics.HalloweenSeason :
                month == 10 && (day == 24 || day == 25) ? Topics.HolidaySeason :
                month == 12 && day >= 26 ? Topics.YearEndSeason :
                month == 1 && day <= 3 ? Topics.NewYearSeason :
                null;

            // 季節トピック
            var seasonTopic =
                month == 3 || month == 4 ? Topics.SpringSeason :
                month == 5 || month == 6 ? Topics.RainySeason :
                month == 7 || month == 8 ? Topics.SummerSeason :
                month >= 9 || month <= 11 ? Topics.AutumnSeason :
                Topics.WinterSeason;

            // トピックを抽選する
            // 10% 特殊トピック（なければ季節トピック）
            // 20% 季節トピック
            // 70% 通常
            var dice = core?.Random.Next(100);
            var chosenTopic =
                dice < 10 ? specialTopic ?? seasonTopic :
                dice < 20 ? seasonTopic :
                Topics.Generics;

            // 抽選したトピックの中から発言を抽選
            return chosenTopic.Random();
        }

        private string MapVariables(string text)
        {
            var myStorage = core?.GetMyStorage();
            if (myStorage == null) return text;

            var year = DateTime.Now.Year;

            var nouns = myStorage.Get("freetalk.nouns", new List<string>()).ToList();
            var verbs = myStorage.Get("freetalk.verbs", new List<string>()).ToList();
            var adjectives = myStorage.Get("freetalk.adjectives", new List<string>()).ToList();

            return Regex.Replace(text, @"\$(.*?)\$", m => m.Groups[1].Value switch
            {
                "" => "$",
                "noun" => nouns.Random(),
                "verb" => verbs.Random().Split(',')[1],
                "adjective" => adjectives.Random(),
                "zodiac" => GetJapaneseZodiacOf(year),
                "nextZodiac" => GetJapaneseZodiacOf(year + 1),
                "year" => year.ToString(),
                _ => m.Value,
            });
        }

        private static readonly char[] ZodiacTable = "子丑寅卯辰巳午未申酉戌亥".ToCharArray();

        private string GetJapaneseZodiacOf(int year) => ZodiacTable[(year - 1972) % 12].ToString();

        private bool ContainsNgWord(string text)
        {
            text = Regex.Replace(text, @"[\s\.,/／]", "").ToLowerInvariant().ToHiragana();
            return NgWords.Any(w => text.Contains(w));
        }

        private readonly Logger logger = new Logger(nameof(LearnWordsModule));
        private readonly Timer timer;
        private Server? core;
        private IShell? shell;
    }
}
