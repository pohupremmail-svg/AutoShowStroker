// The telepathy frame: the character's bust (the SubViewport this is the material of) in a glowing
// window over the picture. The window sits inside the rect with `pad` of it left round it for the glow.
//   style 0 - neon oval: an upright ellipse, its rim pulsing in the accent colour
//   style 1 - tube screen: a rounded rectangle like an old CRT, in full colour - faint scanlines, a
//             slight bulge, a thin colour fringe, a soft flicker now and then
shader_type canvas_item;

uniform int style = 0;
uniform vec4 glow : source_color = vec4(1.0, 0.17, 0.84, 1.0);
uniform float pad = 0.08;
uniform float fade = 1.0;
uniform float flare = 0.0; // 0..1: brighter while she speaks

float round_box(vec2 p, vec2 b, float r) {
	vec2 q = abs(p) - b + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

// Distance to an ellipse's edge, close enough for a rim a few pixels wide.
float ellipse(vec2 p, vec2 radii) {
	float k = length(p / radii);
	return (k - 1.0) * min(radii.x, radii.y);
}

float hash(float n) {
	return fract(sin(n) * 43758.5453);
}

void fragment() {
	vec2 px = 1.0 / TEXTURE_PIXEL_SIZE;
	vec2 p = (UV - 0.5) * px;
	vec2 inner = px * (0.5 - pad);
	float r = min(inner.x, inner.y);
	float d = style == 0 ? ellipse(p, inner) : round_box(p, inner, r * 0.16);
	vec2 tuv = (UV - pad) / (1.0 - 2.0 * pad);
	float flicker = 1.0;
	if (style == 1) {
		// The glass bulges a little: the picture pulled in towards the corners.
		vec2 c = tuv - 0.5;
		tuv = 0.5 + c * (1.0 + 0.06 * dot(c, c));
	}
	float pulse = 0.75 + 0.25 * sin(TIME * 2.8);

	vec4 figure = texture(TEXTURE, clamp(tuv, 0.0, 1.0));
	vec3 back = mix(vec3(0.05, 0.03, 0.08), glow.rgb * 0.22, smoothstep(r * 1.3, 0.0, length(p - vec2(0.0, -inner.y * 0.25))));
	vec3 inside = mix(back, figure.rgb, figure.a);
	if (style == 1) {
		// A thin colour fringe at the figure's edges, faint scanlines, darker corners, a soft band rolling
		// down, and once in a while the picture dims for a moment.
		vec2 shift = vec2(1.0 / px.x, 0.0);
		float red = texture(TEXTURE, clamp(tuv - shift, 0.0, 1.0)).a;
		float blue = texture(TEXTURE, clamp(tuv + shift, 0.0, 1.0)).a;
		inside.r = mix(inside.r, max(inside.r, 0.8), 0.25 * max(red - figure.a, 0.0));
		inside.b = mix(inside.b, max(inside.b, 0.8), 0.25 * max(blue - figure.a, 0.0));
		inside *= 0.92 + 0.08 * sin(FRAGCOORD.y * 3.14159 / 1.5);
		vec2 c = tuv - 0.5;
		inside *= 1.0 - 0.35 * dot(c, c) * 2.0;
		inside += vec3(0.06) * smoothstep(0.1, 0.0, abs(UV.y - fract(TIME * 0.2)));
		flicker = hash(floor(TIME * 10.0)) > 0.97 ? 0.88 : 1.0;
	}

	// Inside the window, its bright rim, and the glow falling off outside it. Straight alpha: outside
	// the window the colour is the rim's own and the halo is how much of it shows.
	float a_in = smoothstep(1.0, -1.0, d);
	float line = smoothstep(2.5, 0.0, abs(d)) * (style == 1 ? 0.7 : 1.0);
	float halo = exp(-max(d, 0.0) / (r * (0.1 + 0.06 * flare))) * (d > 0.0 ? 1.0 : 0.0) * (0.6 + 0.4 * flare) * pulse;
	vec3 color = mix(glow.rgb, inside, a_in);
	color = mix(color, glow.rgb * 1.15, line * (0.8 + 0.2 * pulse));
	float alpha = max(a_in, max(line, halo));
	COLOR = vec4(color, alpha * fade * mix(1.0, flicker, a_in));
}
