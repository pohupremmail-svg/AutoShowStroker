// Frosted glass: whatever is behind a panel, blurred and tinted, inside the panel's own rounded
// shape - the session plays on visibly behind it. Drawn on a StyleBoxFlat whose vertex colours say
// which part is which (Ui/Glass.cs): white is the body, black the rim, blue the shadow under it.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec4 tint : source_color = vec4(0.176, 0.114, 0.227, 0.45);
uniform vec4 rim : source_color = vec4(1.0, 0.76, 0.93, 0.18);
uniform vec4 shadow : source_color = vec4(0.0, 0.0, 0.0, 0.35);
uniform float blur = 3.4;

void fragment() {
	vec4 part = COLOR;
	if (part.b > 0.5 && part.r < 0.5) {
		COLOR = vec4(shadow.rgb, shadow.a * part.a);
	} else {
		vec2 px = SCREEN_PIXEL_SIZE * 6.0;
		vec3 back = textureLod(screen_texture, SCREEN_UV, blur).rgb * 0.4
			+ textureLod(screen_texture, SCREEN_UV + vec2(px.x, px.y), blur).rgb * 0.15
			+ textureLod(screen_texture, SCREEN_UV + vec2(-px.x, px.y), blur).rgb * 0.15
			+ textureLod(screen_texture, SCREEN_UV + vec2(px.x, -px.y), blur).rgb * 0.15
			+ textureLod(screen_texture, SCREEN_UV + vec2(-px.x, -px.y), blur).rgb * 0.15;
		vec3 glass = mix(back, tint.rgb, tint.a);
		float edge = 1.0 - part.r;
		COLOR = vec4(mix(glass, rim.rgb, edge * rim.a * 2.5), part.a);
	}
}
