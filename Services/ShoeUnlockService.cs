using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Port of Android ShoeUnlockEngine:
/// Evaluates the runner's Top 3 fastest qualifying runs (>= 3 km) in the last 30 days
/// so easy/long runs never pull down tier unlocks.
/// </summary>
public sealed class ShoeUnlockService
{
    public IReadOnlyList<ShoeTierDto> Tiers { get; } =
    [
        new(
            Id: 1,
            Title: "Daily Cushion & Recovery Trainers",
            Subtitle: "Aerobic Base, Easy Runs & Recovery",
            UnlockPaceSecondsPerKm: 9999L,
            UnlockPaceDisplay: "Semua Pace",
            BadgeLabel: "Tier 1 • Cushion",
            Description: "Pilihan sepatu Daily Running / Easy terkurasi berbusa tebal untuk menyerap benturan, melindungi sendi, dan membangun fondasi aerobik.",
            BiomechanicalRationale: "Pada fase membangun base atau recovery, kaki membutuhkan peredaman benturan maksimal tanpa pelat kaku agar adaptasi tulang, tendon, dan kapiler berjalan optimal.",
            Shoes:
            [
                new(
                    "Vomero Plus", "Nike", "Non-Plated · Full-Length ZoomX (Skor Lab 91)",
                    "42.3 / 32.7 mm", 9.6, 289,
                    "Daily Running, Recovery Days & Relaxed Runs",
                    "Midsole full-length ZoomX berkonsep maksimalis dengan upper super nyaman dan konstruksi awet untuk menjaga kaki tetap segar saat pemulihan.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40912/nike-vomero-plus-23479827-380.jpg",
                    Slug: "nike-vomero-plus"
                ),
                new(
                    "Vomero 18", "Nike", "Non-Plated · ZoomX Cushion (Skor Lab 90)",
                    "42.5 / 28.6 mm", 13.9, 298,
                    "Daily Running, Max Cushion & Heel Strikers",
                    "Desain ulang total dengan tumpukan busa ZoomX lebih tebal, step-in comfort ekstra empuk, serta drop besar untuk meredam hentakan tumit.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40615/nike-vomero-18-23005571-380.jpg",
                    Slug: "nike-vomero-18"
                ),
                new(
                    "Clifton 10", "HOKA", "Non-Plated · Supercritical EVA (Skor Lab 86)",
                    "44.4 / 32.0 mm", 12.4, 275,
                    "Daily Running, Stable Base Miles",
                    "Generasi ke-10 berstack maksimalis 44.4 mm dengan toebox lebih lega dan platform stabil untuk lari harian jarak ringan hingga menengah.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40691/hoka-clifton-10-22918659-380.jpg",
                    Slug: "hoka-clifton-10"
                ),
                new(
                    "Bondi 9", "HOKA", "Non-Plated · Supercritical EVA (Skor Lab 86)",
                    "41.3 / 32.2 mm", 9.1, 303,
                    "Daily Running, Recovery & Joint Protection",
                    "Lebih ringan dan lebih awet dibanding v8 dengan busa supercritical lebih membal dan material upper premium.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40639/hoka-bondi-9-22560581-380.jpg",
                    Slug: "hoka-bondi-9"
                )
            ]
        ),
        new(
            Id: 2,
            Title: "Lightweight Daily & Tempo Trainers",
            Subtitle: "Cadence Turnover, Fartlek & Mixed Pace",
            UnlockPaceSecondsPerKm: 420L,
            UnlockPaceDisplay: "Pace < 7:00/km",
            BadgeLabel: "Tier 2 • Performance",
            Description: "Sepatu Daily & Tempo ringan pilihan terbaik dengan busa responsif untuk meningkatkan pergantian langkah (cadence) saat pace mulai menanjak.",
            BiomechanicalRationale: "Mengurangi bobot sepatu ke kisaran 207–254 gram meringankan beban rotasi ayunan kaki sehingga turnover langkah >170 spm terasa lebih natural.",
            Shoes:
            [
                new(
                    "Adizero EVO SL", "adidas", "Non-Plated · 100% Lightstrike Pro (Skor Lab 94)",
                    "36.1 / 28.1 mm", 8.0, 223,
                    "Daily Running, Tempo & Versatile Paces",
                    "Membawa 100% busa balap Lightstrike Pro dan outsole karet Continental dalam bobot hanya 223 g dengan fleksibilitas pace luar biasa.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40586/adidas-adizero-evo-sl-22256345-380.jpg",
                    Slug: "adidas-adizero-evo-sl"
                ),
                new(
                    "Novablast 5", "ASICS", "Non-Plated · FF Blast+ ECO (Skor Lab 93)",
                    "40.9 / 33.5 mm", 7.4, 254,
                    "Daily Running, Tempo & Long Steady",
                    "Lebih ringan, empuk, dan membal dibanding pendahulunya dengan stabilitas mengejutkan untuk membangun kecepatan harian.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40608/asics-novablast-5-22312736-380.jpg",
                    Slug: "asics-novablast-5"
                ),
                new(
                    "Mach 7", "HOKA", "Non-Plated · Supercritical EVA (Skor Lab 90)",
                    "35.6 / 26.2 mm", 9.4, 224,
                    "Daily Running, Tempo & Cadence Drills",
                    "Mempertahankan DNA super lincah berbobot 224 g dengan peningkatan upper dan daya cengkeram outsole untuk latihan tempo cepat.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/41083/hoka-mach-7-24969866-380.jpg",
                    Slug: "hoka-mach-7"
                ),
                new(
                    "SuperComp Rebel", "New Balance", "Carbon Plate · FuelCell (Skor Lab 94)",
                    "38.3 / 29.5 mm", 8.8, 207,
                    "Tempo, Speed Play & Race Prep",
                    "Ringan 207 g dengan busa FuelCell berpantulan tinggi dan traksi kuat yang membawa seri trainer ke level performa balap.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/41475/new-balance-super-comp-rebel-25437549-380.jpg",
                    Slug: "new-balance-supercomp-rebel"
                )
            ]
        ),
        new(
            Id: 3,
            Title: "Super Trainers & Tempo Workhorses",
            Subtitle: "Lactate Threshold & Marathon Pace",
            UnlockPaceSecondsPerKm: 360L,
            UnlockPaceDisplay: "Pace < 6:00/km",
            BadgeLabel: "Tier 3 • Super Trainer",
            Description: "Kumpulan Super Trainer skor tertinggi terbaik: tumpukan superfoam PEBA/LEAP dan/atau pelat nilon-karbon untuk menjaga pace threshold tetap efisien.",
            BiomechanicalRationale: "Kombinasi stack 37–46 mm berdensitas rendah dan geometri propulsif menjaga ekonomi lari serta meminimalkan kelelahan otot betis pada pace tempo panjang.",
            Shoes:
            [
                new(
                    "Megablast", "ASICS", "Non-Plated · FF Turbo (Skor Lab 95)",
                    "45.1 / 35.2 mm", 9.9, 218,
                    "Tempo, Competition & Long Fast Runs",
                    "Peraih skor performa tertinggi (95/100): energy return eksplosif, peredaman benturan superior, grip kelas satu, dan bobot sangat ringan 218 g.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40948/asics-megablast-23653319-380.jpg",
                    Slug: "asics-megablast"
                ),
                new(
                    "Superblast 3", "ASICS", "Non-Plated · FF LEAP (Skor Lab 94)",
                    "45.8 / 37.7 mm", 8.1, 235,
                    "Daily Running, Threshold & Marathon Pace",
                    "Super trainer berbusa FF LEAP setebal 45.8 mm dengan grip lebih menggigit dan bobot turun ke 235 g sambil tetap stabil.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/41200/asics-superblast-3-24636359-380.jpg",
                    Slug: "asics-superblast-3"
                ),
                new(
                    "Endorphin Speed 5", "Saucony", "Winged Nylon Plate · PWRRUN PB (Skor Lab 94)",
                    "37.4 / 26.8 mm", 10.6, 241,
                    "Tempo Runs, Threshold & Half Marathon",
                    "Pelat nilon bersayap dan busa PWRRUN PB menghasilkan pantulan kelas dunia dengan sirkulasi udara upper yang sangat sejuk.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40830/saucony-endorphin-speed-5-23261039-380.jpg",
                    Slug: "saucony-endorphin-speed-5"
                ),
                new(
                    "Superblast 2", "ASICS", "Non-Plated · FF Turbo+ / Blast+ (Skor Lab 91)",
                    "42.8 / 34.6 mm", 8.2, 252,
                    "Tempo, Marathon Pace & High Mileage",
                    "Salah satu trainer paling serbaguna dengan midsole bertenaga dan outsole solid untuk menjaga konsistensi pace panjang.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40450/asics-superblast-2-21749161-380.jpg",
                    Slug: "asics-superblast-2"
                ),
                new(
                    "Zoom Fly 6", "Nike", "Carbon Plate · ZoomX (Skor Lab 88)",
                    "39.7 / 30.1 mm", 9.6, 248,
                    "Tempo, Threshold Intervals & Long Fast",
                    "Memadukan busa ZoomX responsif, pelat karbon, serta durabilitas outsole tinggi sebagai pasangan latihan super-shoe harian.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40613/nike-zoom-fly-6-22464236-380.jpg",
                    Slug: "nike-zoom-fly-6"
                ),
                new(
                    "FuelCell SuperComp Trainer v3", "New Balance", "Carbon Plate · FuelCell (Skor Lab 89)",
                    "36.8 / 29.5 mm", 7.3, 278,
                    "Tempo Workouts & High-Stability Long Runs",
                    "Versi paling stabil dan bertenaga di lini SC Trainer dengan grip sangat baik untuk volume latihan kecepatan mingguan.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40405/new-balance-fuel-cell-super-comp-trainer-v-3-21926162-380.jpg",
                    Slug: "new-balance-fuelcell-supercomp-trainer-v3"
                )
            ]
        ),
        new(
            Id: 4,
            Title: "Carbon Plated Competition Racers",
            Subtitle: "Race Day 5K, 10K, HM & Marathon PRs",
            UnlockPaceSecondsPerKm: 300L,
            UnlockPaceDisplay: "Pace < 5:00/km",
            BadgeLabel: "Tier 4 • Carbon Racer",
            Description: "Sepatu balap Competition / Race teruji terbaik dengan pelat serat karbon penuh berbobot sub-215 gram untuk memecahkan rekor personal lomba.",
            BiomechanicalRationale: "Pelat serat karbon kaku mengurangi tekukan berlebih pada sendi MTP telapak depan dan melontarkan kembali energi kompresi busa super di tiap langkah.",
            Shoes:
            [
                new(
                    "Vaporfly 4", "Nike", "Carbon Plate · ZoomX (Skor Lab 93)",
                    "34.1 / 25.5 mm", 8.6, 166,
                    "Competition 5K, 10K & Half Marathon PR",
                    "Super-shoe super ringan 166 g dengan penyempurnaan lidah/fit upper dan transisi sangat natural untuk memecahkan PR jarak pendek-menengah.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40754/nike-vaporfly-4-22860848-380.jpg",
                    Slug: "nike-vaporfly-4"
                ),
                new(
                    "Adizero Adios Pro 4", "adidas", "Carbon EnergyRods · Lightstrike Pro (Skor Lab 93)",
                    "36.6 / 28.5 mm", 8.1, 200,
                    "Competition Half & Full Marathon Race",
                    "Menghadirkan batang karbon EnergyRods dengan busa Lightstrike Pro lebih empuk dan ledakan dorongan ke depan di bobot tepat 200 g.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40584/adidas-adizero-adios-pro-4-22483965-380.jpg",
                    Slug: "adidas-adizero-adios-pro-4"
                ),
                new(
                    "Endorphin Pro 5", "Saucony", "Carbon Plate · PWRRUN PB (Skor Lab 91)",
                    "36.5 / 27.8 mm", 8.7, 215,
                    "Competition Marathon & Heel-Strike Stability",
                    "Super shoe lomba berpelat karbon paling ramah kestabilan, nyaman untuk heel striker dan percaya diri pada tikungan maupun jalan basah.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/41067/saucony-endorphin-pro-5-24665925-380.jpg",
                    Slug: "saucony-endorphin-pro-5"
                )
            ]
        ),
        new(
            Id: 5,
            Title: "Elite Pinnacle Competition Super-Shoes",
            Subtitle: "Sub-4:00 Pace, Podium & Record Tech",
            UnlockPaceSecondsPerKm: 240L,
            UnlockPaceDisplay: "Pace < 4:00/km",
            BadgeLabel: "Tier 5 • Alpha Weapon",
            Description: "Kasta kompetisi tertinggi terkurasi: super-shoe ultra-ekstrem sub-190 g hingga sub-100 g untuk efisiensi biomekanika maksimal pada pace elit.",
            BiomechanicalRationale: "Pada kecepatan >15 km/jam, gaya kontak kaki cukup besar untuk memeras kompresi penuh superfoam generasi baru sambil menjaga bobot serendah mungkin.",
            Shoes:
            [
                new(
                    "Alphafly 4", "Nike", "Carbon Plate · ZoomX (Skor Lab 94)",
                    "39.0 / 32.8 mm", 6.2, 190,
                    "Competition Marathon Record & Podium",
                    "Alphafly terbaik di kelasnya: lebih ringan (190 g), pantulan lebih bertenaga, traksi membaik, dan lock-down Atomknit paling aman.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/41531/nike-alphafly-4-25929108-380.jpg",
                    Slug: "nike-alphafly-4"
                ),
                new(
                    "Metaspeed Sky Tokyo", "ASICS", "Carbon Plate · FF LEAP (Skor Lab 92)",
                    "38.7 / 32.7 mm", 6.0, 163,
                    "Competition Elite Road & Full Marathon",
                    "Konstruksi super ringan 163 g dengan ventilasi maksimal, busa FF LEAP empuk, dan pelat karbon agresif untuk mempertahankan kecepatan puncak.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/40812/asics-metaspeed-sky-tokyo-23429911-380.jpg",
                    Slug: "asics-metaspeed-sky-tokyo"
                ),
                new(
                    "Adizero Adios Pro Evo 3", "adidas", "Carbon Plate · Lightstrike Pro Evo (Skor Lab 91)",
                    "38.1 / 32.2 mm", 5.9, 99,
                    "Elite Championship & Sub-2h Architecture",
                    "Inovasi ultra-ekstrem berbobot 99 g di timbangan lab dengan stack 38.1 mm, menghadirkan energy return dan shock absorption level puncak dunia.",
                    "https://cdn.runrepeat.com/storage/gallery/product_primary/41285/adidas-adizero-adios-pro-evo-3-24813691-380.jpg",
                    Slug: "adidas-adizero-adios-pro-evo-3"
                )
            ]
        )
    ];

