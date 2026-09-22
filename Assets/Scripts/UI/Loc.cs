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
        // ---- navigation / settings page ----
        ["Disciplines"] = "Yo'nalishlar",
        ["Close"] = "Yopish",
        ["SETTINGS"] = "SOZLAMALAR",
        ["changes are saved automatically"] = "o'zgarishlar avtomatik saqlanadi",
        ["Transmitter / input"] = "Pult / boshqaruv",
        ["Calibrate transmitter sticks"] = "Pult stiklarini kalibrlash",
        ["Cancel"] = "Bekor qilish",
        ["Gamepad: throttle stick centre = 50%"] = "Geympad: gaz stikining markazi = 50%",
        ["Rates (Betaflight Actual Rates)"] = "Tezliklar (Betaflight Actual Rates)",
        ["Flight"] = "Parvoz",
        ["Beginner throttle assist (stick centre = hover; NOT realistic)"] = "Boshlang'ich uchun gaz yordami (stik markazi = muallaq turish; REAL EMAS)",
        ["Air mode"] = "Air mode",
        ["Infinite battery"] = "Cheksiz batareya",
        ["Ground effect"] = "Yer effekti",
        ["FPV camera"] = "FPV kamera",
        ["Analog video look (noise, breakup by range)"] = "Analog video ko'rinishi (shovqin, masofaga qarab uzilish)",
        ["Audio"] = "Ovoz",
        ["Show stick overlay"] = "Stiklar ko'rsatkichini ko'rsatish",
        ["View:"] = "Ko'rinish:",
        ["Detected: {0}   joystick={1}  gamepad={2}"] = "Aniqlandi: {0}   joystick={1}  geympad={2}",
        ["yes"] = "bor",
        ["no"] = "yo'q",
        // settings sliders
        ["Deadband"] = "O'lik zona",
        ["Roll/Pitch centre sensitivity (deg/s)"] = "Roll/Pitch markaziy sezgirligi (grad/s)",
        ["Roll/Pitch max rate (deg/s)"] = "Roll/Pitch maksimal tezligi (grad/s)",
        ["Roll/Pitch expo"] = "Roll/Pitch ekspo",
        ["Yaw centre sensitivity"] = "Yaw markaziy sezgirligi",
        ["Yaw max rate"] = "Yaw maksimal tezligi",
        ["Yaw expo"] = "Yaw ekspo",
        ["Angle mode max tilt (deg)"] = "Angle rejimida maksimal qiyalik (grad)",
        ["Wind speed (m/s)"] = "Shamol tezligi (m/s)",
        ["Gustiness"] = "Shamol kuchayishi",
        ["Turbulence (m/s)"] = "Turbulentlik (m/s)",
        ["Video latency (ms)"] = "Video kechikishi (ms)",
        ["Lens distortion"] = "Linza buzilishi",
        ["Camera FOV (vertical, deg)"] = "Kamera ko'rish burchagi (vertikal, grad)",
        ["Master volume"] = "Umumiy ovoz balandligi",

        // ---- briefing labels ----
        ["Difficulty"] = "Qiyinlik",
        ["YOU LEARN"] = "NIMA O'RGANASIZ",
        ["WATCH OUT"] = "DIQQAT",
        ["CONTROLS"] = "BOSHQARUV",

        // ---- drill goals ----
        ["Lift off, then keep the drone inside the yellow box for 10 seconds."] = "Ko'tarilib, dronni sariq quti ichida 10 soniya ushlab turing.",
        ["Climb above 3 m, then land softly on the orange H pad and stay still for 2 seconds."] = "3 m dan yuqoriga ko'tarilib, to'q sariq H maydonchasiga yumshoq qo'ning va 2 soniya qimirlamang.",
        ["Fly 2 full circles around the striped pylon, staying 3-8 m from it and above 1 m."] = "Chiziqli ustun atrofida 2 to'liq aylana uching: undan 3-8 m masofada va 1 m dan yuqorida.",
        ["Fly through all 8 gates in order. The next gate glows green."] = "Barcha 8 darvozadan tartib bilan o'ting. Keyingi darvoza yashil yonadi.",
        ["Fly two laps of the same course in ACRO, where the drone does not level itself."] = "O'sha trassada ACRO rejimida ikki aylana uching - bu rejimda dron o'zini tekislamaydi.",
        ["Wait for the 3-2-1-GO countdown, then fly three timed laps."] = "3-2-1-START sanog'ini kutib, vaqt o'lchanadigan uch aylana uching.",
        ["Beat 22 seconds on a single lap. You get two laps to try."] = "Bitta aylanani 22 soniyadan tez uching. Ikki aylana urinish beriladi.",
        ["In ACRO, above 6 m, do three full rolls."] = "ACRO rejimida, 6 m dan yuqorida uchta to'liq roll bajaring.",
        ["In ACRO, above 6 m, do three full flips."] = "ACRO rejimida, 6 m dan yuqorida uchta to'liq flip bajaring.",
        ["Do two power loops: dive for speed, then loop up and over the top."] = "Ikki pauer-lup bajaring: tezlik uchun sho'ng'ib, so'ng yuqoriga aylanib o'ting.",
        ["Fly through all 14 balloons tied around the yard."] = "Maydon bo'ylab bog'langan 14 ta sharning hammasidan uchib o'ting.",
        ["Drop water balloons onto the three bullseyes. You carry 6."] = "Suv sharlarini uchta nishonga tashlang. Sizda 6 ta shar bor.",
        ["Fly into the RED car only. You have 3 drones."] = "Faqat QIZIL mashinaga uchib boring. Sizda 3 ta dron bor.",
        ["Hit the BLUE car at 12 m/s or faster. You have 3 drones."] = "KO'K mashinani 12 m/s yoki tezroq urib yuboring. Sizda 3 ta dron bor.",
        ["Demolish 60% of the yard using 5 drones."] = "5 ta dron bilan maydonning 60% ini buzing.",
        ["Watch the drone fly the whole mission by itself, then take over whenever you like."] = "Dron butun missiyani o'zi uchishini kuzatib, xohlagan vaqtda boshqaruvni o'z qo'lingizga oling.",

        // ---- what the drill teaches ----
        ["Throttle control - holding a steady height is the first thing every pilot must own."] = "Gazni boshqarish - bir tekis balandlikni ushlab turish har bir uchuvchi egallashi kerak bo'lgan birinchi ko'nikma.",
        ["Controlled descent and a soft touch-down - the part of every flight that breaks props."] = "Nazorat ostida pasayish va yumshoq qo'nish - parvozning parraklar ko'p sinadigan qismi.",
        ["Coordinating yaw with roll - the turn that lets you follow a subject or a track."] = "Yaw va rollni birga ishlatish - nishon yoki trassani kuzatib uchish imkonini beradigan burilish.",
        ["Reading a course and lining up on a target - aim at the gate before you reach it."] = "Trassani o'qish va nishonga tenglanish - darvozaga yetib bormasdan oldin unga qarab turing.",
        ["Flying without self-levelling - the mode real freestyle and race pilots use."] = "Avtomatik tekislanishsiz uchish - haqiqiy fristayl va poyga uchuvchilari ishlatadigan rejim.",
        ["Racing under pressure and starting cleanly - your best lap is saved as a record."] = "Bosim ostida poyga va toza start - eng yaxshi aylanangiz rekord sifatida saqlanadi.",
        ["Flying a racing line: wide entry, tight exit, no wasted movement."] = "Poyga chizig'i bo'ylab uchish: keng kirish, tor chiqish, keraksiz harakatlarsiz.",
        ["Throwing and catching a roll - the basic freestyle move."] = "Rollni tashlash va ushlash - fristaylning asosiy harakati.",
        ["The flip - the same catch as a roll, but around the other axis and with more height loss."] = "Flip - roll kabi ushlash, lekin boshqa o'q atrofida va balandlik ko'proq yo'qoladi.",
        ["Trading height for speed and back again - the core of freestyle flying."] = "Balandlikni tezlikka, tezlikni balandlikka aylantirish - fristayl uchishning asosi.",
        ["Planning an efficient route and hitting a small target in three dimensions."] = "Tejamkor marshrut tuzish va uch o'lchamda kichik nishonga tegish.",
        ["Leading a target - a released load keeps your speed and falls behind you."] = "Nishondan oldinroq tashlash - qo'yib yuborilgan yuk sizning tezligingizni saqlab, orqaroqqa tushadi.",
        ["Committing to a chosen target and hitting it accurately."] = "Tanlangan nishonga qat'iy yo'nalish olish va unga aniq tegish.",
        ["Building and holding speed on an approach, and reading your own speed display."] = "Yondashuvda tezlik yig'ish va uni saqlash, hamda tezlik ko'rsatkichini o'qish.",
        ["Choosing the target that gives the most result - the same judgement as picking a landing spot."] = "Eng ko'p natija beradigan nishonni tanlash - qo'nish joyini tanlashdagi kabi mulohaza.",
        ["What a smooth, correct flight looks like - copy the heights, speeds and turns you see."] = "Silliq va to'g'ri parvoz qanday bo'lishi - ko'rgan balandlik, tezlik va burilishlarni takrorlang.",

        // ---- common mistakes ----
        ["Students push the throttle too far and shoot past the box. Move it a little, then wait a moment to see the result."] = "O'quvchilar gazni ko'p beradi va qutidan o'tib ketadi. Ozgina qo'shing, so'ng natijani ko'rish uchun bir lahza kuting.",
        ["Cutting the throttle to zero over the pad drops the drone. Sink slowly, under 2 m/s, and keep a little throttle right to the ground."] = "Maydoncha ustida gazni nolga tushirish dronni yerga uradi. Sekin, 2 m/s dan kam tezlikda pasayib, yergacha ozgina gaz saqlang.",
        ["Rolling alone makes you drift away from the pylon. Add a little yaw all the way round so the nose keeps pointing at it."] = "Faqat roll qilsangiz ustundan uzoqlashib ketasiz. Butun aylana bo'ylab ozgina yaw qo'shib, burunni ustunga qaratib turing.",
        ["Turning at the last moment clips the posts. Point the nose at the next gate while you are still far away."] = "Oxirgi lahzada burilish ustunlarga urilishga olib keladi. Hali uzoqda turib burunni keyingi darvozaga qaratib oling.",
        ["In ACRO the drone keeps whatever tilt you give it. After every input return the stick to centre and level the drone yourself."] = "ACRO da dron siz bergan qiyalikni saqlab qoladi. Har bir harakatdan keyin stikni markazga qaytarib, dronni o'zingiz tekislang.",
        ["Crossing a gate before GO is a false start and restarts the countdown. Hold your position until you see GO."] = "START dan oldin darvozadan o'tish - noto'g'ri start, sanoq qaytadan boshlanadi. START ni ko'rmaguncha joyingizda turing.",
        ["Full throttle everywhere is slower. Ease off before a turn and add it again as you come out."] = "Hamma joyda to'liq gaz - sekinroq natija. Burilishdan oldin gazni kamaytiring, chiqishda yana qo'shing.",
        ["Students catch it too late and lose height. Add throttle first, then centre the stick the moment the drone is level again."] = "O'quvchilar rollni kech ushlaydi va balandlik yo'qotadi. Avval gaz qo'shing, dron tekislangan lahzada stikni markazga qaytaring.",
        ["A flip drops you further than a roll. Start higher than you think you need and punch the throttle just before you begin."] = "Flip rollga qaraganda ko'proq pastga tushiradi. Kerak deb o'ylaganingizdan balandroqda boshlang va boshlashdan oldin gazni keskin oshiring.",
        ["Without enough speed at the bottom you will stall on your back at the top. Dive first, then pull."] = "Pastda tezlik yetarli bo'lmasa, yuqorida chalqancha holatda to'xtab qolasiz. Avval sho'ng'ing, keyin tortib ko'taring.",
        ["Chasing balloons one at a time wastes the clock. Look ahead and link them into a route."] = "Sharlarni birma-bir quvish vaqtni yo'qotadi. Oldinga qarab, ularni bitta marshrutga bog'lang.",
        ["Pressing F right over the target always overshoots. Release before you get there, or slow down first."] = "F ni nishon ustida bosish har doim uzoqqa tushiradi. Yetib bormasdan tashlang yoki avval sekinlashing.",
        ["Hitting anything else wastes a drone. Identify the red car by its spinning orange marker before you accelerate."] = "Boshqa narsaga urilish dronni bekorga sarflaydi. Tezlashishdan oldin qizil mashinani aylanuvchi to'q sariq belgisidan aniqlang.",
        ["Lifting the nose just before impact kills your speed. Stay low, stay committed, and watch SPD in the top-left corner."] = "Urilishdan oldin burunni ko'tarish tezlikni yo'qotadi. Pastda qoling, yo'nalishdan chiqmang va yuqori chap burchakdagi SPD ni kuzatib turing.",
        ["One drone into one crate is wasted. Aim at the base of a tall stack so the whole thing comes down."] = "Bitta dronni bitta yashikka sarflash - behuda. Baland uyumning tagiga uring, shunda butun uyum ag'daladi.",
        ["Do not just watch the drone move. Watch where it slows down and how wide it turns, then fly the same line yourself."] = "Shunchaki dron harakatini kuzatib qolmang. U qayerda sekinlashishini va qanchalik keng burilishini kuzatib, keyin xuddi shu chiziq bo'ylab o'zingiz uchib ko'ring.",

        // ---- controls lines ----
        ["W / S = throttle (up / down),   arrow keys = tilt,   SPACE = arm"] = "W / S = gaz (yuqori / past),   strelkalar = qiyalik,   SPACE = yoqish",
        ["W / S = throttle,   arrow keys = tilt,   SPACE = arm"] = "W / S = gaz,   strelkalar = qiyalik,   SPACE = yoqish",
        ["A / D = yaw (turn),   arrow keys = tilt,   W / S = throttle"] = "A / D = yaw (burilish),   strelkalar = qiyalik,   W / S = gaz",
        ["arrow keys = tilt,   A / D = yaw,   W / S = throttle"] = "strelkalar = qiyalik,   A / D = yaw,   W / S = gaz",
        ["M = switch mode,   arrow keys = tilt,   A / D = yaw,   W / S = throttle"] = "M = rejimni almashtirish,   strelkalar = qiyalik,   A / D = yaw,   W / S = gaz",
        ["M = mode,   arrow keys = tilt,   A / D = yaw,   W / S = throttle"] = "M = rejim,   strelkalar = qiyalik,   A / D = yaw,   W / S = gaz",
        ["M = mode,   left / right arrows = roll,   W / S = throttle"] = "M = rejim,   chap / o'ng strelkalar = roll,   W / S = gaz",
        ["M = mode,   up / down arrows = pitch,   W / S = throttle"] = "M = rejim,   yuqori / past strelkalar = pitch,   W / S = gaz",
        ["F = release a balloon,   arrow keys = tilt,   W / S = throttle"] = "F = sharni tashlash,   strelkalar = qiyalik,   W / S = gaz",
        ["arrow keys = tilt,   W / S = throttle,   SPD shows your speed"] = "strelkalar = qiyalik,   W / S = gaz,   SPD tezligingizni ko'rsatadi",
        ["C = change camera,   move any stick = take over,   Backspace = restart the agent"] = "C = kamerani almashtirish,   istalgan stikni harakatlantirish = boshqaruvni olish,   Backspace = agentni qayta ishga tushirish",

        // ---- steps ----
        ["Throttle at the bottom, then press SPACE to arm. The props spin slowly."] = "Gazni pastga tushirib, yoqish uchun SPACE ni bosing. Parraklar sekin aylanadi.",
        ["Raise the throttle gently with W until the drone leaves the ground, at about 35%."] = "W bilan gazni asta oshirib boring, taxminan 35% da dron yerdan ko'tariladi.",
        ["Nudge the arrow keys to slide across into the yellow box."] = "Sariq quti ichiga siljish uchun strelkalarni ozgina bosing.",
        ["Hold it inside for 10 seconds, correcting early with tiny inputs."] = "Uni 10 soniya ichida ushlab turing, kichik harakatlar bilan oldindan to'g'rilab boring.",
        ["Arm and climb above 3 m."] = "Dronni yoqib, 3 m dan yuqoriga ko'tariling.",
        ["Fly over the orange H pad with small arrow-key nudges."] = "Kichik strelka harakatlari bilan to'q sariq H maydonchasi ustiga uching.",
        ["Lower the throttle slightly and let it sink slowly - slower than 2 m/s."] = "Gazni ozgina kamaytirib, sekin pasayishiga yo'l qo'ying - 2 m/s dan sekin.",
        ["Touch down and hold still for 2 seconds."] = "Yerga tegib, 2 soniya qimirlamang.",
        ["Arm, climb to about 3 m and fly out to one side of the pylon."] = "Yoqing, 3 m atrofiga ko'tarilib, ustunning bir tomoniga uching.",
        ["Roll gently to start moving sideways around it."] = "Uning atrofida yonbosh harakatni boshlash uchun asta roll qiling.",
        ["At the same time hold a little yaw so the nose stays on the pylon."] = "Shu bilan birga ozgina yaw ushlab, burunni ustunda saqlang.",
        ["Keep the distance between 3 and 8 m for two full laps."] = "Ikki to'liq aylana davomida masofani 3-8 m orasida saqlang.",
        ["Arm and climb to gate height, about 2 m."] = "Yoqing va darvoza balandligiga - taxminan 2 m ga ko'tariling.",
        ["Find the gate glowing green - that is your next one."] = "Yashil yonayotgan darvozani toping - u sizning keyingi darvozangiz.",
        ["Yaw to point at it, then tilt forward to fly through the middle."] = "Yaw bilan unga qarang, so'ng oldinga qiyalanib o'rtasidan uchib o'ting.",
        ["Straight after passing, turn towards the next one."] = "O'tgandan so'ng darhol keyingisiga qarab buriling.",
        ["Press M until the top-left corner reads ACRO."] = "Yuqori chap burchakda ACRO yozilmaguncha M ni bosing.",
        ["Arm and lift off with small inputs - it will drift, that is normal."] = "Yoqing va kichik harakatlar bilan ko'tariling - u siljiydi, bu normal.",
        ["Tap the arrows instead of holding them, and level out after each tap."] = "Strelkalarni ushlab turmasdan qisqa bosing va har bosishdan keyin tekislang.",
        ["Complete two laps of the gates."] = "Darvozalar bo'ylab ikki aylanani yakunlang.",
        ["Arm and hover behind the start arch."] = "Yoqing va start darvozasi orqasida muallaq turing.",
        ["Wait for GO - do not cross a gate early."] = "START ni kuting - darvozadan erta o'tib ketmang.",
        ["Fly three laps, carrying your speed through the gates."] = "Uch aylana uching, darvozalardan tezlikni saqlab o'ting.",
        ["Brake by tilting back, not by cutting the throttle."] = "Gazni kamaytirib emas, orqaga qiyalanib tormozlang.",
        ["Wait for GO, then build speed along the straight."] = "START ni kutib, to'g'ri qismda tezlik yig'ing.",
        ["Enter each turn from the outside and cut close to the inside gate."] = "Har bir burilishga tashqi tomondan kirib, ichki darvozaga yaqin o'ting.",
        ["Keep the drone smooth - every wobble costs time."] = "Dronni silliq ushlang - har bir tebranish vaqt yo'qotadi.",
        ["The better of your two laps counts."] = "Ikki aylanadan yaxshirog'i hisobga olinadi.",
        ["Press M for ACRO and climb above 6 m."] = "ACRO uchun M ni bosib, 6 m dan yuqoriga ko'tariling.",
        ["Add a little throttle to gain height before you start."] = "Boshlashdan oldin balandlik olish uchun ozgina gaz qo'shing.",
        ["Push the roll stick fully to one side and hold it."] = "Roll stikini bir tomonga to'liq bosib, ushlab turing.",
        ["The instant the drone is level again, centre the stick and add throttle."] = "Dron yana tekislangan lahzada stikni markazga qaytarib, gaz qo'shing.",
        ["ACRO mode, climb above 6 m."] = "ACRO rejimi, 6 m dan yuqoriga ko'tariling.",
        ["Punch the throttle for a moment to gain height."] = "Balandlik olish uchun bir lahza gazni keskin oshiring.",
        ["Push the pitch stick fully forward or back and hold it."] = "Pitch stikini to'liq oldinga yoki orqaga bosib, ushlab turing.",
        ["Catch it level, centre the stick, add throttle."] = "Tekislangan holatda ushlang, stikni markazga qaytaring, gaz qo'shing.",
        ["ACRO, climb high and dive to build speed."] = "ACRO, balandga chiqib, tezlik yig'ish uchun sho'ng'ing.",
        ["Pull back steadily and keep pulling over the top."] = "Bir tekis orqaga tortib, eng yuqori nuqtadan o'tguncha tortishda davom eting.",
        ["You must gain 2.5 m or more at the highest point."] = "Eng yuqori nuqtada 2,5 m yoki undan ko'p balandlik olishingiz kerak.",
        ["Complete the circle and fly out level."] = "Aylanani yakunlab, tekis holatda chiqib keting.",
        ["Arm and take off from the pad."] = "Yoqing va maydonchadan ko'tariling.",
        ["Pick the nearest group of balloons, not just the nearest balloon."] = "Eng yaqin bitta sharni emas, eng yaqin shar guruhini tanlang.",
        ["Fly through each one - touching it pops it."] = "Har biridan uchib o'ting - tegish bilan shar yoriladi.",
        ["Keep moving: your time is scored."] = "To'xtamang: vaqtingiz ham baholanadi.",
        ["Arm and take off - the balloons make the drone heavier."] = "Yoqing va ko'tariling - sharlar dronni og'irlashtiradi.",
        ["Fly over a bullseye at a steady height."] = "Bir tekis balandlikda nishon ustidan uching.",
        ["Press F slightly BEFORE you are above the centre."] = "Markaz ustiga yetib bormasdan, sal OLDIN F ni bosing.",
        ["Watch where it lands and correct the next drop. 10 / 5 / 2 points by ring."] = "Qayerga tushganini kuzatib, keyingi tashlashni to'g'rilang. Halqalar bo'yicha 10 / 5 / 2 ball.",
        ["Arm and climb a few metres."] = "Yoqing va bir necha metr ko'tariling.",
        ["Find the RED car - the one with the spinning orange marker."] = "QIZIL mashinani toping - aylanuvchi to'q sariq belgisi borini.",
        ["Line up on it from a distance and fly straight in."] = "Uzoqdan unga tenglanib, to'g'ri uchib boring.",
        ["Fewer drones used = more stars."] = "Kamroq dron sarflansa = ko'proq yulduz.",
        ["Arm and fly well away from the car to make room."] = "Yoqing va joy qoldirish uchun mashinadan yaxshi uzoqlashing.",
        ["Turn back and accelerate along a long straight line."] = "Qaytib burilib, uzun to'g'ri chiziq bo'ylab tezlashing.",
        ["Keep the SPD readout above 12 m/s."] = "SPD ko'rsatkichini 12 m/s dan yuqorida saqlang.",
        ["Hold that line into the blue car without flaring."] = "Burunni ko'tarmasdan, shu chiziq bo'ylab ko'k mashinaga boring.",
        ["Look over the yard: cars, crate towers, barrel pyramids and walls."] = "Maydonni ko'zdan kechiring: mashinalar, yashik minoralari, bochka piramidalari va devorlar.",
        ["Pick the target where a single hit topples the most."] = "Bitta urilish eng ko'p narsani ag'daradigan nishonni tanlang.",
        ["Hit it low, at the base, with speed."] = "Pastdan, tagidan, tezlik bilan uring.",
        ["Repeat with your remaining drones until you pass 60%."] = "60% dan o'tguncha qolgan dronlar bilan takrorlang.",
        ["Start the drill - the drone arms and takes off on its own."] = "Mashqni boshlang - dron o'zi yonib, o'zi ko'tariladi.",
        ["Press C to watch from the chase, pilot and FPV cameras."] = "Kuzatuv, uchuvchi va FPV kameralaridan ko'rish uchun C ni bosing.",
        ["Move a stick at any time to take control."] = "Boshqaruvni olish uchun istalgan vaqtda stikni harakatlantiring.",
        ["Press Backspace to hand the mission back to the agent."] = "Missiyani agentga qaytarish uchun Backspace ni bosing.",

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
        ["The drone flies the mission by itself: take-off, the gate loop at 6 m, return and land. Watch from any camera (C). Move the sticks to take over; Backspace restarts the agent."] =
            "Dron missiyani o'zi bajaradi: ko'tarilish, 6 m balandlikda darvoza halqasi, qaytish va qo'nish. Istalgan kameradan kuzating (C). Boshqaruvni olish uchun stiklarni harakatlantiring; Backspace agentni qayta ishga tushiradi.",
    };
}
