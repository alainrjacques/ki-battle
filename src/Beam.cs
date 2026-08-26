using Godot;

namespace KiBattle;

/// <summary>
/// One caster's beam: a 2-point Line2D stretched from muzzle to clash point,
/// with the beam shader doing all the visual work. Updated every frame by BattleManager.
/// </summary>
public partial class Beam : Node2D
{
    private Line2D _line = null!;
    private ShaderMaterial _mat = null!;
    private float _displayIntensity;
    private int _appliedStyle = -1;
    private BeamType _appliedType = (BeamType)(-1);

    // Cached uniform names: a string literal here would marshal to a new StringName every call.
    private static readonly StringName SnCore = "core_color", SnGlow = "glow_color", SnStyle = "style",
        SnType = "beam_type", SnIntensity = "intensity", SnNoise = "noise_scale", SnScroll = "scroll_speed",
        SnWobble = "wobble_amp", SnLen = "beam_len", SnSeed = "seed";

    public override void _Ready()
    {
        // The headless dummy renderer crashes intermittently on GPU resources; skip entirely.
        if (DisplayServer.GetName() == "headless") { SetProcess(false); return; }

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/beam.gdshader") };

        // Line2D stretch UVs require an assigned texture; a 1x1 white pixel suffices.
        var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        img.Fill(Colors.White);

        _line = new Line2D
        {
            Width = 150f,
            TextureMode = Line2D.LineTextureMode.Stretch,
            Texture = ImageTexture.CreateFromImage(img),
            Material = _mat,
            Points = new[] { Vector2.Zero, Vector2.Right * 100f },
        };
        AddChild(_line);
        Visible = false;
    }

    /// <summary>Feed current gameplay state into the shader. Target intensity 0 hides the beam.</summary>
    public void UpdateBeam(Caster caster, Vector2 from, Vector2 to, float targetIntensity, float delta)
    {
        // Fast attack, slightly slower release: the beam snaps on and sputters off.
        float speed = targetIntensity > _displayIntensity ? 6f : 10f;
        _displayIntensity = Mathf.MoveToward(_displayIntensity, targetIntensity, speed * delta);

        Visible = _displayIntensity > 0.02f;
        if (!Visible) return;

        _line.SetPointPosition(0, from);
        _line.SetPointPosition(1, to);

        // Style uniforms only change on an element/beam switch; don't re-marshal them at 60 fps.
        var style = caster.Style;
        if (style.StyleIndex != _appliedStyle || caster.Beam != _appliedType)
        {
            _appliedStyle = style.StyleIndex;
            _appliedType = caster.Beam;
            _mat.SetShaderParameter(SnCore, style.Core);
            _mat.SetShaderParameter(SnGlow, style.Glow);
            _mat.SetShaderParameter(SnStyle, style.StyleIndex);
            _mat.SetShaderParameter(SnType, (int)caster.Beam);
            _mat.SetShaderParameter(SnNoise, style.NoiseScale);
            _mat.SetShaderParameter(SnScroll, style.ScrollSpeed);
            _mat.SetShaderParameter(SnWobble, style.WobbleAmp);
            _mat.SetShaderParameter(SnSeed, caster.Side == 0 ? 3.1f : 9.7f);
        }
        _mat.SetShaderParameter(SnIntensity, _displayIntensity);
        _mat.SetShaderParameter(SnLen, from.DistanceTo(to));
    }

    public float DisplayIntensity => _displayIntensity;
}
