using StrideApex.Api.Models;

namespace StrideApex.Api.Services;

/// <summary>
/// Port of Android BiomechanicsCatalog containing 5 gait & running form drill modules.
/// </summary>
public sealed class BiomechanicsCatalogService
{
    public IReadOnlyList<BiomechanicsModuleDto> Modules { get; } =
    [
        new(
            Id: "midfoot_strike",
            Title: "Midfoot Strike & Tibial Alignment",
            Subtitle: "Eliminate heel-striking braking forces by landing directly beneath your center of mass.",
            FocusMetricLabel: "Tibial Inclination at Contact",
            OptimalTargetRange: "0° to +4° (Vertical Shin)",
            CommonFault: "Overstriding with extended knee (+14° tibial angle), creating high eccentric braking impulse.",
            BiomechanicalCue: "Imagine pedaling a bicycle backward: paw the track down and back so your foot touches down directly under your hips.",
            Drills:
            [
                "2 x 30m Barefoot Grass Cadence Paws",
                "3 x 40m A-Skip to Running Transition",
                "Metronome 182 SPM Short-Lever Strides"
            ],
            TargetKneeFlexionDeg: 162,
            TargetTibialAngleDeg: 2,
            TargetTrunkLeanDeg: 8,
            TargetCadenceSpm: 182
        ),
        new(
            Id: "cadence_recoil",
            Title: "180+ SPM Cadence & Achilles Recoil",
            Subtitle: "Shorten ground contact time (<210ms) to harness free elastic energy from the Achilles tendon.",
            FocusMetricLabel: "Ground Contact Time (GCT)",
            OptimalTargetRange: "175ms - 205ms @ 180-188 SPM",
            CommonFault: "Low cadence (<166 SPM) with bounding vertical oscillation (>10.5 cm) and energy dissipation.",
            BiomechanicalCue: "Run on hot coals: focus on rapid, light ankle stiffness and instant heel lift right after mid-stance.",
            Drills:
            [
                "3 x 60s In-Place Ankling Hops (Stiff Ankle)",
                "4 x 100m +5% Cadence Metronome Pickups",
                "2 x 20 Single-Leg Pogo Jumps"
            ],
            TargetKneeFlexionDeg: 158,
            TargetTibialAngleDeg: 3,
            TargetTrunkLeanDeg: 7,
            TargetCadenceSpm: 186
        ),
        new(
            Id: "forward_lean",
            Title: "Ankle-Driven Forward Lean",
            Subtitle: "Use gravity for horizontal propulsion by leaning from the ankles rather than collapsing at the waist.",
            FocusMetricLabel: "Whole-Body Forward Tilt",
            OptimalTargetRange: "6° to 9° from Ankle Axis",
            CommonFault: "Hinging at the lumbar spine ('sitting in the bucket'), restricting diaphragm expansion and hip extension.",
            BiomechanicalCue: "Stand tall with a string pulling the crown of your head skyward, then press your hips slightly forward until gravity initiates your stride.",
            Drills:
            [
                "4 x Falling Starts from Standing Tall",
                "3 x 50m Wall-Lean March & Acceleration",
                "Thoracic Tall-Posture Float Strides"
            ],
            TargetKneeFlexionDeg: 160,
            TargetTibialAngleDeg: 1,
            TargetTrunkLeanDeg: 9,
            TargetCadenceSpm: 180
        ),
        new(
            Id: "pelvic_neutrality",
            Title: "Pelvic Neutrality & Triple Extension",
            Subtitle: "Lock in core stability to transfer 100% of glute and hamstring torque into forward velocity.",
            FocusMetricLabel: "Peak Hip Extension Angle",
            OptimalTargetRange: "18° to 24° Behind Vertical",
            CommonFault: "Anterior pelvic tilt (arched lower back), cutting off trailing-leg hip extension and overloading hip flexors.",
            BiomechanicalCue: "Level your belt buckle like a bowl of water, squeeze your glute at toe-off to drive the ground behind you.",
            Drills:
            [
                "2 x 15 Glute Bridge Marching Holds",
                "3 x 40m High-Knee B-Skips with Hip Lock",
                "4 x 60m Hill Bounds for Triple Extension"
            ],
            TargetKneeFlexionDeg: 165,
            TargetTibialAngleDeg: 2,
            TargetTrunkLeanDeg: 8,
            TargetCadenceSpm: 182
        ),
        new(
            Id: "compact_arm_swing",
            Title: "Compact Arm Swing & Counter-Rotation",
            Subtitle: "Balance lower-body angular momentum with compact 85° elbow drive back toward the hip pocket.",
            FocusMetricLabel: "Elbow Flexion & Drive Arc",
            OptimalTargetRange: "80°-90° Flexion (Zero Midline Cross)",
            CommonFault: "Arms crossing the sternum midline, inducing torso torque and lateral side-to-side energy waste.",
            BiomechanicalCue: "Brush your thumbs lightly past your hip pockets on the backswing, hands relaxed as if holding a potato chip without crushing it.",
            Drills:
            [
                "3 x 45s Seated Rapid Arm-Drive Drill",
                "4 x 80m Relaxed-Jaw & Dropped-Shoulder Strides",
                "2 x 30m Band-Resisted Elbow Back-Drive"
            ],
            TargetKneeFlexionDeg: 161,
            TargetTibialAngleDeg: 2,
            TargetTrunkLeanDeg: 8,
            TargetCadenceSpm: 184
        )
    ];
}
