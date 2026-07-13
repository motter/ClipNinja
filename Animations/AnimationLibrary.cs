namespace ClipNinjaV2.Animations;

/// <summary>
/// Library of pre-baked ninja animations. Each one is a Street Fighter
/// inspired move expressed as a sequence of poses. Times are in seconds;
/// angles in degrees; positions in canvas units.
/// </summary>
public static class AnimationLibrary
{
    private static readonly Random _rng = new();

    /// <summary>
    /// Per-move shout text — the line the ninja yells during the move.
    /// SF2-canonical where it makes sense ("HADOUKEN!" "SHORYUKEN!"
    /// "TATSUMAKI!"), upbeat onomatopoeia otherwise. Empty string = silent
    /// (IdleBob doesn't yell).
    /// </summary>
    private static readonly Dictionary<string, string> _shouts = new()
    {
        ["Kick"] = "KIAI!",
        ["Punch"] = "HYAH!",
        ["Hadouken"] = "HADOUKEN!",
        ["Shoryuken"] = "SHORYUKEN!",
        ["Backflip"] = "WHOOSH!",
        ["JumpKick"] = "TOBI GERI!",
        ["SpinKick"] = "MAWASHI!",
        ["LightningKick"] = "HYAKURETSU!",
        ["HelicopterKick"] = "TENSHO!",
        ["HurricaneKick"] = "TATSUMAKI!",
        ["HeroLanding"] = "TA-DAAA!",
        ["SpinningBirdKick"] = "SPINNING BIRD KICK!",
        ["IdleBob"] = "",
    };

    /// <summary>All available animations, by name.</summary>
    public static readonly IReadOnlyDictionary<string, NinjaAnimation> All = BuildAll();

    private static IReadOnlyDictionary<string, NinjaAnimation> BuildAll()
    {
        var src = new Dictionary<string, NinjaAnimation>
        {
            ["Kick"] = Kick(),
            ["Punch"] = Punch(),
            ["Hadouken"] = Hadouken(),
            ["Shoryuken"] = Shoryuken(),
            ["Backflip"] = Backflip(),
            ["JumpKick"] = JumpKick(),
            ["SpinKick"] = SpinKick(),
            ["LightningKick"] = LightningKick(),
            ["HelicopterKick"] = HelicopterKick(),
            ["HurricaneKick"] = HurricaneKick(),
            ["HeroLanding"] = HeroLanding(),
            ["SpinningBirdKick"] = SpinningBirdKick(),
            ["IdleBob"] = IdleBob(),
        };
        // Attach the shout text — done here rather than in each move function
        // so the move definitions stay focused on keyframes.
        var withShouts = new Dictionary<string, NinjaAnimation>();
        foreach (var (key, anim) in src)
        {
            withShouts[key] = new NinjaAnimation
            {
                Name = anim.Name,
                Poses = anim.Poses,
                Shout = _shouts.TryGetValue(key, out var s) ? s : "",
            };
        }
        return withShouts;
    }

    /// <summary>Pick a random animation, weighted toward the more visual ones.</summary>
    public static NinjaAnimation PickRandom()
    {
        var keys = new[]
        {
            "Kick", "Punch", "Hadouken", "Shoryuken", "Backflip",
            "JumpKick", "SpinKick", "LightningKick", "HelicopterKick",
            "HurricaneKick", "HeroLanding", "SpinningBirdKick"
        };
        return All[keys[_rng.Next(keys.Length)]];
    }

    // ── Move definitions ────────────────────────────────────────────────────

    /// <summary>
    /// Front kick — wind up high (knee back), then snap right leg forward & up.
    /// Right leg pivots at hip (CenterX=44, CenterY=68); leg extends DOWN at rest.
    /// POSITIVE rotation = clockwise = leg swings FORWARD-RIGHT (the kick direction).
    /// </summary>
    private static NinjaAnimation Kick() => new()
    {
        Name = "Kick",
        Poses = new()
        {
            new() { Time = 0.00 },
            // Wind up: bend knee back (NEGATIVE rotates leg backward)
            new() { Time = 0.10, RightLegRotate = -50, NinjaY = -2 },
            // SNAP: leg swings forward to nearly horizontal (positive 100° = forward-up)
            new() { Time = 0.22, RightLegRotate = 100, NinjaX = 8, NinjaY = -3 },
            // Hold dramatic kick pose
            new() { Time = 0.50, RightLegRotate = 100, NinjaX = 8, NinjaY = -3 },
            // Recover
            new() { Time = 0.85 },
        }
    };

