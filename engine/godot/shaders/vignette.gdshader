// The picture's edges: a little darker always, so the eye stays in the middle, and lit in the accent
// for a moment on every note that lands - the picture breathing with the beat.
shader_type canvas_item;

uniform vec4 glow : source_color = vec4(1.0, 0.0, 0.75, 1.0);
uniform float pulse = 0.0;
uniform float shade = 0.32;

void fragment() {
	vec2 d = (UV - 0.5) * 2.0;
	float r = length(d * vec2(1.0, 0.92));
	float edge = smoothstep(0.7, 1.45, r);
	float lit = clamp(pulse, 0.0, 1.0);
	COLOR = vec4(mix(vec3(0.0), glow.rgb, lit), edge * (shade + 0.55 * lit));
}
