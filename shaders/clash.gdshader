// Clash point, anime style: a large translucent energy orb around the collision,
// a spiky irregular white star core, and small dash-sparks drifting inside the
// orb. Tinted by both beams' colors weighted by who is winning the push.
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
	float I = clamp(intensity, 0.0, 1.6);

	// Side tint: left half leans color_a, right half color_b, biased by push ratio.
	float sideMix = clamp(0.5 + p.x * 1.6 + (ratio - 0.5) * 0.8, 0.0, 1.0);
	vec3 tint = mix(color_a.rgb, color_b.rgb, sideMix);

	// --- 1. Translucent energy orb (SDR: must NOT bloom) ---
	float orbR = (0.80 + 0.04 * sin(t * 3.1)) * mix(0.45, 1.0, I / 1.6);
	float orb = smoothstep(orbR, orbR * 0.2, r);
	float rim = smoothstep(orbR, orbR * 0.88, r) * smoothstep(orbR * 1.02, orbR * 0.97, r);
	vec3 orbCol = mix(vec3(0.62, 0.65, 0.80), tint, 0.35);
	vec3 col = orbCol * (orb * 0.34 + rim * 0.25) * smoothstep(0.15, 0.7, I);

	// --- 2. Dash-sparks drifting inside the orb ---
	float dashes = 0.0;
	vec2 g = p * 13.0;
	vec2 cell = floor(g);
	float rnd = hash21(cell + seed);
	if (rnd > 0.35) {
		vec2 f = fract(g) - 0.5;
		float phase = fract(t * (0.5 + rnd * 0.7) + rnd * 7.0);
		vec2 radial = normalize(cell + 0.5 + vec2(0.001));
		f -= radial * (phase - 0.5) * 0.35; // slow outward drift
		float a = rnd * 6.28318 + floor(t * 0.9 + rnd) * 1.7;
		vec2 fr = vec2(f.x * cos(a) + f.y * sin(a), -f.x * sin(a) + f.y * cos(a));
		float dash = smoothstep(0.20, 0.10, abs(fr.x)) * smoothstep(0.055, 0.02, abs(fr.y));
		dashes = dash * sin(phase * 3.14159); // fade in and out over its life
	}
	float inOrb = smoothstep(orbR * 0.98, orbR * 0.7, r);
	col += mix(vec3(1.0), tint, 0.25) * dashes * inOrb * 0.9 * smoothstep(0.2, 0.8, I);

	// --- 3. Spiky irregular star core (HDR: blooms) ---
	float na = vnoise(vec2(ang * 2.5 + seed, floor(t * 9.0) * 0.61));
	float starR = 0.10 + 0.16 * pow(abs(cos(ang * 5.0 + na * 4.0)), 3.0)
	            + 0.10 * pow(abs(cos(ang * 2.0 - na * 2.0)), 2.0) + 0.06 * na;
	starR *= mix(0.5, 1.0, I / 1.6);
	float star = smoothstep(starR, starR * 0.3, r);
	col += (vec3(1.05) + tint * 0.3) * star * 1.15 * I;

	// Hot center flare
	col += vec3(1.0) * exp(-r * 9.0) * 0.85 * I;

	COLOR = vec4(col, 1.0);
}