    /// <summary>
    /// Straight punch — left arm thrusts forward.
    /// Left arm pivots at shoulder (CenterX=16, CenterY=42); arm hangs DOWN at rest.
    /// POSITIVE rotation = clockwise = arm swings FORWARD-RIGHT.
    /// We rotate to ~+95° so the arm is fully extended forward (horizontal).
    /// </summary>
    private static NinjaAnimation Punch() => new()
    {
        Name = "Punch",
        Poses = new()
        {
            new() { Time = 0.00 },
            // Wind back: pull arm back behind body (negative = counter-clockwise)
            new() { Time = 0.10, LeftArmRotate = -45, NinjaX = -3 },
            // STRIKE: arm swings forward to fully extended (positive ~100°)
            new() { Time = 0.22, LeftArmRotate = 100, NinjaX = 9 },
            // Hold extended punch pose
            new() { Time = 0.50, LeftArmRotate = 100, NinjaX = 9 },
            // Recover
            new() { Time = 0.80 },
        }
    };

    /// <summary>
    /// <summary>
    /// Hadouken — gather chi between hands then thrust forward.
    /// 
    /// Sequence:
    ///   0.00-0.10  neutral
    ///   0.10-0.50  hands cup together at chest, blue chi ball MATERIALIZES
    ///              and grows between them (tiny → small → medium)
    ///   0.50-0.65  THRUST: both arms push forward fast, ball is propelled
    ///   0.65-1.20  ball flies across stage, ninja holds fire pose
    ///   1.20-1.50  recover to neutral
    /// </summary>
    private static NinjaAnimation Hadouken() => new()
    {
        Name = "Hadouken",
        Poses = new()
        {
            new() { Time = 0.00 },

            // Phase 1: STANCE PREP. Ninja sinks low into a forward stance,
            // hands begin to come together LOW in front of body (hip-height).
            // Ball not yet visible — just hands cupping.
            new() { Time = 0.20, LeftArmRotate = 35, RightArmRotate = -35,
                    NinjaX = -4, NinjaY = 2,
                    FireballScale = 0.0, FireballOpacity = 0.0 },

            // Phase 2a: BALL APPEARS. Energy materializes between cupped
            // hands at hip-level (lower than chest — like the reference).
            // Tiny, dim spark to start.
            new() { Time = 0.35, LeftArmRotate = 42, RightArmRotate = -42,
                    NinjaX = -5, NinjaY = 3,
                    FireballX = 173, FireballY = 115, FireballScale = 0.18, FireballOpacity = 0.5 },

            // Phase 2b: BALL GROWS. Mid-charge — ball clearly visible,
            // glowing brighter. Ninja's body coils inward, hands cradle
            // the growing chi.
            new() { Time = 0.55, LeftArmRotate = 50, RightArmRotate = -50,
                    NinjaX = -6, NinjaY = 4,
                    FireballX = 173, FireballY = 115, FireballScale = 0.55, FireballOpacity = 0.9 },

            // Phase 2c: PEAK CHARGE. Ball at FULL SIZE between hands.
            // Held briefly so the user can SEE the gathered energy.
            new() { Time = 0.75, LeftArmRotate = 52, RightArmRotate = -52,
                    NinjaX = -6, NinjaY = 4,
                    FireballX = 173, FireballY = 115, FireballScale = 1.0, FireballOpacity = 1.0 },

            // Phase 3a: THE THRUST. Body uncoils forward explosively. Arms
            // straighten forward (positive = forward for left, negative for
            // right). Ball launches from the hand-meeting point.
            new() { Time = 0.88, LeftArmRotate = 95, RightArmRotate = -95,
                    NinjaX = 8, NinjaY = -1,
                    FireballX = 215, FireballY = 105, FireballScale = 1.1, FireballOpacity = 1.0 },

            // Phase 3b: PROJECTILE TRAVELS. Ninja holds the extended pose
            // while the ball flies across the stage.
            new() { Time = 1.15, LeftArmRotate = 95, RightArmRotate = -95,
                    NinjaX = 8, NinjaY = -1,
                    FireballX = 295, FireballY = 102, FireballScale = 1.2, FireballOpacity = 1.0 },

            // Phase 3c: BALL EXITS. Fades out past the right edge.
            new() { Time = 1.45, LeftArmRotate = 95, RightArmRotate = -95,
                    NinjaX = 8, NinjaY = -1,
                    FireballX = 385, FireballY = 100, FireballScale = 1.3, FireballOpacity = 0.0 },

            // Recover to neutral
            new() { Time = 1.80 },
        }
    };

