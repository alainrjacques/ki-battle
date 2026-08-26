using Godot;

namespace KiBattle;

/// <summary>
/// Silhouette wizard: dark robed Polygon2D with an element-colored aura copy behind it,
/// plus muzzle particles at the hands while channeling. Faces +X; the enemy side is mirrored.
/// </summary>
public partial class CasterVisual : Node2D
{
    private Caster _caster = null!;
    private Polygon2D _aura = null!;
    private Polygon2D _body = null!;
    private GpuParticles2D _muzzle = null!;
    private float _dissolve; // KO fade

    // Hooded figure, feet at (0,0), hands thrust forward at ~(-60..-110) height.
    private static readonly Vector2[] Silhouette =
    {
        new(-30, 0), new(-24, -45), new(-20, -85), new(-16, -118), new(-14, -132),
        new(-24, -140), new(-26, -154), new(-16, -166), new(-2, -170), new(10, -162),
        new(12, -150), new(8, -140), new(16, -124), new(58, -112), new(62, -103),
        new(60, -94), new(14, -96), new(18, -60), new(26, -28), new(30, 0),
    };

    public override void _Ready()
    {
        // The headless dummy renderer crashes intermittently on GPU resources; skip entirely.
        if (DisplayServer.GetName() == "headless") { SetProcess(false); return; }

        _caster = GetParent<Caster>();

        _aura = new Polygon2D
        {
            Polygon = Silhouette,
            Color = new Color(0.5f, 0.7f, 1f, 0.5f),
            Scale = new Vector2(1.10f, 1.06f),
            Position = new Vector2(-2f, 4f),
        };
        AddChild(_aura);

        _body = new Polygon2D
        {
            Polygon = Silhouette,
            Color = new Color(0.04f, 0.03f, 0.08f),
        };
        AddChild(_body);

        _muzzle = new GpuParticles2D
        {
            Position = MuzzleLocal,
            Amount = 24,
            Lifetime = 0.35,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Spread = 180f,
                InitialVelocityMin = 30f,
                InitialVelocityMax = 120f,
                Gravity = Vector3.Zero,
                ScaleMin = 1.5f,
                ScaleMax = 3.5f,
            },
            Emitting = false,
        };
        AddChild(_muzzle);
    }

    public static Vector2 MuzzleLocal => new(64, -103);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var glow = _caster.Style.Glow;

        float channel = _caster.IsExhausted ? 0.05f : _caster.Tier switch
        {
            PowerTier.Overdrive => 1f,
            PowerTier.Empowered => 0.65f,
            _ => 0.3f,
        };

        // HDR aura: bright enough to bloom when channeling hard.
        var target = new Color(glow.R * (0.4f + channel * 1.2f), glow.G * (0.4f + channel * 1.2f),
                               glow.B * (0.4f + channel * 1.2f), 0.35f + channel * 0.4f);
        _aura.Color = _aura.Color.Lerp(target, 8f * dt);

        _muzzle.Emitting = !_caster.IsExhausted;
        _muzzle.Modulate = glow;

        if (_dissolve > 0f)
        {
            _dissolve = Mathf.Min(1f, _dissolve + dt * 1.4f);
            var faded = new Color(1f, 1f, 1f, 1f - _dissolve);
            _body.Modulate = faded;
            _aura.Modulate = faded;
        }
    }

    /// <summary>KO: flash white, then fade out.</summary>
    public void StartDissolve()
    {
        if (_dissolve > 0f) return;
        _dissolve = 0.01f;
        _body.Color = new Color(3f, 3f, 3f); // HDR white flash before the fade
    }
}
