using System.Windows.Media;
using System.Windows.Threading;

namespace ClipNinjaV2.Animations;

/// <summary>
/// Pose snapshot at one instant in time. Field naming matches the
/// transform names in MainWindow.xaml so we can map directly.
/// All angles in degrees; translations in canvas units.
/// </summary>
public class Pose
{
    public double Time { get; set; }              // seconds from start of animation
    public double NinjaX { get; set; } = 0;
    public double NinjaY { get; set; } = 0;
    public double NinjaRotate { get; set; } = 0;
    public double NinjaScaleX { get; set; } = 1;
    public double NinjaScaleY { get; set; } = 1;
    public double LeftArmRotate { get; set; } = 0;
    public double RightArmRotate { get; set; } = 0;
    public double LeftLegRotate { get; set; } = 0;
    public double RightLegRotate { get; set; } = 0;
    public double HeadRotate { get; set; } = 0;

    /// <summary>
    /// Fireball position relative to the stage canvas (340x160). Set
    /// FireballOpacity > 0 to make it visible. Used for Hadouken.
    /// </summary>
    public double FireballX { get; set; } = 0;
    public double FireballY { get; set; } = 0;
    public double FireballScale { get; set; } = 1;
    public double FireballOpacity { get; set; } = 0;
}

/// <summary>A named animation = sequence of poses interpolated over time.</summary>
public class NinjaAnimation
{
    public string Name { get; init; } = "";
    public required List<Pose> Poses { get; init; }
    public double Duration => Poses.Count > 0 ? Poses[^1].Time : 0;
}

/// <summary>
/// Drives the ninja's named transforms over time using a DispatcherTimer
/// at 30fps (sufficient for the kind of motion we want, low CPU cost).
/// Uses linear interpolation between poses.
/// </summary>
public class NinjaAnimator
{
    private readonly TranslateTransform _translate;
    private readonly RotateTransform _rotate;
    private readonly ScaleTransform _scale;
    private readonly RotateTransform _leftArm;
    private readonly RotateTransform _rightArm;
    private readonly RotateTransform _leftLeg;
    private readonly RotateTransform _rightLeg;
    private readonly RotateTransform _head;
    private readonly TranslateTransform _fireballTranslate;
    private readonly ScaleTransform _fireballScale;
    private readonly System.Windows.UIElement _fireball;

    private readonly DispatcherTimer _timer;
    private NinjaAnimation? _current;
    private DateTime _startTime;

    public bool IsPlaying => _current is not null;

    /// <summary>Fires when an animation completes.</summary>
    public event EventHandler? AnimationCompleted;

    public NinjaAnimator(
        TranslateTransform translate, RotateTransform rotate, ScaleTransform scale,
        RotateTransform leftArm, RotateTransform rightArm,
        RotateTransform leftLeg, RotateTransform rightLeg,
        RotateTransform head,
        TranslateTransform fireballTranslate, ScaleTransform fireballScale,
        System.Windows.UIElement fireball)
    {
        _translate = translate;
        _rotate = rotate;
        _scale = scale;
        _leftArm = leftArm;
        _rightArm = rightArm;
        _leftLeg = leftLeg;
        _rightLeg = rightLeg;
        _head = head;
        _fireballTranslate = fireballTranslate;
        _fireballScale = fireballScale;
        _fireball = fireball;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(42),   // ~24 fps — saves CPU vs 30fps, visually equivalent
        };
        _timer.Tick += OnTick;
    }

    /// <summary>Start playing the given animation. Cancels any in-flight one.</summary>
    public void Play(NinjaAnimation anim)
    {
        _current = anim;
        _startTime = DateTime.UtcNow;
        _timer.Start();
    }

    /// <summary>Stop and return ninja to neutral pose immediately.</summary>
    public void Stop()
    {
        _timer.Stop();
        _current = null;
        ApplyPose(NeutralPose);
    }

    /// <summary>Default pose (everything at zero — sword arm at side).</summary>
    public static Pose NeutralPose => new();

    private void OnTick(object? sender, EventArgs e)
    {
        if (_current is null) { _timer.Stop(); return; }
        double t = (DateTime.UtcNow - _startTime).TotalSeconds;
        if (t >= _current.Duration)
        {
            // Snap to the last pose, then return to idle
            ApplyPose(_current.Poses[^1]);
            _timer.Stop();
            var done = _current;
            _current = null;
            // Smoothly settle to neutral
            ApplyPose(NeutralPose);
            AnimationCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }
        ApplyPose(InterpolateAt(_current, t));
    }

    /// <summary>Find the two poses surrounding time t and lerp between them.</summary>
    private static Pose InterpolateAt(NinjaAnimation anim, double t)
    {
        var poses = anim.Poses;
        if (poses.Count == 0) return NeutralPose;
        if (t <= poses[0].Time) return poses[0];
        for (int i = 1; i < poses.Count; i++)
        {
            if (t <= poses[i].Time)
            {
                var a = poses[i - 1];
                var b = poses[i];
                double dt = b.Time - a.Time;
                double k = dt > 0 ? (t - a.Time) / dt : 0;
                return Lerp(a, b, k);
            }
        }
        return poses[^1];
    }

    private static Pose Lerp(Pose a, Pose b, double k)
    {
        double L(double x, double y) => x + (y - x) * k;
        return new Pose
        {
            NinjaX = L(a.NinjaX, b.NinjaX),
            NinjaY = L(a.NinjaY, b.NinjaY),
            NinjaRotate = L(a.NinjaRotate, b.NinjaRotate),
            NinjaScaleX = L(a.NinjaScaleX, b.NinjaScaleX),
            NinjaScaleY = L(a.NinjaScaleY, b.NinjaScaleY),
            LeftArmRotate = L(a.LeftArmRotate, b.LeftArmRotate),
            RightArmRotate = L(a.RightArmRotate, b.RightArmRotate),
            LeftLegRotate = L(a.LeftLegRotate, b.LeftLegRotate),
            RightLegRotate = L(a.RightLegRotate, b.RightLegRotate),
            HeadRotate = L(a.HeadRotate, b.HeadRotate),
            FireballX = L(a.FireballX, b.FireballX),
            FireballY = L(a.FireballY, b.FireballY),
            FireballScale = L(a.FireballScale, b.FireballScale),
            FireballOpacity = L(a.FireballOpacity, b.FireballOpacity),
        };
    }

    private void ApplyPose(Pose p)
    {
        _translate.X = p.NinjaX;
        _translate.Y = p.NinjaY;
        _rotate.Angle = p.NinjaRotate;
        _scale.ScaleX = p.NinjaScaleX;
        _scale.ScaleY = p.NinjaScaleY;
        _leftArm.Angle = p.LeftArmRotate;
        _rightArm.Angle = p.RightArmRotate;
        _leftLeg.Angle = p.LeftLegRotate;
        _rightLeg.Angle = p.RightLegRotate;
        _head.Angle = p.HeadRotate;
        _fireballTranslate.X = p.FireballX;
        _fireballTranslate.Y = p.FireballY;
        _fireballScale.ScaleX = p.FireballScale;
        _fireballScale.ScaleY = p.FireballScale;
        _fireball.Opacity = p.FireballOpacity;
    }
}
