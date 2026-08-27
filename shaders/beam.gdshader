// One shader family for all 24 beams (8 elements x 3 types).
// Runs on a 2-point Line2D with stretch UVs: UV.x = along beam, UV.y = across.
// All shaping (helix, wobble, jags, bubbles) happens here - no CPU polylines.
shader_type canvas_item;
render_mode blend_add;

uniform vec4 core_color : source_color = vec4(1.0);
uniform vec4 glow_color : source_color = vec4(0.3, 0.7, 1.0, 1.0);
uniform int beam_type = 0;   // 0 single, 1 twin helix, 2 pinpoint
uniform int style = 0;       // element index, see ElementDb
uniform float intensity : hint_range(0.0, 3.0) = 1.0;
uniform float noise_scale = 4.0;
uniform float scroll_speed = 1.5;
uniform float wobble_amp = 0.10;
uniform float helix_freq = 5.0;
uniform float seed = 0.0;
uniform float beam_len = 1000.0; // world px, keeps pattern density length-independent

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	// Quintic interpolation: C2-continuous, so hard thresholds and bloom don't
	// expose the cell grid as vertical/horizontal creases.
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	return 0.55 * vnoise(p) + 0.30 * vnoise(p * 2.13) + 0.15 * vnoise(p * 4.71);
}

// Piecewise-LINEAR 1D noise with its derivative: value in .x, d/dx in .y.
// Sharp kinks are the backbone of the lightning bolts; the derivative lets the
// renderer measure PERPENDICULAR distance to the path, so steep segments draw
// at full thickness instead of thinning into apparent breaks (tearing).
vec2 lin1dg(float x, float s) {
	float i = floor(x);
	float f = fract(x);
	float a = hash21(vec2(i, s)) * 2.0 - 1.0;
	float b = hash21(vec2(i + 1.0, s)) * 2.0 - 1.0;
	return vec2(mix(a, b, f), b - a);
}

// Two octaves of kinked displacement = a jagged bolt path (value, d/dx).
vec2 jagg(float x, float s) {
	vec2 c0 = lin1dg(x * 3.0, s);
	vec2 c1 = lin1dg(x * 9.0, s + 5.1);
	return vec2(c0.x * 0.7 + c1.x * 0.3, c0.y * 2.1 + c1.y * 2.7);
}

// The bolt's center height at pattern-x. Between fine-octave knots this is
// exactly linear, so the path is a polyline with knots every 1/(9*xscale).
float boltC(float xs, float bs, float xscale, float amp, float env, float helixSign, float ws, float t) {
	float c = 0.5 + jagg(xs * xscale, bs).x * amp * env;
	if (helixSign != 0.0) c += helixSign * 0.16 * ws * sin(xs * helix_freq + t * 2.0);
	return c;
}

float segDist(vec2 p, vec2 a, vec2 b) {
	vec2 ab = b - a;
	float h = clamp(dot(p - a, ab) / dot(ab, ab), 0.0, 1.0);
	return length(p - a - ab * h);
}

// TRUE distance (in UV.y units) from this pixel to the bolt polyline: exact
// point-to-segment distance over the 3 nearest segments. Uniform thickness on
// steep runs and corners alike — no tearing by construction. Pixel space is
// 240 px per pattern-x unit and 150 px across the quad.
float boltDist(float x, float bs, float xscale, float amp, float env, float y, float helixSign, float ws, float t) {
	float cell = 1.0 / (9.0 * xscale);
	float x0 = (floor(x * xscale * 9.0) - 1.0) * cell;
	vec2 p = vec2(x * 240.0, y * 150.0);
	vec2 prev = vec2(x0 * 240.0, boltC(x0, bs, xscale, amp, env, helixSign, ws, t) * 150.0);
	float d = 1e6;
	for (int k = 1; k <= 3; k++) {
		float xk = x0 + float(k) * cell;
		vec2 cur = vec2(xk * 240.0, boltC(xk, bs, xscale, amp, env, helixSign, ws, t) * 150.0);
		d = min(d, segDist(p, prev, cur));
		prev = cur;
	}
	return d / 150.0;
}

