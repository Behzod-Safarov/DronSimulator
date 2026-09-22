using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI language. Strings are authored in English in the code and looked up here;
/// Uzbek (Latin) is the default, L on the title screen or the settings panel switches.
/// Unknown strings fall through unchanged, so new text is never blank.
/// </summary>
public static class Loc
{
    static string lang;
    public static string Lang
    {
        get { if (lang == null) lang = PlayerPrefs.GetString("lang", "uz"); return lang; }
        set { lang = value; PlayerPrefs.SetString("lang", value); PlayerPrefs.Save(); }
    }
    public static bool Uz => Lang == "uz";
    public static void Toggle() => Lang = Uz ? "en" : "uz";
    public static string LangName => Uz ? "O'zbekcha" : "English";

    /// <summary>Translate a literal.</summary>
    public static string T(string en) => Uz && uz.TryGetValue(en, out var s) ? s : en;
    /// <summary>Translate a composite-format string and fill it in.</summary>
    public static string F(string en, params object[] args) => string.Format(T(en), args);


    // ---- in-flight status text ------------------------------------------------------------
    // Statuses are built with numbers inside, so they are translated phrase by phrase.
    static readonly (System.Text.RegularExpressions.Regex rx, string uz)[] phrases = Build(new[]
    {
        (@"Throttle down, press SPACE / \(A\) to arm", "Gazni pastga tushiring, yoqish uchun SPACE / (A) ni bosing"),
        (@"Reset - press arm to fly", "Qayta o'rnatildi - uchish uchun SPACE ni bosing"),
        (@"PROP DAMAGED - land and press R", "PARRAK SHIKASTLANGAN - qo'ning va R ni bosing"),
        (@"PROP WASH - add forward speed", "PARRAK OQIMI - oldinga tezlik qo'shing"),
        (@"BATTERY EMPTY - press R for a fresh pack", "BATAREYA BO'SH - yangi batareya uchun R ni bosing"),
        (@"LOW BATTERY - LAND", "BATAREYA KAM - QO'NING"),
        (@"PROP STRIKE", "PARRAK URILDI"),
        (@"- land and press R", "- qo'ning va R ni bosing"),
        (@"CRASH ", "HALOKAT "),
        (@"Flipped over", "Ag'darildi"),
        (@"Out of bounds", "Chegaradan chiqdi"),
        (@"Fly into the box", "Quti ichiga uching"),
        (@"Hold still\.\.\. ", "Qimirlamang... "),
        (@"Hold\.\.\. ", "Ushlab turing... "),
        (@"Climb above (\S+) m  \(alt (\S+) m\)", "$1 m dan yuqoriga ko'tariling  (balandlik $2 m)"),
        (@"Now land gently on the orange pad", "Endi to'q sariq maydonchaga yumshoq qo'ning"),
        (@"Hard landing \((\S+) m/s\) - go around and try again", "Qattiq qo'nish ($1 m/s) - aylanib qayta urinib ko'ring"),
        (@"Landed!", "Qo'ndingiz!"),
        (@"Laps (\S+) / (\S+)   get (\S+) m from the pylon, above (\S+) m", "Aylanalar $1 / $2   ustundan $3 m masofaga, $4 m dan balandga chiqing"),
        (@"Laps (\S+) / (\S+)   keep (\S+) m from the pylon", "Aylanalar $1 / $2   ustundan $3 m masofada qoling"),
        (@"Orbit complete - (\S+) laps", "Aylanish yakunlandi - $1 aylana"),
        (@"Get ready\.\.\. ", "Tayyorlaning... "),
        (@"FALSE START - wait for GO", "NOTO'G'RI START - START ni kuting"),
        (@"FALSE START", "NOTO'G'RI START"),
        (@"^GO!$", "START!"),
        (@"Gate (\d+) / (\d+)", "Darvoza $1 / $2"),
        (@"   Lap (\d+) / (\d+)", "   Aylana $1 / $2"),
        (@"best lap", "eng yaxshi aylana"),
        (@"   last ", "   oxirgi "),
        (@"   record ", "   rekord "),
        (@"   target was ", "   maqsad edi "),
        (@"   target ", "   maqsad "),
        (@"TARGET BEATEN", "MAQSADGA ERISHILDI"),
        (@"TOO SLOW", "JUDA SEKIN"),
        (@"Race finished in ", "Poyga yakunlandi: "),
        (@"Course complete!", "Trassa yakunlandi!"),
        (@"^LAP ", "AYLANA "),
        (@"climb above (\S+) m first", "avval $1 m dan yuqoriga ko'tariling"),
        (@"All tricks landed!", "Barcha trikklar bajarildi!"),
        (@"^Rolls ", "Roll "),
        (@"^Flips ", "Flip "),
        (@"^Power loops ", "Pauer-lup "),
        (@"   go!", "   boshlang!"),
        (@"^ROLL$", "ROLL"),
        (@"^POWER LOOP$", "PAUER-LUP"),
        (@"All (\d+) balloons popped!", "Barcha $1 ta shar yorildi!"),
        (@"Balloons (\d+) / (\d+)   (\d+) to go", "Sharlar $1 / $2   $3 ta qoldi"),
        (@"Balloons (\d+)   Points (\d+)", "Sharlar $1   Ball $2"),
        (@"Press F over a target to drop", "Nishon ustida tashlash uchun F ni bosing"),
        (@"HIT (\d+) pts", "URILDI $1 ball"),
        (@"Done: (\d+) points, (\d+) hits", "Tayyor: $1 ball, $2 urilish"),
        (@"Done!", "Tayyor!"),
        (@"TARGET HIT at (\S+) m/s with (\d+) drone\(s\)", "NISHON URILDI: $1 m/s, $2 ta dron"),
        (@"TARGET HIT!", "NISHON URILDI!"),
        (@"Hit the MARKED target", "BELGILANGAN nishonni uring"),
        (@"\(hit faster than (\S+) m/s\)", "(tezroq uring: $1 m/s dan)"),
        (@"drones left (\d+)", "$1 ta dron qoldi"),
        (@"Out of drones - target missed", "Dronlar tugadi - nishon urilmadi"),
        (@"Out of drones - (\d+)% demolished \(needed (\d+)%\)", "Dronlar tugadi - $1% buzildi (kerak edi $2%)"),
        (@"Demolished (\d+)% \(need (\d+)%\)", "Buzildi $1% (kerak $2%)"),
        (@"Passed: (\d+)% demolished with (\d+) drones", "O'tildi: $1% buzildi, $2 ta dron bilan"),
        (@"BOOM!  (\S+) m/s   (\d+)% demolished   next drone in (\S+) s", "BOOM!  $1 m/s   $2% buzildi   keyingi dron $3 s dan keyin"),
        (@"AGENT: Taking off", "AGENT: Ko'tarilmoqda"),
        (@"AGENT: Waypoint (\d+) / (\d+)", "AGENT: Nuqta $1 / $2"),
        (@"AGENT: Returning home", "AGENT: Uyga qaytmoqda"),
        (@"AGENT: Landing", "AGENT: Qo'nmoqda"),
        (@"AGENT: Landed - mission complete", "AGENT: Qo'ndi - missiya yakunlandi"),
        (@"AGENT: Manual - pilot took over", "AGENT: Qo'lda - uchuvchi boshqaruvni oldi"),
        (@"Mission complete - press Backspace to fly it again", "Missiya yakunlandi - qayta uchish uchun Backspace ni bosing"),
        (@"Crashed - agent stopped", "Halokat - agent to'xtadi"),
        (@"New best!", "Yangi rekord!"),
        (@"  Score ", "  Ball "),
        (@" finished$", " yakunlandi"),
    });

