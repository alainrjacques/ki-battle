// Arena backdrop: deep night gradient, slow nebula drift, faint stars.
// Deliberately dim (SDR) so only the beams bloom.
shader_type canvas_item;

uniform vec4 top_color : source_color = vec4(0.03, 0.02, 0.08, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.10, 0.04, 0.10, 1.0);
uniform vec4 nebula_color : source_color = vec4(0.15, 0.08, 0.25, 1.0);

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

float fbm(vec2 p) {
	return 0.5 * vnoise(p) + 0.3 * vnoise(p * 2.1) + 0.2 * vnoise(p * 4.3);
}

void fragment() {
	vec2 uv = UV;
	vec3 col = mix(top_color.rgb, bottom_color.rgb, pow(uv.y, 1.4));

	// Slow nebula
	float neb = fbm(uv * vec2(3.0, 2.0) + vec2(TIME * 0.008, 0.0));
	col += nebula_color.rgb * pow(neb, 2.2) * 0.8;

	// Stars: sparse bright points in the upper half
	vec2 cell = floor(uv * vec2(160.0, 90.0));
	float star = step(0.997, hash21(cell));
	float twinkle = 0.5 + 0.5 * sin(TIME * (1.0 + hash21(cell + 7.0) * 3.0) + hash21(cell + 13.0) * 6.28);
	col += vec3(0.8, 0.85, 1.0) * star * twinkle * smoothstep(0.7, 0.1, uv.y) * 0.5;

	// Ground haze line
	col += vec3(0.12, 0.06, 0.14) * smoothstep(0.78, 0.92, uv.y) * (1.0 - smoothstep(0.92, 1.0, uv.y));

	COLOR = vec4(col, 1.0);
}
