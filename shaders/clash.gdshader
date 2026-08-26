// Clash-point shockwave: polar rings + core flare + rotating spikes,
// tinted by both beams' colors weighted by who is winning the push.
shader_type canvas_item;
render_mode blend_add;

uniform vec4 color_a : source_color = vec4(0.3, 0.7, 1.0, 1.0); // left beam glow
uniform vec4 color_b : source_color = vec4(1.0, 0.4, 0.2, 1.0); // right beam glow
uniform float intensity : hint_range(0.0, 3.0) = 1.0;
uniform float ratio : hint_range(0.0, 1.0) = 0.5; // 0 = left dominating, 1 = right
uniform float seed = 0.0;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
	           mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	float t = TIME;
	vec2 p = UV - 0.5;
	float r = length(p) * 2.0;
	float ang = atan(p.y, p.x);

	// Side tint: left half leans color_a, right half color_b, biased by push ratio.
	float sideMix = clamp(0.5 + p.x * 1.6 + (ratio - 0.5) * 0.8, 0.0, 1.0);
	vec3 tint = mix(color_a.rgb, color_b.rgb, sideMix);

	// Core flare: tight, so the beams stay readable behind it
	float core = exp(-r * 9.0) * 1.8;

	// Expanding rings
	float rings = 0.0;
	for (int i = 0; i < 2; i++) {
		float speed = 0.9 + float(i) * 0.5;
		float band = fract(r * 1.6 - t * speed + float(i) * 0.37);
		rings += exp(-pow((band - 0.35) * 7.0, 2.0)) * smoothstep(0.9, 0.2, r) * 0.45;
	}

	// Jittering radial spikes
	float na = vnoise(vec2(ang * 2.2 + seed, floor(t * 14.0) * 0.7));
	float spikes = pow(abs(cos(ang * 6.0 + na * 4.0 + t * 2.5)), 10.0)
	             * smoothstep(0.9, 0.1, r) * 0.55;

	float energy = (core + rings + spikes) * intensity;
	vec3 col = tint * energy + vec3(1.0) * core * 0.45 * intensity;

	COLOR = vec4(col, 1.0);
}
