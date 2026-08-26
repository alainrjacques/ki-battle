using Godot;

namespace KiBattle;

/// <summary>
/// The collision point of the two beams: shockwave shader quad, spark particles,
/// and a flickering light. Positioned and fed by BattleManager each frame.
/// </summary>
public partial class ClashPoint : Node2D
{
    private ShaderMaterial _mat = null!;
    private GpuParticles2D[] _sparksL = null!, _sparksR = null!; // [up fan, down fan]
    private PointLight2D _light = null!;
    private float _time;

    private static readonly StringName SnColorA = "color_a", SnColorB = "color_b",
        SnIntensity = "intensity", SnRatio = "ratio";

    public override void _Ready()
    {
        // The headless dummy renderer crashes intermittently on GPU resources; skip entirely.
        if (DisplayServer.GetName() == "headless") { SetProcess(false); return; }

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/clash.gdshader") };

        var quad = new ColorRect
        {
            Material = _mat,
            OffsetLeft = -340, OffsetTop = -340, OffsetRight = 340, OffsetBottom = 340,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(quad);

        // Per side: an up fan and a down fan (the splash spreads both ways along
        // the interface); the dominating side sprays harder.
        _sparksL = new[] { MakeSparks(+1f, -1f, 90), MakeSparks(+1f, +1f, 60) };
        _sparksR = new[] { MakeSparks(-1f, -1f, 90), MakeSparks(-1f, +1f, 60) };
        foreach (var s in _sparksL) AddChild(s);
        foreach (var s in _sparksR) AddChild(s);

        _light = new PointLight2D
        {
            Texture = MakeLightTexture(),
            TextureScale = 3.5f,
            Energy = 0f,
        };
        AddChild(_light);
        Visible = false;
    }

    /// <summary>
    /// dirX marks which side's beam feeds this spray. Like a pressure jet hitting
    /// a plate, the material deflects at the interface: a fast fan roughly
    /// perpendicular to the beam, leaning past the clash toward the loser,
    /// arcing over under heavy gravity.
    /// </summary>
    private static GpuParticles2D MakeSparks(float dirX, float dirY, int amount) => new()
    {
        Amount = amount,
        Lifetime = 0.5,
        Texture = MakeStreakTexture(),
        ProcessMaterial = new ParticleProcessMaterial
        {
            Direction = new Vector3(dirX * 0.35f, dirY, 0),
            Spread = 50f,
            InitialVelocityMin = 520f,
            InitialVelocityMax = 1250f,
            Gravity = new Vector3(0, 950, 0),
            ScaleMin = 0.7f,
            ScaleMax = 1.9f,
            ParticleFlagAlignY = true, // streak stretches along its velocity
        },
    };

    private static Texture2D MakeStreakTexture()
    {
        var img = Image.CreateEmpty(4, 18, false, Image.Format.Rgba8);
        img.Fill(Colors.White);
        return ImageTexture.CreateFromImage(img);
    }

    private static Texture2D MakeLightTexture()
    {
        var grad = new Gradient();
        grad.SetColor(0, Colors.White);
        grad.SetColor(1, new Color(1, 1, 1, 0));
        return new GradientTexture2D
        {
            Gradient = grad,
            Width = 256, Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0f),
        };
    }

    /// <summary>intensity 0..~3 combined beam power; ratio 0 = left dominating, 1 = right.</summary>
    public void UpdateClash(Color leftGlow, Color rightGlow, float intensity, float ratio, float delta)
    {
        _time += delta;
        Visible = intensity > 0.03f;
        foreach (var s in _sparksL) s.Emitting = intensity > 0.25f;
        foreach (var s in _sparksR) s.Emitting = intensity > 0.25f;
        if (!Visible) return;

        _mat.SetShaderParameter(SnColorA, leftGlow);
        _mat.SetShaderParameter(SnColorB, rightGlow);
        _mat.SetShaderParameter(SnIntensity, intensity);
        _mat.SetShaderParameter(SnRatio, ratio);

        // Winner's color dominates the spray; loser still spits a few sparks.
        float leftDom = 1f - ratio;
        foreach (var s in _sparksL)
        {
            s.AmountRatio = 0.15f + 0.85f * Mathf.Pow(leftDom, 1.5f);
            s.Modulate = new Color(leftGlow.R * 1.9f, leftGlow.G * 1.9f, leftGlow.B * 1.9f);
        }
        foreach (var s in _sparksR)
        {
            s.AmountRatio = 0.15f + 0.85f * Mathf.Pow(ratio, 1.5f);
            s.Modulate = new Color(rightGlow.R * 1.9f, rightGlow.G * 1.9f, rightGlow.B * 1.9f);
        }

        _light.Color = leftGlow.Lerp(rightGlow, ratio);
        _light.Energy = intensity * (0.9f + 0.25f * Mathf.Sin(_time * 37f));
    }
}
