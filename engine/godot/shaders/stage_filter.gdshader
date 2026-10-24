// What the effect layer does to the picture itself, drawn over the stage and under the note track.
//
// Censoring: everything outside a circle round the focus is blurred, pixelated or darkened; inside it
// the picture stays clear, with a soft edge. Aberration: the red and blue of the picture are pulled
// apart sideways - the climax's shudder.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;

// 0 clear, 1 blur, 2 mosaic, 3 dark.
uniform int mode = 0;
// The clear circle, in screen UV for its centre and screen pixels for its radius.
uniform vec2 focus = vec2(0.5);
uniform float radius = 100.0;
// How much of the radius the edge fades over.
uniform float feather = 0.45;
// How far red and blue are pulled apart, in pixels.
uniform float aberration = 0.0;
// A mosaic tile, in pixels.
uniform float block = 18.0;

vec3 split(vec2 uv, vec2 px) {
	if (aberration <= 0.0) return texture(screen_texture, uv).rgb;
	vec2 off = vec2(aberration, 0.0) * px;
	return vec3(texture(screen_texture, uv + off).r, texture(screen_texture, uv).g, texture(screen_texture, uv - off).b);
}

vec3 blurred(vec2 uv, vec2 px) {
	vec3 sum = vec3(0.0);
	for (int i = -2; i <= 2; i++) {
		for (int j = -2; j <= 2; j++) {
			sum += textureLod(screen_texture, uv + vec2(float(i), float(j)) * px * 11.0, 3.5).rgb;
		}
	}
	return sum / 25.0;
}

void fragment() {
	vec2 px = SCREEN_PIXEL_SIZE;
	vec2 uv = SCREEN_UV;
	vec3 clear = split(uv, px);
	if (mode == 0) {
		COLOR = vec4(clear, 1.0);
	} else {
		float dist = length((uv - focus) / px);
		float hide = smoothstep(radius * (1.0 - feather), radius, dist);
		vec3 hidden;
		if (mode == 1) {
			hidden = blurred(uv, px);
		} else if (mode == 2) {
			vec2 cell = (floor(uv / px / block) + 0.5) * block * px;
			hidden = textureLod(screen_texture, cell, 1.0).rgb;
		} else {
			hidden = textureLod(screen_texture, uv, 2.0).rgb * 0.2;
		}
		COLOR = vec4(mix(clear, hidden, hide), 1.0);
	}
}
