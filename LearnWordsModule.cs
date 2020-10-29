using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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

        public string Name => "free-talk";

        public string[] Aliases => new string[0];

        public bool IgnoreCase => true;

        public PermissionFlag Permission => PermissionFlag.Any;

        public string Usage => "/free-talk";

        public string Description => "FreeTalkPlugin Utility Command for administrators.";

        public LearnWordsModule()
        {
            // 下ネタ回避のために Citrine を参照する
            harassmentHandler = new HarassmentHandlerModule();
            timer = new Timer(1000);
            timer.Elapsed += OnElapsed;
            timer.Start();
            logger.Info($"Installed LearnWordsModule with {Topics.Length} sentences");
        }

        /// <summary>
        /// コマンドのハンドリング
        /// </summary>
        public async Task<string> OnActivatedAsync(ICommandSender sender, Server core, IShell shell, string[] args, string body)
        {
            this.shell ??= shell;
            this.core ??= core;

            var subCommand = args.Length >= 1 ? args[0] : throw new CommandException();
            var myStorage = core.GetMyStorage();

            var queue = myStorage.Get("freetalk.queue", new List<string>());

            switch (subCommand.ToLowerInvariant())
            {
                case "gen":
                case "generate":
                    // free-talk gen
                    // フリートーク文字列を生成する
                    // 自動Postと違い、こちらはダブリ補正などを行わないし、ダブリ補正に影響を与えない
                    return MapVariables(ExtractTopics().Random());
                case "zodiac":
                    // free-talk zodiac
                    // 今年の干支を返す
                    return GetJapaneseZodiacOf(DateTime.Now.Year);
                case "word":
                    // free-talk word
                    // アンケートの語彙をランダム生成
                    return string.Join(", ", GenerateChoices(myStorage));
                case "nativeluckyitem":
                    // free-talk nativeluckyitem <maxPrefixCount>
                    // シトリンのネイティブ ラッキーアイテムを生成
                    {
                        if (args.Length != 2)
                            throw new CommandException();
                        var maxPrefixCount = int.TryParse(args[1], out var i) ? i : throw new CommandException();
                        return string.Join(", ", GenerateNativeLuckyItem(maxPrefixCount));
                    }
                case "enqueue":
                    // free-talk enqueue <text> [time]
                    if (!sender.IsAdmin) throw new AdminOnlyException();

                    return "まだ作ってない";

                    if (args.Length is not 2 and not 3)
                        throw new CommandException();

                        var text = args[1];
                        float? time = null;
                        if (args.Length is 3)
                        {
                            
                        }
                    break;
                case "var":
                    // free-talk var <varName>
                    // トピックの $ で囲む変数展開記法のデバッグ用。変数展開を行って返す
                    if (!sender.IsAdmin) throw new AdminOnlyException();
                    if (args.Length != 2)
                        throw new CommandException();
                    return MapVariables("$" + args[1] + "$");
                case "config":
                    // free-talk config get <key>
                    // <key> に対応するコンフィグを取得する

                    // free-talk config set <key> <value>
                    // <key> に対応するコンフィグに <value> という値をセットする
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
                                    return "整数値を指定して";
                                }
                                return myStorage.Get("freetalk.config.pollRatio", 30).ToString();
                            case "talkratio":
                                if (getset == "set")
                                {
                                    if (float.TryParse(value, out var f))
                                    {
                                        if (f < 60) return "60秒以上にしてほしい";
                                        myStorage.Set("freetalk.config.talkRatio", f);
                                        return "ok";
                                    }
                                    return "実数値を指定して";
                                }
                                return myStorage.Get("freetalk.config.talkRatio", 3600f).ToString();
                            default:
                                return $"{key} は正しいサブコマンドではないみたい";
                        }
                    }
                default:
                    throw new CommandException();
            }
        }

        public override async Task<bool> ActivateAsync(IPost n, IShell shell, Server core)
        {
            if (n.Text != null && n.Text.IsMatch("(何|な[んに])か(喋|しゃべ|話[しせそ])"))
            {
                await shell.ReplyAsync(n, MapVariables(ExtractTopics().Random()));
                core.LikeWithLimited(n.User);
                EconomyModule.Pay(n, shell, core);
                return true;
            }
            return false;
        }

        /// <summary>
        /// タイマーのハンドリング。1秒ごとに実行される
        /// </summary>
        private async void OnElapsed(object sender, ElapsedEventArgs e)
        {
            if (locked || shell == null || core == null) return;
            // 非同期実行を考慮してロックする
            locked = true;

            var storage = core.GetMyStorage();
            var now = DateTimeOffset.Now;
            var lastTalkedAt = storage.Get("freetalk.lastTalkedAt", DateTimeOffset.MinValue);

            if (lastTalkedAt == DateTimeOffset.MinValue)
            {
                lastTalkedAt = DateTimeOffset.Now;
                storage.Set("freetalk.lastTalkedAt", lastTalkedAt);
            }

            // 前回発言時から設定した時間経過していれば処理実行
            if (now - lastTalkedAt >= TimeSpan.FromSeconds(storage.Get("freetalk.config.talkRatio", 3600f)))
            {
                await Talk(storage);
                storage.Set("freetalk.lastTalkedAt", now);
            }
            locked = false;
        }

        /// <summary>
        /// トークを生成し投稿
        /// </summary>
        private async Task Talk(UserStorage.UserRecord storage)
        {
            if (core == null) return;
            if (shell == null) return;

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
            else if (lastLearnedWord != null && core.Random.Next(100) < 10)
            {
                await shell.PostAsync(Topics.Learned.Random().Replace("$word$", lastLearnedWord));
            }
            else
            {
                // ダブリが頻発しないように、直近の抽選履歴を見てかぶらないトピックを抽選する
                string s;
                var count = 0;
                var topics = ExtractTopics().ToArray();
                do
                {
                    s = topics.Random();
                    count++;
                } while (recent.Contains(s) && count < 1000);
                // 1000回超えても被ってしまうのなら諦めて最後に試行したものを採用

                await shell.PostAsync(MapVariables(s));

                if (!s.Contains("$"))
                    recent.Add(s);
                storage.Set("freetalk.recent", recent.TakeLast(topics.Length).ToList());
            }
            lastLearnedWord = null;
        }

        /// <summary>
        /// アンケート用の選択肢を生成
        /// </summary>
        private List<string> GenerateChoices(UserStorage.UserRecord storage)
        {
            var rnd = core?.Random ?? new Random();
            var nouns = storage.Get("freetalk.nouns", new List<string>()).ToList();
            var verbs = storage.Get("freetalk.verbs", new List<string>()).ToList();
            var adjectives = storage.Get("freetalk.adjectives", new List<string>()).ToList();

            return Enumerable.Range(0, rnd.Next(2, 5)).Select(_ =>
            {
                var dice = rnd.Next(100);
                // 20% 名詞
                // 20% 形容詞+名詞
                // 20% 動詞+名詞
                // 20% 動詞+形容詞+名詞
                // 20% ネイティブラッキーアイテム
                return
                    dice < 20 ? nouns.Random() :
                    dice < 40 ? adjectives.Random() + nouns.Random() :
                    dice < 60 ? verbs.Random().Split(',')[1] + nouns.Random() :
                    dice < 80 ? verbs.Random().Split(',')[1] + adjectives.Random() + nouns.Random() :
                    GenerateNativeLuckyItem(5);
            }).ToList();
        }

        /// <summary>
        /// 学習
        /// </summary>
        public override async Task<bool> OnTimelineAsync(IPost n, IShell shell, Server core)
        {
            this.shell ??= shell;
            this.core ??= core;

            if (!(Environment.GetEnvironmentVariable("YAHOO_API_KEY") is string key))
            {
                logger.Warn("Yahoo API Key is not set!");
                return false;
            }

            var t = n.Text ?? n.Repost?.Text;
            var myStorage = core.GetMyStorage();

            var now = DateTimeOffset.UtcNow;
            var last = myStorage.Get("freetalk.lastLearnedAt", DateTimeOffset.MinValue);

            // 前回学習から30秒経過していなければ学習しない
            if ((now - last).TotalSeconds < 30) return false;
            // 本文無し / メンションを含む / NGワードを含む　なら学習しない
            if (t == null || t.ContainsMentions() || harassmentHandler.IsHarassmented(t)) return false;
            // フォロワー限定/ダイレクトなどであれば学習しない
            if (n.Visiblity != Visibility.Public && n.Visiblity == Visibility.Limited) return false;
            // スラッシュコマンドであれば学習しない
            if (t.StartsWith("/")) return false;
            // URLを除外
            t = Regex.Replace(t, @"^https?\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$", "");

            await LearnAsync(t, myStorage, now, key);
            return false;
        }

        /// <summary>
        /// 文法を学習します。
        /// </summary>
        private async Task LearnAsync(string text, UserStorage.UserRecord storage, DateTimeOffset now, string apiKey)
        {
            var result = await AnalysisAsync(apiKey, text);
            storage.Set("freetalk.lastLearnedAt", now);

            var nouns = storage.Get("freetalk.nouns", new List<string>()).Where(t => !t.IsMatch(@"^[a-z\-_0-9]+$")).ToList();
            var verbs = storage.Get("freetalk.verbs", new List<string>()).ToList();
            var adjectives = storage.Get("freetalk.adjectives", new List<string>()).ToList();
            string? prefix = null;
            string? noun = null;

            for (var i = 0; i < result.Length; i++)
            {
                // ルール1: 名詞が連続する場合、その連続する名詞は1語として記憶する
                // ルール2: 接頭辞がある場合、登録時にくっつけて登録する
                // ルール3: next が接尾辞を指す場合、登録時にくっつけて登録する
                // ルール4: 名詞+助動詞(する)を検出した場合、動詞として登録する
                var (surface, reading, pos, baseform, group1, group2) = result[i];
                var next = i < result.Length - 1 ? result[i + 1] : default;

                void RegisterNoun()
                {
                    if (noun != null)
                    {
                        noun += pos == "接尾辞" ? baseform : null;

                        if (!noun.IsMatch(@"^[a-z\-_0-9]$"))
                        {
                            nouns.Add(noun);
                            lastLearnedWord = noun;
                        }
                        prefix = null;
                        noun = null;
                    }
                }

                switch (pos)
                {
                    case "形容詞" when baseform != null:
                        RegisterNoun();
                        adjectives.Add(baseform);
                        lastLearnedWord = baseform;
                        break;
                    case "接頭辞":
                        prefix = (prefix ?? "") + baseform;
                        break;
                    case "名詞":
                        noun += baseform;
                        break;
                    case "助動詞" when baseform == "する":
                        noun += baseform;
                        if (!noun.IsMatch(@"^[a-z\-_0-9]+$"))
                        {
                            verbs.Add("サ変する," + noun);
                            lastLearnedWord = noun;
                        }
                        prefix = null;
                        noun = null;
                        break;
                    case "動詞":
                        {
                            RegisterNoun();
                            var verb = prefix + baseform;
                            lastLearnedWord = verb;
                            verbs.Add(group1 + "," + verb);
                            prefix = null;
                            noun = null;
                            break;
                        }
                    default:
                        RegisterNoun();
                        break;
                }
            }

            if (noun != null)
            {
                if (!noun.IsMatch(@"^[a-z\-_0-9]+$"))
                {
                    nouns.Add(noun);
                    lastLearnedWord = noun;
                }
            }

            const int limit = 200;
            storage.Set("freetalk.nouns", nouns.Distinct().TakeLast(limit).ToList());
            storage.Set("freetalk.verbs", verbs.Distinct().TakeLast(limit).ToList());
            storage.Set("freetalk.adjectives", adjectives.Distinct().TakeLast(limit).ToList());
        }

        private static async Task<(string? surface, string? reading, string? pos, string? baseform, string? group1, string? group2)[]> AnalysisAsync(string key, string t)
        {
            // 形態素解析 API を呼ぶ
            var res = await Server.Http.PostAsync("https://jlp.yahooapis.jp/MAService/V1/parse", new FormUrlEncodedContent(Helper.BuildKeyValues(
                ("appId", key),
                ("sentence", t),
                ("response", "feature")
            )));
            var text = await res.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(text);

            // XML を解析しデータ処理
            return doc.Descendants("{urn:yahoo:jp:jlp}word")
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
                }).ToArray() as (string? surface, string? reading, string? pos, string? baseform, string? group1, string? group2)[];
        }

        /// <summary>
        /// 抽選対象のトピックをピックアップします。
        /// </summary>
        private IEnumerable<string> ExtractTopics()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;
            var hour = now.Hour;
            var wd = now.DayOfWeek;

            // 特殊トピック
            var specialTopic =
                month == 7 && day <= 7 ? Topics.TanabataSeason :
                month == 10 && day == 31 ? Topics.HalloweenSeason :
                month == 12 && (day == 24 || day == 25) ? Topics.HolidaySeason :
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

            // 曜日トピック
            var weekDayTopic = 8 <= hour && hour <= 16 ? wd switch
            {
                DayOfWeek.Friday => Topics.FridayDayTopic,
                DayOfWeek.Saturday => Topics.SaturdayDayTopic,
                DayOfWeek.Sunday => Topics.SundayDayTopic,
                _ => null,
            } : 17 <= hour ? wd switch
            {
                DayOfWeek.Friday => Topics.FridayNightTopic,
                DayOfWeek.Saturday => Topics.SaturdayNightTopic,
                DayOfWeek.Sunday => Topics.SundayNightTopic,
                _ => null
            } : null;

            var empty = Enumerable.Empty<string>();

            // 利用可能なトピック候補
            var candidates = Topics.Generics
                .Concat(seasonTopic)
                .Concat(specialTopic ?? empty)
                .Concat(weekDayTopic ?? empty);

            // 発言を抽選
            return candidates;
        }

        /// <summary>
        /// 指定したテキストの変数を展開し、新たな文字列として返します。
        /// </summary>
        private string MapVariables(string text)
        {
            var myStorage = core?.GetMyStorage();
            if (myStorage == null) return text;

            var year = DateTime.Now.Year;

            var nouns = myStorage.Get("freetalk.nouns", new List<string>()).ToList();
            var verbs = myStorage.Get("freetalk.verbs", new List<string>()).ToList();
            var adjectives = myStorage.Get("freetalk.adjectives", new List<string>()).ToList();

            return Regex.Replace(text, @"\$(.*?)\$", m =>
            {
                var val = m.Groups[1].Value.Split(',');
                if (val.Length == 0) return "";
                var key = val[0];
                var args = val.Skip(1).ToArray();
                try
                {
                    return key switch
                    {
                        "" => "$",
                        "noun" => nouns.Random(),
                        "verb" => verbs.Random().Split(',')[1],
                        "adjective" => adjectives.Random(),
                        "!adjective" => adjectives.Random()[0..^1] + "くない",
                        "zodiac" => GetJapaneseZodiacOf(year),
                        "nextZodiac" => GetJapaneseZodiacOf(year + 1),
                        "luckyitem" => GenerateNativeLuckyItem(5),
                        "year" => year.ToString(),
                        "rnd" => core?.Random.Next(int.Parse(args[0]), int.Parse(args[1])).ToString(),
                        "hour" => GetHour(args.Length == 0 ? (int?)null : int.Parse(args[0])),
                        "like" => PickNicknameOf(Rating.Like),
                        "bestFriend" => PickNicknameOf(Rating.BestFriend),
                        "partner" => PickNicknameOf(Rating.Partner),
                        "nickname" => PickNickname(),
                        _ => m.Value,
                    };
                }
                catch (Exception e)
                {
                    logger.Error($"{e.GetType().Name}: {e.Message}");
                    logger.Error(e.StackTrace);
                    return m.Value;
                }
            });
        }

        /// <summary>
        /// 現在の時をあいまいな形で返します。
        /// </summary>
        private string GetHour(int? hour)
        {
            var now = DateTime.Now;
            var (h, m) = (now.Hour + (hour ?? 0), now.Minute);
            return
                m < 25 ? h + "時" :
                m < 36 ? h + "時半" : h + 1 + "時";
        }

        /// <summary>
        /// 指定した好感度を持つユーザーのニックネームをランダムに抽出します。
        /// ニックネームを登録していないユーザーは対象外です。
        /// ニックネームが1つも見つからない場合は<c>"だれか</c>を返します。
        /// </summary>
        private string PickNicknameOf(Rating rat)
        {
            var pickedUser = core?.Storage.Records
                .Where(r => core.GetRatingOf(r.Key) == rat)
                .Where(r => r.Value.Has(StorageKey.Nickname))
                .Random().Key;

            if (core == null || pickedUser == null) return "だれか";
            return core.Storage[pickedUser].Get(StorageKey.Nickname, "");
        }

        /// <summary>
        /// ユーザーのニックネームをランダムに抽出します。
        /// ニックネームを登録していないユーザーは対象外です。
        /// ニックネームが1つも見つからない場合は<c>"だれか</c>を返します。
        /// </summary>
        private string PickNickname()
        {
            var name = core?.Storage.Records
                .Where(r => r.Value.Has(StorageKey.Nickname))
                .Select(r => r.Value.Get(StorageKey.Nickname, ""))
                .Random();

            if (core == null || name == null) return "だれか";
            return name;
        }

        /// <summary>
        /// 指定した年の干支を返します。
        /// </summary>
        private string GetJapaneseZodiacOf(int year) => ZodiacTable[(year - 1972) % 12].ToString();

        private string GenerateNativeLuckyItem(int maxPrefixCount = 0)
        {
            var sb = new StringBuilder();
            var pc = core?.Random.Next(maxPrefixCount) ?? 0;
            if (pc > 0)
            {
                Enumerable.Repeat("", pc)
                    .Select(_ => ItemPrefix())
                    .ToList()
                    .ForEach(s => sb.Append(s));
            }
            sb.Append(Item());
            if (core?.Random.Next(100) > 70)
            {
                sb.Append(ItemSuffix());
            }
            return sb.ToString();
        }

        private static string Item() => FortuneModule.Items.Random();
        private static string ItemPrefix() => FortuneModule.ItemPrefixes.Random();
        private static string ItemSuffix() => FortuneModule.ItemSuffixes.Random();

        private static readonly char[] ZodiacTable = "子丑寅卯辰巳午未申酉戌亥".ToCharArray();

        private readonly Logger logger = new Logger(nameof(LearnWordsModule));
        private readonly HarassmentHandlerModule harassmentHandler;
        private readonly Timer timer;
        private Server? core;
        private IShell? shell;
        private string? lastLearnedWord = null;
        private bool locked;
    }

    public struct QueueItem
    {
        public string Text { get; set; }
        public float Time { get; set; }
    }
}
