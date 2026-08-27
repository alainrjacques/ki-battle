using Godot;

namespace KiBattle;

/// <summary>
/// One caster's beam: a 2-point Line2D stretched from muzzle to clash point,
/// with the beam shader doing all the visual work. Updated every frame by BattleManager.
/// </summary>
public partial class Beam : Node2D
{
    private MeshInstance2D _quad = null!;
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

        // One explicit unit quad (scaled to length x 150 px each frame): UV.x is
        // guaranteed continuous 0..1 across the whole beam. Line2D tessellation
        // produced UV discontinuities that read as vertical tearing seams.
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = new Vector2[]
        {
            new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f),
        };
        arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1),
        };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        _quad = new MeshInstance2D { Mesh = mesh, Material = _mat };
        AddChild(_quad);
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

        _quad.GlobalPosition = (from + to) * 0.5f;
        _quad.GlobalRotation = (to - from).Angle();
        _quad.Scale = new Vector2(from.DistanceTo(to), 150f);

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
