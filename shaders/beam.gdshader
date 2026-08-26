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
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	return 0.55 * vnoise(p) + 0.30 * vnoise(p * 2.13) + 0.15 * vnoise(p * 4.71);
}

// Piecewise-LINEAR 1D noise: sharp kinks, the backbone of the lightning bolts.
float lin1d(float x, float s) {
	float i = floor(x);
	float f = fract(x);
	return mix(hash21(vec2(i, s)), hash21(vec2(i + 1.0, s)), f) * 2.0 - 1.0;
}

// Two octaves of kinked displacement = a jagged bolt path.
float jag(float x, float s) {
	return lin1d(x * 3.0, s) * 0.7 + lin1d(x * 9.0, s + 5.1) * 0.3;
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

	// --- Beam profile ---
	float profile = 0.0;
	float d = abs(y - center);
	if (beam_type == 0) { // Single: broad steady band
		float hw = 0.36 * widthScale;
		profile = pow(smoothstep(hw, 0.0, d), 1.6);
	} else if (beam_type == 1) { // Twin: two crossing helix strands + rungs
		float amp = 0.20 * widthScale;
		float phase = x * helix_freq + t * 2.0;
		float s = sin(phase);
		float cA = center + amp * s;
		float cB = center - amp * s;
		float w = 0.075 * widthScale;
		float strandA = exp(-pow(abs(y - cA) / w, 2.0));
		float strandB = exp(-pow(abs(y - cB) / w, 2.0));
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
			float c = 0.5 + jag(x, bs) * boltAmp * env;
			if (beam_type == 1) { // twin: two bolts spiralling around each other
				c += (b == 0 ? 1.0 : -1.0) * 0.16 * widthScale * sin(x * helix_freq + t * 2.0);
			}
			profile += smoothstep(mainW, 0.0, abs(y - c)) * 1.15;
		}
		for (int b = 0; b < 3; b++) { // stray branches, wider wander, faint
			float bs = tq + 31.7 + float(b) * 7.9;
			float c = 0.5 + jag(x * 1.4, bs) * boltAmp * 1.5 * env;
			profile += smoothstep(0.011, 0.0, abs(y - c)) * 0.32;
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
		profile += smoothstep(0.62, 0.78, b) * inside * 0.9;
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

	COLOR = vec4(col * fadeIn * clashEnd, 1.0);
}
