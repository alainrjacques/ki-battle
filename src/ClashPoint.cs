using Godot;

namespace KiBattle;

/// <summary>
/// The collision point of the two beams: shockwave shader quad, spark particles,
/// and a flickering light. Positioned and fed by BattleManager each frame.
/// </summary>
public partial class ClashPoint : Node2D
{
    private ShaderMaterial _mat = null!;
    private GpuParticles2D _sparks = null!;
    private PointLight2D _light = null!;
    private float _time;

    public override void _Ready()
    {
        // The headless dummy renderer crashes intermittently on GPU resources; skip entirely.
        if (DisplayServer.GetName() == "headless") { SetProcess(false); return; }

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/clash.gdshader") };

        var quad = new ColorRect
        {
            Material = _mat,
            OffsetLeft = -280, OffsetTop = -280, OffsetRight = 280, OffsetBottom = 280,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(quad);

        _sparks = new GpuParticles2D
        {
            Amount = 48,
            Lifetime = 0.5,
            Explosiveness = 0.1f,
            ProcessMaterial = MakeSparkMaterial(),
        };
        AddChild(_sparks);

        _light = new PointLight2D
        {
            Texture = MakeLightTexture(),
            TextureScale = 3.5f,
            Energy = 0f,
        };
        AddChild(_light);
        Visible = false;
    }

    private static ParticleProcessMaterial MakeSparkMaterial() => new()
    {
        Direction = new Vector3(0, -1, 0),
        Spread = 180f,
        InitialVelocityMin = 250f,
        InitialVelocityMax = 700f,
        Gravity = new Vector3(0, 350, 0),
        ScaleMin = 2.0f,
        ScaleMax = 5.0f,
        Color = Colors.White,
    };

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
        _sparks.Emitting = intensity > 0.4f;
        if (!Visible) return;

        _mat.SetShaderParameter("color_a", leftGlow);
        _mat.SetShaderParameter("color_b", rightGlow);
        _mat.SetShaderParameter("intensity", intensity);
        _mat.SetShaderParameter("ratio", ratio);

        _sparks.Modulate = leftGlow.Lerp(rightGlow, ratio);
        _light.Color = leftGlow.Lerp(rightGlow, 0.5f);
        _light.Energy = intensity * (0.9f + 0.25f * Mathf.Sin(_time * 37f));
    }
}
