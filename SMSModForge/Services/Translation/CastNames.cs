using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;

namespace SMSModForge.Services.Translation;

/// <summary>
/// The game's own characters' names, and how each is written in the languages
/// ModForge has that use another alphabet - spelled as the name sounds, never
/// translated as a word.
/// <para/>
/// Only names. The game files a good many characters under a description -
/// "Android", "Technician", "Park Woman", "Mobster 1" - and those are words, to
/// be translated like any other. Where a description carries a name, the name
/// alone is here: "Nurse Nina" is Nina, "Doctor Frost" is Frost, "Judge
/// Benjamin" is Benjamin; the title around it is translated.
/// <para/>
/// Every name has a spelling in every language here, so the game's own
/// characters never reach the Translate window's list of names - the author
/// has nothing to do for them. Where the game does not settle it, the
/// commonest form is given: a Japanese name in Chinese in the characters it is
/// most often written with (Chihiro 千寻, Sakura 樱), since the game never shows
/// its owner's; a Western name as Chinese spells it by sound, in the
/// characters used for a woman's name where the game makes her one; Master
/// Zhen, a Chinese name, as 甄.
/// </summary>
public static class CastNames
{
    /// <summary>Name, then Russian, Japanese, Korean and Chinese.</summary>
    private static readonly string?[][] Table =
    {
        new[] { "Anna", "Анна", "アンナ", "안나", "安娜" },
        new[] { "Nikki", "Никки", "ニッキー", "니키", "妮基" },
        new[] { "Charlotte", "Шарлотта", "シャーロット", "샬럿", "夏洛特" },
        new[] { "Claudia", "Клаудия", "クラウディア", "클라우디아", "克劳迪娅" },
        new[] { "Chloe", "Хлоя", "クロエ", "클로이", "克洛伊" },
        new[] { "Tasha", "Таша", "ターシャ", "타샤", "塔莎" },
        new[] { "Nyxara", "Никсара", "ニクサラ", "닉사라", "妮克萨拉" },
        new[] { "Josef", "Йозеф", "ヨーゼフ", "요제프", "约瑟夫" },
        new[] { "Adrian", "Адриан", "エイドリアン", "에이드리언", "阿德里安" },
        new[] { "Isabella", "Изабелла", "イザベラ", "이사벨라", "伊莎贝拉" },
        new[] { "Sofia", "София", "ソフィア", "소피아", "索菲亚" },
        new[] { "Katarina", "Катарина", "カタリナ", "카타리나", "卡塔琳娜" },
        new[] { "Emma", "Эмма", "エマ", "엠마", "艾玛" },
        new[] { "Nina", "Нина", "ニーナ", "니나", "妮娜" },
        new[] { "Mario", "Марио", "マリオ", "마리오", "马里奥" },
        new[] { "Nadia", "Надя", "ナディア", "나디아", "娜迪亚" },
        new[] { "Haniya", "Хания", "ハニヤ", "하니야", "哈妮娅" },
        new[] { "Celeste", "Селеста", "セレステ", "셀레스트", "塞莱斯特" },
        new[] { "Toni", "Тони", "トニ", "토니", "托妮" },
        new[] { "River", "Ривер", "リバー", "리버", "里弗" },
        new[] { "Kate", "Кейт", "ケイト", "케이트", "凯特" },
        new[] { "Amelia", "Амелия", "アメリア", "아멜리아", "阿米莉亚" },
        new[] { "Ken", "Кен", "ケン", "켄", "肯" },
        new[] { "Zuri", "Зури", "ズリ", "주리", "祖丽" },
        new[] { "Alice", "Алиса", "アリス", "앨리스", "爱丽丝" },
        new[] { "Norah", "Нора", "ノラ", "노라", "诺拉" },
        new[] { "Samuel", "Сэмюэл", "サミュエル", "새뮤얼", "塞缪尔" },
        new[] { "Gabriel", "Габриэль", "ガブリエル", "가브리엘", "加布里埃尔" },
        new[] { "Samantha", "Саманта", "サマンサ", "사만다", "萨曼莎" },
        new[] { "Phoenix", "Феникс", "フェニックス", "피닉스", "菲尼克斯" },
        new[] { "Joey", "Джоуи", "ジョーイ", "조이", "乔伊" },
        new[] { "Himari", "Химари", "ヒマリ", "히마리", "阳葵" },
        new[] { "Kirby", "Кирби", "カービー", "커비", "柯比" },
        new[] { "Clay", "Клэй", "クレイ", "클레이", "克莱" },
        new[] { "Robert", "Роберт", "ロバート", "로버트", "罗伯特" },
        new[] { "Astrid", "Астрид", "アストリッド", "아스트리드", "阿斯特丽德" },
        new[] { "Liz", "Лиз", "リズ", "리즈", "莉兹" },
        new[] { "Frost", "Фрост", "フロスト", "프로스트", "弗罗斯特" },
        new[] { "Vanessa", "Ванесса", "ヴァネッサ", "바네사", "瓦妮莎" },
        new[] { "Frank", "Фрэнк", "フランク", "프랭크", "弗兰克" },
        new[] { "Jasper", "Джаспер", "ジャスパー", "재스퍼", "贾斯珀" },
        new[] { "Roxy", "Рокси", "ロキシー", "록시", "洛克茜" },
        new[] { "Chihiro", "Тихиро", "チヒロ", "치히로", "千寻" },
        new[] { "Freya", "Фрейя", "フレイヤ", "프레이야", "芙蕾雅" },
        new[] { "Liam", "Лиам", "リアム", "리암", "利亚姆" },
        new[] { "Leilani", "Лейлани", "レイラニ", "레일라니", "蕾拉妮" },
        new[] { "Mei", "Мэй", "メイ", "메이", "美" },
        new[] { "Michelle", "Мишель", "ミシェル", "미셸", "米歇尔" },
        new[] { "Riku", "Рику", "リク", "리쿠", "陆" },
        new[] { "Sakura", "Сакура", "サクラ", "사쿠라", "樱" },
        new[] { "Benjamin", "Бенджамин", "ベンジャミン", "벤저민", "本杰明" },
        new[] { "Toshiro", "Тосиро", "トシロウ", "도시로", "敏郎" },
        new[] { "Daiju", "Дайдзю", "ダイジュ", "다이주", "大树" },
        new[] { "Hiroji", "Хиродзи", "ヒロジ", "히로지", "宏治" },
        new[] { "Sora", "Сора", "ソラ", "소라", "空" },
        new[] { "Diego", "Диего", "ディエゴ", "디에고", "迭戈" },
        new[] { "Clara", "Клара", "クララ", "클라라", "克拉拉" },
        new[] { "Felix", "Феликс", "フェリックス", "펠릭스", "费利克斯" },
        new[] { "Carina", "Карина", "カリーナ", "카리나", "卡琳娜" },
        new[] { "Elfina", "Эльфина", "エルフィナ", "엘피나", "艾尔菲娜" },
        new[] { "Jeff", "Джефф", "ジェフ", "제프", "杰夫" },
        // Doctor Frost's first name: she is Doctor Evelyn Frost (see VanillaCastData).
        new[] { "Evelyn", "Эвелин", "エヴリン", "에블린", "伊芙琳" },
        new[] { "Cerise", "Сериз", "セリーズ", "세리즈", "瑟丽丝" },
        new[] { "Zhen", "Чжэнь", "ジェン", "젠", "甄" },
        new[] { "Jack", "Джек", "ジャック", "잭", "杰克" },
        new[] { "Kimura", "Кимура", "キムラ", "기무라", "木村" },
        new[] { "Tristan", "Тристан", "トリスタン", "트리스탄", "特里斯坦" },
        new[] { "Zazia", "Зазия", "ザジア", "자지아", "扎齐娅" },
        new[] { "Megan", "Меган", "ミーガン", "메건", "梅根" },
    };