    static (System.Text.RegularExpressions.Regex, string)[] Build((string, string)[] src)
    {
        var list = new (System.Text.RegularExpressions.Regex, string)[src.Length];
        for (int i = 0; i < src.Length; i++) list[i] = (new System.Text.RegularExpressions.Regex(src[i].Item1), src[i].Item2);
        return list;
    }

    /// <summary>Translate an in-flight status / banner / message (numbers are kept).</summary>
    public static string S(string text)
    {
        if (!Uz || string.IsNullOrEmpty(text)) return text;
        foreach (var (rx, uzText) in phrases) text = rx.Replace(text, uzText);
        return text;
    }

    static readonly Dictionary<string, string> uz = new Dictionary<string, string>
    {
        // ---- title -------------------------------------------------------------------
        ["FPV FLIGHT SCHOOL"] = "FPV UCHISH MAKTABI",
        ["5-inch freestyle quad  ·  Betaflight-style controller  ·  real wind, battery sag and prop strikes"] =
            "5 dyuymli freestyle kvadrokopter  ·  Betaflight uslubidagi kontroller  ·  haqiqiy shamol, batareya cho'kishi va parrak urilishi",
        ["Start training"] = "Mashg'ulotni boshlash",
        ["Free flight"] = "Erkin parvoz",
        ["Settings"] = "Sozlamalar",
        ["Quit"] = "Chiqish",
        ["Input"] = "Boshqaruv",
        ["Keyboard"] = "Klaviatura",
        ["Pilot progress"] = "Uchuvchi natijasi",
        ["{0} drills"] = "{0} ta mashq",
        ["Language"] = "Til",
        ["↑  ↓  choose      ENTER / ⓐ  select      L  language      TAB  settings"] = "↑  ↓  tanlash      ENTER / ⓐ  tasdiqlash      L  til      TAB  sozlamalar",

        // ---- disciplines ---------------------------------------------------------------
        ["Pit field"] = "Mashq maydoni",
        ["Race track"] = "Poyga trassasi",
        ["Freestyle park"] = "Fristayl parki",
        ["Demolition yard"] = "Buzish maydoni",
        ["Basics"] = "Asoslar",
        ["Racing"] = "Poyga",
        ["Freestyle"] = "Fristayl",
        ["Demolition"] = "Buzish",
        ["Agent"] = "Agent",
        ["Hover  ·  Land  ·  Orbit"] = "Muallaq turish  ·  Qo'nish  ·  Aylanish",
        ["Gates  ·  Laps  ·  Time trial"] = "Darvozalar  ·  Aylanalar  ·  Vaqtga poyga",
        ["Rolls  ·  Flips  ·  Power loops"] = "Roll  ·  Flip  ·  Pauer-lup",
        ["Balloons  ·  Drops  ·  Kamikaze"] = "Sharlar  ·  Tashlash  ·  Kamikadze",
        ["Autopilot  ·  Take-over"] = "Avtopilot  ·  Boshqaruvni olish",
        ["No score  ·  No rules"] = "Ballsiz  ·  Qoidalarsiz",
        ["What a 5-inch quad really feels like: throttle finesse, killing drift, soft landings and the coordinated turn. Start here."] =
            "5 dyuymli kvadrokopter aslida qanday his qilinadi: gaz nozikligi, siljishni to'xtatish, yumshoq qo'nish va muvofiqlashtirilgan burilish. Shu yerdan boshlang.",
        ["The gate loop from a gentle Angle-mode lap to a full Acro race with a 3-2-1 countdown, false-start detection and a lap record to beat."] =
            "Darvoza halqasi: yumshoq Angle rejimidagi aylanadan 3-2-1 sanoq, noto'g'ri start aniqlash va yangilanishi kerak bo'lgan rekord bilan to'liq Acro poygagacha.",
        ["Acro tricks recognised from the quad's real body rates. Build the muscle memory to throw and catch a roll before you try it outside."] =
            "Kvadrokopterning haqiqiy burchak tezliklaridan aniqlanadigan Acro trikklar. Tashqarida sinashdan oldin rollni tashlash va ushlashni mushak xotirasiga o'rnating.",
        ["Precision with consequences: pop balloons, bomb bullseyes with water balloons, then fly kamikaze runs into the demolition lot."] =
            "Oqibatli aniqlik: sharlarni yoring, nishonlarga suv sharlarini tashlang, so'ng buzish maydonchasiga kamikadze uchishlarini bajaring.",
        ["Watch the autopilot fly the mission and take the sticks whenever you want. A calm way to learn the course and the cameras."] =
            "Avtopilot missiyani qanday uchishini kuzating va xohlagan vaqtda boshqaruvni oling. Trassa va kameralarni o'rganishning xotirjam yo'li.",
        ["The whole field, every camera, unlimited packs. Just fly."] = "Butun maydon, barcha kameralar, cheksiz batareyalar. Shunchaki uching.",
        ["CHOOSE YOUR TRAINING"] = "MASHG'ULOTNI TANLANG",
        ["Each discipline is a ladder of scored drills. Three stars on every drill and you are ready for the real thing."] =
            "Har bir yo'nalish — baholanadigan mashqlar zinapoyasi. Har bir mashqda uch yulduz olsangiz, haqiqiy dronga tayyorsiz.",
        ["COMPLETE"] = "BAJARILDI",
        ["not started"] = "boshlanmagan",
        ["resume at drill {0}"] = "{0}-mashqdan davom",
        ["sandbox"] = "erkin maydon",
        ["←  →  choose      ENTER / ⓐ  open      1-5  jump      F  free flight      ESC  back"] =
            "←  →  tanlash      ENTER / ⓐ  ochish      1-5  o'tish      F  erkin parvoz      ESC  orqaga",

        // ---- drills -----------------------------------------------------------------------
        ["DRILLS"] = "MASHQLAR",
        ["PASSED"] = "O'TILDI",
        ["ATTEMPTS"] = "URINISHLAR",
        ["AVG BEST"] = "O'RTACHA",
        ["{0} / {1} ★  earned"] = "{0} / {1} ★  to'plandi",
        ["best lap  {0:0.00} s"] = "eng yaxshi aylana  {0:0.00} s",
        ["par {0:0} s"] = "me'yor {0:0} s",
        ["never flown"] = "hali uchilmagan",
        ["{0} attempt"] = "{0} urinish",
        ["{0} attempts"] = "{0} urinish",
        ["best {0}"] = "eng yaxshi {0}",
        ["BRIEFING"] = "TOPSHIRIQ",
        ["SCORING"] = "BAHOLASH",
        ["100 points, minus {0} per crash and {1:0.#} per second over par ({2:0} s).   ★ 35   ★★ 65   ★★★ 90"] =
            "100 ball, har bir halokat uchun −{0}, me'yordan ({2:0} s) oshgan har soniya uchun −{1:0.#}.   ★ 35   ★★ 65   ★★★ 90",
        ["You have not flown this drill yet."] = "Bu mashqni hali uchmagansiz.",
        ["Your best: {0}   score {1}   over {2} attempt(s)"] = "Eng yaxshi natijangiz: {0}   ball {1}   {2} urinishda",
        ["↑  ↓  drill      ←  →  discipline      ENTER / ⓐ  FLY      ESC  back"] = "↑  ↓  mashq      ←  →  yo'nalish      ENTER / ⓐ  UCHISH      ESC  orqaga",

        // ---- intro / pause / results ----------------------------------------------------
        ["FREE FLIGHT"] = "ERKIN PARVOZ",
        ["The whole field is yours"] = "Butun maydon sizniki",
        ["suggested mode  {0}   ·   par {1:0} s   ·   best {2}"] = "tavsiya etilgan rejim  {0}   ·   me'yor {1:0} s   ·   eng yaxshi {2}",
        ["PAUSED"] = "PAUZA",
        ["Resume"] = "Davom etish",
        ["Retry drill"] = "Mashqni qaytadan",
        ["Next drill"] = "Keyingi mashq",
        ["Choose drill"] = "Mashq tanlash",
        ["Main menu"] = "Bosh menyu",
        ["Retry"] = "Qaytadan",
        ["↑  ↓  choose      ENTER / ⓐ  select      ESC / ⓑ  back"] = "↑  ↓  tanlash      ENTER / ⓐ  tasdiqlash      ESC / ⓑ  orqaga",
        ["PERFECT RUN"] = "MUKAMMAL PARVOZ",
        ["DRILL COMPLETE"] = "MASHQ BAJARILDI",
        ["DRILL PASSED"] = "MASHQ O'TILDI",
        ["RUN OVER"] = "PARVOZ TUGADI",
        ["SCORE / 100"] = "BALL / 100",
        ["time  <b>{0:0.0} s</b>      crashes  <b>{1}</b>      best  <b>{2}</b>"] = "vaqt  <b>{0:0.0} s</b>      halokatlar  <b>{1}</b>      eng yaxshi  <b>{2}</b>",
        ["NEW PERSONAL BEST"] = "YANGI SHAXSIY REKORD",
        ["↑  ↓  choose      ENTER / ⓐ  select      ESC  drill list"] = "↑  ↓  tanlash      ENTER / ⓐ  tasdiqlash      ESC  mashqlar ro'yxati",

        // ---- HUD lesson panel ---------------------------------------------------------------
        ["drill"] = "mashq",
        ["best"] = "eng yaxshi",
        ["Time"] = "Vaqt",
        ["Crashes"] = "Halokatlar",
        ["Score"] = "Ball",
        ["no scoring - just fly"] = "ballsiz - shunchaki uching",
        ["C camera   M mode   R fresh pack   F drop   Esc menu"] = "C kamera   M rejim   R yangi batareya   F tashlash   Esc menyu",

        // ---- drill titles ----------------------------------------------------------------------
        ["Hover in the box"] = "Quti ichida muallaq turish",
        ["Precision landing"] = "Aniq qo'nish",
        ["Orbit the pylon"] = "Ustun atrofida aylanish",
        ["Gate course"] = "Darvozalar trassasi",
        ["Gates in ACRO"] = "ACRO rejimida darvozalar",
        ["Race - 3 laps"] = "Poyga - 3 aylana",
        ["Time trial - beat 22 s"] = "Vaqtga poyga - 22 s dan tez",
        ["Rolls x3"] = "Roll x3",
        ["Flips x3"] = "Flip x3",
        ["Power loops x2"] = "Pauer-lup x2",
        ["Balloon pop"] = "Sharlarni yorish",
        ["Water-balloon drop"] = "Suv shari tashlash",
        ["Hit the marked car"] = "Belgilangan mashinani urish",
        ["Speed hit - 12 m/s"] = "Tezlikda urish - 12 m/s",
        ["Kamikaze demolition"] = "Kamikadze buzish",
        ["Autopilot agent"] = "Avtopilot agent",

        // ---- drill briefings ------------------------------------------------------------------
        ["Arm, lift off and hold the drone inside the yellow box for 10 seconds without leaving it. A 5\" quad hovers at ~35% throttle - small stick inputs!"] =
            "Dronni yoqing, ko'taring va sariq quti ichida 10 soniya davomida undan chiqmasdan ushlab turing. 5 dyuymli kvadrokopter ~35% gazda muallaq turadi - stiklarni ozgina harakatlantiring!",
        ["Climb above 3 m, then land on the orange H pad softly (under 2 m/s) and stay still for 2 seconds. Hard landings break props!"] =
            "3 m dan yuqoriga ko'tariling, so'ng to'q sariq H maydonchasiga yumshoq (2 m/s dan sekin) qo'ning va 2 soniya qimirlamang. Qattiq qo'nish parraklarni sindiradi!",
        ["Fly 2 full circles around the red-and-white pylon, staying 3-8 m from it and above 1 m. Yaw to keep the nose on the pylon while rolling around it."] =
            "Qizil-oq ustun atrofida 2 ta to'liq aylana uching: undan 3-8 m masofada va 1 m dan yuqorida qoling. Atrofida roll qilib uchayotganda burunni ustunga qaratib turish uchun yaw dan foydalaning.",
        ["Fly through the gates in order. The next gate glows green. Use yaw to turn and pitch to move forward."] =
            "Darvozalardan tartib bilan o'ting. Keyingi darvoza yashil yonadi. Burilish uchun yaw, oldinga harakat uchun pitch dan foydalaning.",
        ["Same course in ACRO (press M). No self-levelling: you must actively level the drone. Two laps."] =
            "O'sha trassa ACRO rejimida (M ni bosing). Avtomatik tekislanish yo'q: dronni o'zingiz tekislashingiz kerak. Ikki aylana.",
        ["Wait for the 3-2-1-GO countdown (crossing a gate early is a false start), then three timed laps. Your best lap is remembered."] =
            "3-2-1-START sanog'ini kuting (darvozadan erta o'tish - noto'g'ri start), so'ng vaqt o'lchanadigan uch aylana. Eng yaxshi aylanangiz eslab qolinadi.",
        ["Two laps after the countdown. Full marks only if your best lap is under 22 seconds - carry speed through the gates, brake with pitch, not throttle."] =
            "Sanoqdan keyin ikki aylana. To'liq ball faqat eng yaxshi aylanangiz 22 soniyadan qisqa bo'lsa - darvozalardan tezlikni saqlab o'ting, gaz bilan emas, pitch bilan tormozlang.",
        ["Switch to ACRO (M), climb above 6 m, then do three full rolls (roll stick all the way, hold, centre when level). Catch each one before it drops."] =
            "ACRO ga o'ting (M), 6 m dan yuqoriga ko'tariling, so'ng uchta to'liq roll bajaring (roll stikini oxirigacha, ushlab turing, tekislanganda markazga). Har birini tushib ketmasdan ushlang.",
        ["ACRO, above 6 m: three full flips (pitch stick fully forward or back, hold, centre when level). Punch the throttle just before you start."] =
            "ACRO, 6 m dan yuqorida: uchta to'liq flip (pitch stiki to'liq oldinga yoki orqaga, ushlab turing, tekislanganda markazga). Boshlashdan oldin gazni keskin oshiring.",
        ["ACRO: dive to gain speed, pull up hard and keep pulling over the top so you gain 2.5 m or more at the apex, then complete the loop. Two of them."] =
            "ACRO: tezlik olish uchun sho'ng'ing, keskin tortib yuqoriga chiqing va eng yuqori nuqtada 2,5 m yoki undan ko'p ko'tarilguncha tortishda davom eting, so'ng aylanani yakunlang. Ikki marta.",
        ["14 balloons are tied around the field at different heights. Fly through every one of them - fastest time wins."] =
            "Maydon bo'ylab turli balandliklarda 14 ta shar bog'langan. Ularning har biridan uchib o'ting - eng tez vaqt g'olib.",
        ["You carry 6 water balloons (they weigh you down). Fly over a bullseye and press F to release. The balloon keeps your speed and falls with drag, so lead the target. 10 / 5 / 2 points by ring."] =
            "Sizda 6 ta suv shari bor (ular og'irlik qiladi). Nishon ustidan uching va tashlash uchun F ni bosing. Shar sizning tezligingizni saqlab, havo qarshiligi bilan tushadi - nishondan oldinroq tashlang. Halqalar bo'yicha 10 / 5 / 2 ball.",
        ["Only the RED target car counts (leftmost bay, spinning marker). Fly into it. Hitting anything else wastes a drone. Fewer drones used = more stars."] =
            "Faqat QIZIL nishon mashina hisoblanadi (eng chapdagi joy, aylanuvchi belgi). Unga uchib boring. Boshqa narsaga urilish dronni bekorga sarflaydi. Kamroq dron = ko'proq yulduz.",
        ["Hit the BLUE car (middle bay) at 12 m/s or faster. Build speed on a long straight approach, stay low, and don't flare before impact."] =
            "KO'K mashinani (o'rtadagi joy) 12 m/s yoki undan tez urib yuboring. Uzun to'g'ri yondashuvda tezlik yig'ing, pastda uching va urilishdan oldin burunni ko'tarmang.",
        ["Cars, crate towers, barrel pyramids and walls. Demolish 60% of everything with 5 drones - pick targets where one blast topples the most."] =
            "Mashinalar, yashik minoralari, bochka piramidalari va devorlar. 5 ta dron bilan hammasining 60% ini buzing - bitta portlash eng ko'p narsani ag'daradigan nishonlarni tanlang.",
        ["The drone flies the mission by itself: take-off, the gate loop at 4 m, return and land. Watch from any camera (C). Move the sticks to take over; Backspace restarts the agent."] =
            "Dron missiyani o'zi bajaradi: ko'tarilish, 4 m balandlikda darvoza halqasi, qaytish va qo'nish. Istalgan kameradan kuzating (C). Boshqaruvni olish uchun stiklarni harakatlantiring; Backspace agentni qayta ishga tushiradi.",
    };
}
