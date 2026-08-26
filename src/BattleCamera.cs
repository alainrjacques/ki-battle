using Godot;

namespace KiBattle;

/// <summary>Trauma-based screen shake: offset = max * trauma^2 * noise, decaying over time.</summary>
public partial class BattleCamera : Camera2D
{
    private const float MaxOffset = 26f;
    private const float MaxRoll = 0.035f;
    private const float Decay = 1.5f;

    private float _trauma;
    private readonly FastNoiseLite _noise = new() { Frequency = 2.5f };
    private float _t;

    public void AddTrauma(float amount) => _trauma = Mathf.Clamp(_trauma + amount, 0f, 1f);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt * 60f;
        _trauma = Mathf.Max(0f, _trauma - Decay * dt);

        float shake = _trauma * _trauma;
        Offset = new Vector2(
            MaxOffset * shake * _noise.GetNoise2D(_t, 0f),
            MaxOffset * shake * _noise.GetNoise2D(0f, _t));
        Rotation = MaxRoll * shake * _noise.GetNoise2D(_t, _t);
    }
}