    /// <summary>Which column of <see cref="Table"/> a language's spellings are in.</summary>
    private static int Column(string? code)
    {
        switch (PluralRules.LanguageOf(code ?? ""))
        {
            case "ru": return 1;
            case "ja": return 2;
            case "ko": return 3;
            case "zh":
                // Only simplified characters are given; traditional ones differ.
                return (code ?? "").IndexOf("hant", StringComparison.OrdinalIgnoreCase) >= 0
                       || (code ?? "").EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                       || (code ?? "").EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ? -1 : 4;
            default: return -1;
        }
    }

    /// <summary>Every name of the game's cast, longest first.</summary>
    public static IReadOnlyList<string> All { get; } =
        Table.Select(row => row[0]!).OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToList();

    public static bool Has(string? name)
        => name != null && Table.Any(row => string.Equals(row[0], name, StringComparison.Ordinal));

    /// <summary>How <paramref name="name"/> is written in <paramref name="code"/>,
    /// or null: not a name of the game's, or a language that keeps it as
    /// written.</summary>
    public static string? In(string? code, string? name)
    {
        int column = Column(code);
        if (column < 0 || name == null) return null;
        foreach (var row in Table)
            if (string.Equals(row[0], name, StringComparison.Ordinal)) return row[column];
        return null;
    }
}