void fragment() {
	float t = TIME;
	// Pattern-space x: one unit per ~240 px of beam so visuals don't stretch with length.
	float x = UV.x * beam_len / 240.0;
	float y = UV.y;

	float widthScale = mix(0.55, 1.0, clamp(intensity, 0.0, 1.0));
	float bright = intensity;

	// --- Center-line displacement (element personality) ---
	float center = 0.5;
	float n = fbm(vec2(x * noise_scale * 0.35 - t * scroll_speed, seed * 7.3)) - 0.5;
	if (style == 1) { // Ice: crystalline, faceted
		n = floor(n * 6.0) / 6.0;
	}
	if (style == 2) { // Water: smooth flowing sine on top of soft noise
		center += 0.05 * sin(x * 3.0 - t * 2.2) * widthScale;
	}
	if (style == 6 || style == 7) { // Holy/Light: serene, no turbulence
		n *= 0.15;
	}
	center += wobble_amp * n * widthScale;

	// Center-line slope (px/px) via finite difference, so every profile can use
	// PERPENDICULAR distance: steep wobble sections keep their true thickness
	// instead of thinning into apparent breaks (tearing). 0.625 = 150 px quad
	// height / 240 px per pattern-x unit.
	float hfd = 0.06;
	float nR = fbm(vec2((x + hfd) * noise_scale * 0.35 - t * scroll_speed, seed * 7.3)) - 0.5;
	float nL = fbm(vec2((x - hfd) * noise_scale * 0.35 - t * scroll_speed, seed * 7.3)) - 0.5;
	if (style == 1) { nR = floor(nR * 6.0) / 6.0; nL = floor(nL * 6.0) / 6.0; }
	if (style == 6 || style == 7) { nR *= 0.15; nL *= 0.15; }
	float centerSlope = wobble_amp * (nR - nL) / (2.0 * hfd) * widthScale * 0.625;
	if (style == 2) centerSlope += 0.05 * 3.0 * cos(x * 3.0 - t * 2.2) * widthScale * 0.625;

	// --- Beam profile ---
	float profile = 0.0;
	float d = abs(y - center) / sqrt(1.0 + centerSlope * centerSlope);
	if (beam_type == 0) { // Single: broad steady band
		float hw = 0.36 * widthScale;
		profile = pow(smoothstep(hw, 0.0, d), 1.6);
	} else if (beam_type == 1) { // Twin: two crossing helix strands + rungs
		float amp = 0.20 * widthScale;
		float phase = x * helix_freq + t * 2.0;
		float s = sin(phase);
		float helixSlope = amp * helix_freq * cos(phase) * 0.625;
		float slA = centerSlope + helixSlope;
		float slB = centerSlope - helixSlope;
		float cA = center + amp * s;
		float cB = center - amp * s;
		float w = 0.075 * widthScale;
		float dA = abs(y - cA) / sqrt(1.0 + slA * slA);
		float dB = abs(y - cB) / sqrt(1.0 + slB * slB);
		float strandA = exp(-pow(dA / w, 2.0));
		float strandB = exp(-pow(dB / w, 2.0));
		float rung = smoothstep(0.18, 0.0, abs(s)) * smoothstep(amp + w, 0.0, abs(y - center)) * 0.55;
		profile = clamp(strandA + strandB + rung, 0.0, 1.6);
	} else { // Pinpoint: razor ray with travelling pulse packets
		float hw = 0.075 * widthScale;
		profile = pow(smoothstep(hw, 0.0, d), 1.2);
		float p = fract(x * 0.8 - t * 3.5);
		float packet = exp(-pow((p - 0.5) * 6.0, 2.0));
		profile *= 1.0 + 1.2 * packet;
		bright *= 1.25; // concentrated: brighter core for its width
	}

	if (style == 3) {
		// Lightning replaces the band profile entirely: a bright main bolt plus
		// dimmer branch filaments, all piecewise-linear jagged paths re-seeded
		// rapidly and converging at the muzzle and the clash point.
		float tq = floor(t * 12.0) * 0.618 + seed;
		float env = mix(0.10, 1.0, sin(3.14159 * UV.x)); // pinch at both ends
		float boltAmp = 0.30 * widthScale;
		profile = 0.0;

		int mains = beam_type == 1 ? 2 : 1;
		float mainW = (beam_type == 2 ? 0.014 : 0.026) * (0.7 + 0.3 * widthScale);
		for (int b = 0; b < 2; b++) {
			if (b >= mains) { break; }
			float bs = tq + float(b) * 17.3;
			float helixSign = beam_type == 1 ? (b == 0 ? 1.0 : -1.0) : 0.0;
			float dm = boltDist(x, bs, 1.0, boltAmp, env, y, helixSign, widthScale, t);
			profile += smoothstep(mainW, 0.0, dm) * 1.15   // hot white core
			         + smoothstep(0.10, 0.0, dm) * 0.22;   // soft haze hugging the bolt
		}
		for (int b = 0; b < 3; b++) { // stray branches, wider wander, faint
			float bs = tq + 31.7 + float(b) * 7.9;
			float dm = boltDist(x, bs, 1.4, boltAmp * 1.5, env, y, 0.0, widthScale, t);
			profile += smoothstep(0.011, 0.0, dm) * 0.32;
		}
		profile = clamp(profile, 0.0, 1.4);
		bright *= 0.85 + 0.4 * hash21(vec2(tq, seed)); // flicker per re-seed
	}

	// --- Element flourishes on intensity ---
	if (style == 0) { // Fire: living flicker + licks
		float licks = fbm(vec2(x * 2.0 - t * 3.0, y * 3.0 + seed));
		profile *= 0.75 + 0.5 * licks;
		bright *= 0.9 + 0.25 * vnoise(vec2(t * 12.0, seed));
	}
	if (style == 2) { // Water: caustic shimmer
		profile *= 0.85 + 0.3 * vnoise(vec2(x * 5.0 - t * 1.2, y * 8.0 + seed));
	}
	if (style == 5) { // Poison: drifting bubbles inside the beam
		float b = vnoise(vec2(x * 3.0 - t * 0.9, y * 7.0 + seed));
		float inside = smoothstep(0.3, 0.15, d);
		profile += smoothstep(0.58, 0.84, b) * inside * 0.9;
	}
	if (style == 6) { // Holy: travelling halo rings
		float r = fract(x * 1.1 - t * 1.4);
		profile *= 1.0 + 0.8 * exp(-pow((r - 0.5) * 7.0, 2.0));
	}

	// --- Ends: muzzle fade-in, clash-end compression ---
	float fadeIn = smoothstep(0.0, 0.045, UV.x);
	float clashEnd = 1.0 + 0.5 * smoothstep(0.90, 1.0, UV.x);

	// --- Compose HDR color (values > 1.0 feed the 2D glow) ---
	vec3 col;
	if (style == 4) { // Darkness: hollow dark core, blazing violet rim
		float hw = 0.36 * widthScale;
		float rim = smoothstep(hw, hw * 0.55, d) * smoothstep(hw * 0.15, hw * 0.5, d);
		if (beam_type == 1) { rim = profile * 0.5; } // helix reads better lit
		if (beam_type == 2) { rim = profile * 0.6; }
		float mist = profile * 0.22;
		col = glow_color.rgb * (rim * 3.2 + mist) * bright;
	} else {
		col = core_color.rgb * pow(profile, 3.0) * 1.8 * bright
		    + glow_color.rgb * profile * 0.9 * bright;
	}
	if (style == 7 && beam_type != 1) { // Light: prismatic edge fringe (band profiles only)
		float hw = 0.36 * widthScale;
		float edge = smoothstep(hw, hw * 0.7, d) * smoothstep(hw * 0.45, hw * 0.68, d);
		vec3 rainbow = 0.5 + 0.5 * cos(6.28318 * (d * 5.0 - t * 0.4) + vec3(0.0, 2.094, 4.188));
		col += rainbow * edge * 1.4 * bright;
	}

	// Cap the HDR peak: extreme values dump so much energy into the glow mips
	// that their upsampling blocks become visible as vertical seams.
	COLOR = vec4(min(col * fadeIn * clashEnd, vec3(4.0)), 1.0);
}