    /// <summary>
    /// Shoryuken (rising dragon punch) — leap up while uppercutting.
    /// Left arm punches up: rotation goes from rest (down) to UP-FORWARD.
    /// </summary>
    private static NinjaAnimation Shoryuken() => new()
    {
        Name = "Shoryuken",
        Poses = new()
        {
            new() { Time = 0.00 },
            new() { Time = 0.10, NinjaY = 3, LeftArmRotate = -20 },                                  // crouch + arm cocks back
            new() { Time = 0.30, NinjaY = -28, LeftArmRotate = 145, NinjaRotate = -10, NinjaX = 4 }, // launch + uppercut
            new() { Time = 0.55, NinjaY = -32, LeftArmRotate = 145, NinjaRotate = -10, NinjaX = 4 }, // peak
            new() { Time = 0.80, NinjaY = -10, LeftArmRotate = 90, NinjaRotate = -3, NinjaX = 2 },   // descending
            new() { Time = 1.05, NinjaY = 0, NinjaRotate = 0 },                                       // land
        }
    };

    /// <summary>Full backflip — rotate ninja 360° backward.</summary>
    private static NinjaAnimation Backflip() => new()
    {
        Name = "Backflip",
        Poses = new()
        {
            new() { Time = 0.00 },
            new() { Time = 0.10, NinjaY = 3 },
            new() { Time = 0.30, NinjaY = -25, NinjaX = -10, NinjaRotate = -180 },
            new() { Time = 0.55, NinjaY = -25, NinjaX = -20, NinjaRotate = -360 },
            new() { Time = 0.80, NinjaY = 0, NinjaX = -25, NinjaRotate = -360 },
            new() { Time = 1.05, NinjaX = -25 },
        }
    };

    /// <summary>Jump kick — leap up while extending right leg in a forward kick.</summary>
    private static NinjaAnimation JumpKick() => new()
    {
        Name = "JumpKick",
        Poses = new()
        {
            new() { Time = 0.00 },
            new() { Time = 0.10, NinjaY = 4 },                                                       // crouch
            new() { Time = 0.30, NinjaY = -22, NinjaX = 5, RightLegRotate = 110, LeftArmRotate = 60 }, // airborne with kick + arm forward
            new() { Time = 0.55, NinjaY = -22, NinjaX = 12, RightLegRotate = 110, LeftArmRotate = 60 }, // hold
            new() { Time = 0.85, NinjaY = 0, NinjaX = 12 },                                           // land
        }
    };

    /// <summary>Spinning bird kick — rotate while right leg sticks out.</summary>
    private static NinjaAnimation SpinKick() => new()
    {
        Name = "SpinKick",
        Poses = new()
        {
            new() { Time = 0.00 },
            new() { Time = 0.20, NinjaY = -10, RightLegRotate = 95, NinjaRotate = 180 },
            new() { Time = 0.40, NinjaY = -10, RightLegRotate = 95, NinjaRotate = 360 },
            new() { Time = 0.60, NinjaY = -10, RightLegRotate = 95, NinjaRotate = 540 },
            new() { Time = 0.85, NinjaY = 0, RightLegRotate = 0, NinjaRotate = 720 },
        }
    };

