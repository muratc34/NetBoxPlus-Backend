namespace NetBoxPlus.DemoSeeder.Content;

internal sealed record Texts(string Title, string Synopsis);

internal sealed record GenreSeed(string Turkish, string English);

internal sealed record PersonSeed(string Name, string BiographyTurkish, string BiographyEnglish);

/// <param name="Role">Director, Writer, Actor or Creator, as the catalog spells it.</param>
internal sealed record CreditSeed(string Person, string Role, string? Character = null);

/// <summary>The two colours of the generated poster and backdrop, as ffmpeg colour names or hex.</summary>
internal sealed record Palette(string From, string To);

/// <param name="VideoHeight">The generated video's height: 720 or 1080.</param>
internal sealed record MovieSeed(
    string OriginalTitle,
    Texts Turkish,
    Texts English,
    int ReleaseYear,
    string AgeRating,
    int RuntimeMinutes,
    IReadOnlyList<string> Genres,
    IReadOnlyList<CreditSeed> Credits,
    Palette Palette,
    int VideoHeight = 720);

internal sealed record EpisodeSeed(int Number, Texts Turkish, Texts English, int RuntimeMinutes);

internal sealed record SeasonSeed(int Number, string TitleTurkish, string TitleEnglish, IReadOnlyList<EpisodeSeed> Episodes);

internal sealed record SeriesSeed(
    string OriginalTitle,
    Texts Turkish,
    Texts English,
    int ReleaseYear,
    string AgeRating,
    IReadOnlyList<string> Genres,
    IReadOnlyList<CreditSeed> Credits,
    Palette Palette,
    IReadOnlyList<SeasonSeed> Seasons);

/// <summary>
/// The demo catalog. Every title, person and story is made up; Turkish is the catalog's required
/// language, English its second. Genres and credits refer to entries here by their Turkish name.
/// </summary>
internal static class DemoContent
{
    public static readonly IReadOnlyList<GenreSeed> Genres =
    [
        new("Dram", "Drama"),
        new("Komedi", "Comedy"),
        new("Bilim Kurgu", "Science Fiction"),
        new("Gerilim", "Thriller"),
        new("Belgesel", "Documentary"),
        new("Animasyon", "Animation"),
    ];

    public static readonly IReadOnlyList<PersonSeed> People =
    [
        new("Defne Aksoy", "Tiyatrodan gelen, karakter rolleriyle tanınan oyuncu.", "A stage actor known for character roles."),
        new("Kerem Yalçın", "Sessiz sahneleri seven, ödüllü yönetmen.", "An award-winning director who loves quiet scenes."),
        new("Elif Karadağ", "Gerilim ve dram senaryoları yazan senarist.", "A screenwriter of thrillers and dramas."),
        new("Mert Özkan", "Komedi zamanlamasıyla bilinen oyuncu.", "An actor known for his comic timing."),
        new("Selin Arda", "Belgesel ve doğa filmleri yönetmeni.", "A director of documentaries and nature films."),
        new("Jonas Weller", "Bilim kurgu dünyaları kuran yazar ve yönetmen.", "A writer and director who builds science fiction worlds."),
        new("Mira Castell", "Uluslararası yapımlarda oynayan oyuncu.", "An actor in international productions."),
        new("Tomas Reyes", "Animasyon dizilerinin yaratıcısı.", "The creator of animated series."),
        new("Aylin Demirtaş", "Genç kuşağın dikkat çeken oyuncularından.", "One of the notable actors of the younger generation."),
        new("Can Erdem", "Karakter oyuncusu ve seslendirme sanatçısı.", "A character actor and voice artist."),
    ];

    public static readonly IReadOnlyList<MovieSeed> Movies =
    [
        new(
            "Kuzey Işığı",
            new("Kuzey Işığı", "Kuzeyde bir deniz fenerinde yalnız yaşayan bekçi, fırtınalı bir gecede kapısını çalan yabancıyla geçmişiyle yüzleşir."),
            new("Northern Light", "A keeper living alone in a northern lighthouse faces his past when a stranger knocks on his door on a stormy night."),
            2023, "Age13", 108, ["Dram"],
            [new("Kerem Yalçın", "Director"), new("Elif Karadağ", "Writer"), new("Defne Aksoy", "Actor", "Nehir"), new("Can Erdem", "Actor", "Fener Bekçisi")],
            new("navy", "teal"), VideoHeight: 1080),
        new(
            "Last Stop Mars",
            new("Son Durak Mars", "Mars'a giden son kargo gemisinin mürettebatı, rotalarında olmaması gereken bir sinyal yakalar."),
            new("Last Stop Mars", "The crew of the last cargo ship bound for Mars picks up a signal that should not be on their route."),
            2024, "Age7", 121, ["Bilim Kurgu"],
            [new("Jonas Weller", "Director"), new("Jonas Weller", "Writer"), new("Mira Castell", "Actor", "Captain Vega"), new("Mert Özkan", "Actor", "Engineer Bora")],
            new("darkred", "orange"), VideoHeight: 1080),
        new(
            "Kayıp Anahtar",
            new("Kayıp Anahtar", "Düğününe bir gün kala evinin anahtarını kaybeden damat, İstanbul'u bir uçtan bir uca dolaşmak zorunda kalır."),
            new("The Lost Key", "A day before his wedding, a groom loses his house key and has to cross Istanbul from end to end."),
            2022, "All", 94, ["Komedi"],
            [new("Kerem Yalçın", "Director"), new("Mert Özkan", "Actor", "Emre"), new("Aylin Demirtaş", "Actor", "Zeynep")],
            new("gold", "darkorange")),
        new(
            "Gece Vardiyası",
            new("Gece Vardiyası", "Bir hastanenin gece vardiyasında çalışan hemşire, kayıtlarda olmayan bir hastanın izini sürer."),
            new("Night Shift", "A nurse on a hospital's night shift tracks a patient who does not exist in the records."),
            2021, "Age16", 102, ["Gerilim"],
            [new("Elif Karadağ", "Director"), new("Elif Karadağ", "Writer"), new("Aylin Demirtaş", "Actor", "Deniz"), new("Can Erdem", "Actor", "Doktor Kaya")],
            new("black", "darkslateblue")),
        new(
            "Mavi Derinlik",
            new("Mavi Derinlik", "Ege'nin derinliklerinde yaşayan canlıları bir yıl boyunca izleyen bir dalış ekibinin hikâyesi."),
            new("Blue Depths", "The story of a diving team that follows the creatures of the deep Aegean for a whole year."),
            2023, "All", 88, ["Belgesel"],
            [new("Selin Arda", "Director")],
            new("darkblue", "deepskyblue")),
        new(
            "Sessiz Şehir",
            new("Sessiz Şehir", "Bir sabah tüm seslerin kaybolduğu şehirde, bir dedektif bunun nedenini bulmak için zamanla yarışır."),
            new("Silent City", "In a city where every sound vanished one morning, a detective races against time to find out why."),
            2020, "Age18", 115, ["Gerilim", "Dram"],
            [new("Jonas Weller", "Director"), new("Elif Karadağ", "Writer"), new("Mira Castell", "Actor", "Detective Lane"), new("Defne Aksoy", "Actor", "Ada")],
            new("dimgray", "darkred")),
    ];

