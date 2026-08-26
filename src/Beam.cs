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

    public override void _Ready()
    {
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

        var style = caster.Style;
        _mat.SetShaderParameter("core_color", style.Core);
        _mat.SetShaderParameter("glow_color", style.Glow);
        _mat.SetShaderParameter("style", style.StyleIndex);
        _mat.SetShaderParameter("beam_type", (int)caster.Beam);
        _mat.SetShaderParameter("intensity", _displayIntensity);
        _mat.SetShaderParameter("noise_scale", style.NoiseScale);
        _mat.SetShaderParameter("scroll_speed", style.ScrollSpeed);
        _mat.SetShaderParameter("wobble_amp", style.WobbleAmp);
        _mat.SetShaderParameter("beam_len", from.DistanceTo(to));
        _mat.SetShaderParameter("seed", caster.Side == 0 ? 3.1f : 9.7f);
    }

    public float DisplayIntensity => _displayIntensity;
}