    /// <summary>Subtle idle bob — slight up/down breathing motion. Used for ambient life.</summary>
    /// <summary>
    /// Lightning kick (Chun-Li's Hyakuretsukyaku) — rapid-fire leg flurries.
    /// The right leg snaps out 5 times in quick succession with no recovery
    /// between hits, then a final hold pose.
    /// </summary>
    private static NinjaAnimation LightningKick() => new()
    {
        Name = "LightningKick",
        Poses = new()
        {
            new() { Time = 0.00 },
            // Flurry of 5 alternating retract/extend kicks (~0.12s each)
            new() { Time = 0.06, RightLegRotate = 95, NinjaX = 4, NinjaY = -1 },   // kick 1 out
            new() { Time = 0.12, RightLegRotate = -10, NinjaX = 3 },                // pull back
            new() { Time = 0.18, RightLegRotate = 100, NinjaX = 5, NinjaY = -1 },  // kick 2 out
            new() { Time = 0.24, RightLegRotate = -10, NinjaX = 4 },
            new() { Time = 0.30, RightLegRotate = 95, NinjaX = 6, NinjaY = -1 },   // kick 3
            new() { Time = 0.36, RightLegRotate = -10, NinjaX = 5 },
            new() { Time = 0.42, RightLegRotate = 100, NinjaX = 7, NinjaY = -1 },  // kick 4
            new() { Time = 0.48, RightLegRotate = -10, NinjaX = 6 },
            new() { Time = 0.54, RightLegRotate = 105, NinjaX = 8, NinjaY = -2 },  // kick 5 (final, biggest)
            // Hold dramatic finishing pose
            new() { Time = 0.85, RightLegRotate = 105, NinjaX = 8, NinjaY = -2 },
            new() { Time = 1.10 },
        }
    };

    /// <summary>
    /// Helicopter kick (Tenshokyaku) — rising spin with leg out, like
    /// Chun-Li's flip kick. Ninja goes UP while rotating; leg sticks out.
    /// </summary>
    private static NinjaAnimation HelicopterKick() => new()
    {
        Name = "HelicopterKick",
        Poses = new()
        {
            new() { Time = 0.00 },
            new() { Time = 0.10, NinjaY = 4, RightLegRotate = -25 },                        // crouch + load
            // Launch up, leg sticks out, rotate counter-clockwise
            new() { Time = 0.30, NinjaY = -20, NinjaRotate = -180, RightLegRotate = 90 },
            new() { Time = 0.50, NinjaY = -28, NinjaRotate = -360, RightLegRotate = 90 },   // peak
            new() { Time = 0.70, NinjaY = -20, NinjaRotate = -540, RightLegRotate = 90 },   // descending
            new() { Time = 0.90, NinjaY = 0, NinjaRotate = -720, RightLegRotate = 0 },      // land facing forward
        }
    };

    /// <summary>
    /// Hurricane kick (Tatsumaki Senpukyaku) — horizontal spinning attack
    /// where the ninja TRAVELS forward while spinning with leg extended.
    /// </summary>
    private static NinjaAnimation HurricaneKick() => new()
    {
        Name = "HurricaneKick",
        Poses = new()
        {
            new() { Time = 0.00 },
            new() { Time = 0.10, NinjaY = 3 },                                                          // crouch
            // Take off forward, start spinning, leg out
            new() { Time = 0.25, NinjaX = 5, NinjaY = -10, NinjaRotate = 360, RightLegRotate = 90 },
            new() { Time = 0.45, NinjaX = 12, NinjaY = -10, NinjaRotate = 720, RightLegRotate = 90 },
            new() { Time = 0.65, NinjaX = 18, NinjaY = -8, NinjaRotate = 1080, RightLegRotate = 90 },
            new() { Time = 0.85, NinjaX = 22, NinjaY = 0, NinjaRotate = 1080, RightLegRotate = 0 },     // land
        }
    };

    /// <summary>
    /// Hero landing — leap up, then slam down into a dramatic pose with
    /// arms wide and legs spread. Like a superhero arrival.
    /// </summary>
    private static NinjaAnimation HeroLanding() => new()
    {
        Name = "HeroLanding",
        Poses = new()
        {
            new() { Time = 0.00 },
            // Crouch + arm load
            new() { Time = 0.10, NinjaY = 4, LeftArmRotate = -20, RightArmRotate = 20 },
            // Soar up high
            new() { Time = 0.40, NinjaY = -35, LeftArmRotate = 30, RightArmRotate = -30 },
            new() { Time = 0.60, NinjaY = -35 },
            // Slam down (faster than rise)
            new() { Time = 0.78, NinjaY = 5, NinjaScaleY = 0.9 },
            // Hero pose: arms wide, legs spread, body slightly squished
            new() { Time = 0.85, NinjaY = 2, LeftArmRotate = -55, RightArmRotate = 55,
                    LeftLegRotate = -15, RightLegRotate = 15, NinjaScaleY = 0.95 },
            // Hold the pose
            new() { Time = 1.20, NinjaY = 0, LeftArmRotate = -55, RightArmRotate = 55,
                    LeftLegRotate = -15, RightLegRotate = 15 },
            // Recover
            new() { Time = 1.55 },
        }
    };