    public ShoeUnlockProgressDto EvaluateProgress(IReadOnlyList<WorkoutLogDto> workouts, long? nowEpochMs = null)
    {
        var now = nowEpochMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var validWorkouts = workouts.Where(w => w.DistanceKm >= 0.5 && w.DurationSeconds >= 60).ToList();

        var thirtyDaysAgoMs = now - 30L * 86_400_000L;
        var recentRuns = validWorkouts
            .Where(w => (w.CreatedAtEpochMs > 0 ? w.CreatedAtEpochMs : now) >= thirtyDaysAgoMs)
            .ToList();
        if (recentRuns.Count == 0) recentRuns = validWorkouts;

        var qualifyingRuns = recentRuns.Where(w => w.DistanceKm >= 3.0).ToList();
        if (qualifyingRuns.Count == 0) qualifyingRuns = recentRuns;

        var topRuns = qualifyingRuns
            .OrderBy(w => w.DurationSeconds / w.DistanceKm)
            .Take(3)
            .ToList();

        var overallAvgPace = topRuns.Count > 0
            ? (long)Math.Round(topRuns.Average(w => w.DurationSeconds / w.DistanceKm))
            : 0L;

        var baseStates = Tiers.Select(tier =>
        {
            var isUnlocked = tier.Id == 1 || (overallAvgPace > 0 && overallAvgPace <= tier.UnlockPaceSecondsPerKm);
            return (Tier: tier, IsUnlocked: isUnlocked);
        }).ToList();

        var highestIdx = Math.Max(0, baseStates.FindLastIndex(t => t.IsUnlocked));
        var updatedStates = baseStates
            .Select((s, idx) => new ShoeTierStateDto(s.Tier, s.IsUnlocked, idx == highestIdx))
            .ToList();

        var currentTier = Tiers[highestIdx];
        var nextTier = highestIdx < Tiers.Count - 1 ? Tiers[highestIdx + 1] : null;

        float progressRatio;
        long secondsNeeded;

        if (nextTier != null && overallAvgPace > 0)
        {
            var targetPace = nextTier.UnlockPaceSecondsPerKm;
            var prevTargetPace = Math.Min(currentTier.UnlockPaceSecondsPerKm, 600L);
            var diffFromTarget = overallAvgPace - targetPace;
            if (diffFromTarget <= 0)
            {
                progressRatio = 1.0f;
                secondsNeeded = 0L;
            }
            else
            {
                var span = Math.Max(30L, prevTargetPace - targetPace);
                var done = Math.Max(0L, prevTargetPace - overallAvgPace);
                progressRatio = Math.Clamp((float)done / span, 0.05f, 0.95f);
                secondsNeeded = diffFromTarget;
            }
        }
        else if (nextTier == null)
        {
            progressRatio = 1.0f;
            secondsNeeded = 0L;
        }
        else
        {
            progressRatio = 0.0f;
            secondsNeeded = nextTier.UnlockPaceSecondsPerKm;
        }

        return new ShoeUnlockProgressDto(
            CurrentAvgPaceSec: overallAvgPace,
            TotalWorkoutsEvaluated: topRuns.Count,
            CurrentTier: currentTier,
            NextTier: nextTier,
            ProgressToNextTier: progressRatio,
            SecondsNeededToNextTier: secondsNeeded,
            Tiers: updatedStates
        );
    }
}