    public static readonly IReadOnlyList<SeriesSeed> Series =
    [
        new(
            "Rüzgarın Kızı",
            new("Rüzgarın Kızı", "Ege kıyısındaki bir köyde büyüyen genç bir kadın, ailesinin rüzgâr değirmenini kurtarmak için mücadele eder."),
            new("Daughter of the Wind", "A young woman raised in an Aegean village fights to save her family's windmill."),
            2022, "Age13", ["Dram"],
            [new("Kerem Yalçın", "Creator"), new("Aylin Demirtaş", "Actor", "Rüya"), new("Defne Aksoy", "Actor", "Hatice"), new("Can Erdem", "Actor", "Osman")],
            new("seagreen", "khaki"),
            [
                new(1, "Başlangıç", "Beginnings",
                [
                    Episode(1, "Eve Dönüş", "Rüya, babasının ölümünden sonra köye döner.", "Homecoming", "Rüya returns to the village after her father's death.", 48),
                    Episode(2, "Eski Borçlar", "Değirmenin borçları ortaya çıkar.", "Old Debts", "The windmill's debts come to light.", 46),
                    Episode(3, "İlk Hasat", "Köy, Rüya'nın planına şüpheyle bakar.", "First Harvest", "The village looks at Rüya's plan with doubt.", 50),
                ]),
                new(2, "Fırtına", "Storm",
                [
                    Episode(1, "Kara Bulutlar", "Bir yatırımcı köye gelir.", "Dark Clouds", "An investor arrives in the village.", 47),
                    Episode(2, "Rüzgâra Karşı", "Rüya zor bir karar verir.", "Against the Wind", "Rüya makes a hard decision.", 49),
                    Episode(3, "Sakin Deniz", "Fırtına diner, sırlar ortaya çıkar.", "Calm Sea", "The storm passes and secrets come out.", 52),
                ]),
            ]),
        new(
            "Robot Workshop",
            new("Robot Atölyesi", "Meraklı iki kardeş, dedelerinin atölyesinde konuşan robotlarla maceradan maceraya koşar."),
            new("Robot Workshop", "Two curious siblings run from one adventure to the next with talking robots in their grandfather's workshop."),
            2024, "All", ["Animasyon", "Komedi"],
            [new("Tomas Reyes", "Creator"), new("Mert Özkan", "Actor", "Bolt"), new("Can Erdem", "Actor", "Dede")],
            new("purple", "hotpink"),
            [
                new(1, "Vida ve Somun", "Nuts and Bolts",
                [
                    Episode(1, "Bolt Uyanıyor", "Kardeşler atölyede eski bir robot bulur.", "Bolt Wakes Up", "The siblings find an old robot in the workshop.", 22),
                    Episode(2, "Kayıp Dişli", "Bolt'un bir dişlisi kaybolur.", "The Missing Gear", "One of Bolt's gears goes missing.", 22),
                    Episode(3, "Bilim Fuarı", "Okulun bilim fuarı karışır.", "Science Fair", "The school science fair goes wrong.", 24),
                ]),
                new(2, "Yeni Parçalar", "New Parts",
                [
                    Episode(1, "Uçan Bolt", "Bolt'a kanat takılır.", "Flying Bolt", "Bolt gets wings.", 22),
                    Episode(2, "Robot Kardeş", "Atölyeye yeni bir robot gelir.", "Robot Sibling", "A new robot joins the workshop.", 23),
                    Episode(3, "Büyük Yarış", "Robotlar mahalle yarışına katılır.", "The Big Race", "The robots enter the neighbourhood race.", 24),
                ]),
            ]),
    ];

    /// <summary>Every video the catalog gets: one per movie and one per episode.</summary>
    public static int VideoCount => Movies.Count + Series.Sum(series => series.Seasons.Sum(season => season.Episodes.Count));

    private static EpisodeSeed Episode(int number, string titleTurkish, string synopsisTurkish, string titleEnglish, string synopsisEnglish, int runtimeMinutes)
        => new(number, new(titleTurkish, synopsisTurkish), new(titleEnglish, synopsisEnglish), runtimeMinutes);
}