    /// <summary>
    /// Spinning Bird Kick (Chun-Li's Hazan Tenshokyaku) — the iconic
    /// upside-down spinning kick. Crouches deep, leaps up, inverts body,
    /// spins horizontally with both legs spread wide like helicopter blades,
    /// then drops and recovers.
    ///
    /// Phases:
    ///   0.00-0.18  Deep crouch (knees bent — both legs rotate outward,
    ///              body sinks low and squashes)
    ///   0.18-0.30  Explosive leap up (legs straighten, body rises fast)
    ///   0.30-0.95  Inverted spin: NinjaRotate sweeps from 0→1080° (3 full
    ///              rotations) while legs are spread wide horizontal. The
    ///              ninja is upside-down during the middle portion.
    ///   0.95-1.15  Land: body re-rights, legs come back together
    ///   1.15-1.45  Final stance, then recover
    /// </summary>
    private static NinjaAnimation SpinningBirdKick() => new()
    {
        Name = "SpinningBirdKick",
        Poses = new()
        {
            new() { Time = 0.00 },

            // Deep crouch — both legs rotate outward like she's loading the
            // spring. Body sinks low and squashes vertically. Arms come up
            // and out for balance/charge pose.
            new() { Time = 0.10, NinjaY = 4, NinjaScaleY = 0.85,
                    LeftLegRotate = -25, RightLegRotate = 25,
                    LeftArmRotate = -60, RightArmRotate = 60 },

            // Full crouch — bottomed out, about to spring
            new() { Time = 0.18, NinjaY = 6, NinjaScaleY = 0.80,
                    LeftLegRotate = -30, RightLegRotate = 30,
                    LeftArmRotate = -75, RightArmRotate = 75 },

            // LEAP — body rises fast, legs start spreading wide
            new() { Time = 0.30, NinjaY = -14, NinjaScaleY = 1.05,
                    LeftLegRotate = -50, RightLegRotate = 50,
                    LeftArmRotate = -90, RightArmRotate = 90,
                    NinjaRotate = 90 },

            // PEAK INVERSION — body upside-down (180°), legs spread fully
            // horizontal (left at -90, right at +90 = both pointing outward
            // perpendicular to body). Arms tucked in toward torso.
            new() { Time = 0.50, NinjaY = -18, NinjaScaleY = 1.0,
                    LeftLegRotate = -90, RightLegRotate = 90,
                    LeftArmRotate = -110, RightArmRotate = 110,
                    NinjaRotate = 360 },

            // SECOND ROTATION — still inverted, legs still wide, spinning
            new() { Time = 0.70, NinjaY = -16, NinjaScaleY = 1.0,
                    LeftLegRotate = -90, RightLegRotate = 90,
                    LeftArmRotate = -110, RightArmRotate = 110,
                    NinjaRotate = 720 },

            // THIRD ROTATION — coming down, body still horizontal
            new() { Time = 0.90, NinjaY = -8, NinjaScaleY = 1.0,
                    LeftLegRotate = -70, RightLegRotate = 70,
                    LeftArmRotate = -95, RightArmRotate = 95,
                    NinjaRotate = 1080 },

            // LAND — back upright (full 1080° = 3 rotations clean), legs
            // come back together, slight knee bend for shock absorption
            new() { Time = 1.05, NinjaY = 2, NinjaScaleY = 0.92,
                    LeftLegRotate = -10, RightLegRotate = 10,
                    LeftArmRotate = -25, RightArmRotate = 25,
                    NinjaRotate = 1080 },

            // Recovery to neutral — note we KEEP NinjaRotate=1080 so it
            // doesn't unwind backward, looks like a clean 3-rotation finish
            new() { Time = 1.30, NinjaRotate = 1080 },
            new() { Time = 1.55, NinjaRotate = 1080 },
        }
    };

    private static NinjaAnimation IdleBob() => new()
    {
        Name = "IdleBob",
        Poses = new()
        {
            new() { Time = 0.0 },
            new() { Time = 1.0, NinjaY = -1 },
            new() { Time = 2.0 },
        }
    };
}
