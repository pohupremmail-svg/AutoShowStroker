// Frosted glass: whatever is behind a panel, blurred hard, its colours lifted and lightly tinted,
// inside the panel's own rounded shape - the session plays on visibly behind it. Drawn on a
// StyleBoxFlat whose vertex colours say which part is which (Ui/Glass.cs): white is the body, black
// the rim, blue the shadow under it.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec4 tint : source_color = vec4(0.176, 0.114, 0.227, 0.4);
uniform vec4 rim : source_color = vec4(1.0, 0.76, 0.93, 0.26);
uniform vec4 shadow : source_color = vec4(0.0, 0.0, 0.0, 0.45);
uniform float blur = 4.6;
uniform float saturation = 1.35;

void fragment() {
	vec4 part = COLOR;
	if (part.b > 0.5 && part.r < 0.5) {
		COLOR = vec4(shadow.rgb, shadow.a * part.a);
	} else {
		vec2 px = SCREEN_PIXEL_SIZE * 14.0;
		vec3 back = vec3(0.0);
		for (int i = -1; i <= 1; i++) {
			for (int j = -1; j <= 1; j++) {
				float w = (i == 0 && j == 0) ? 0.2 : 0.1;
				back += textureLod(screen_texture, SCREEN_UV + vec2(float(i), float(j)) * px, blur).rgb * w;
			}
		}
		float luma = dot(back, vec3(0.299, 0.587, 0.114));
		back = mix(vec3(luma), back, saturation);
		// The brightest of the picture held down, so text on the glass stays readable on a white
		// picture; glass catches a little light of its own.
		back = back / (1.0 + back * 0.55) * 1.3;
		back = mix(back, vec3(1.0), 0.03);
		vec3 glass = mix(back, tint.rgb, tint.a);
		float edge = 1.0 - part.r;
		COLOR = vec4(mix(glass, rim.rgb, clamp(edge * rim.a * 3.0, 0.0, 1.0)), part.a);
	}
}
