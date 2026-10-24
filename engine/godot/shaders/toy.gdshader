// The app's own toy (Gooner.Core.Characters.ToyModel): coloured by what part each vertex is - its
// vertex colour holds the weights, red the head, green a vein, blue the sac, alpha how near the foot -
// in the colour picked under Settings > Look (ToyColours). Mixed exactly as ToyTint.Albedo mixes, so
// a preview rendered from the core is what this draws. Silicone: a clear coat over it, and a little
// light coming through.
shader_type spatial;

uniform vec3 body : source_color = vec3(0.90, 0.68, 0.60);
uniform vec3 body_low : source_color = vec3(0.84, 0.60, 0.53);
uniform vec3 head : source_color = vec3(0.86, 0.50, 0.51);
uniform vec3 vein : source_color = vec3(0.68, 0.47, 0.58);
uniform vec3 sac : source_color = vec3(0.80, 0.56, 0.50);
// ToyTint.VeinShare.
uniform float vein_share = 0.55;

void fragment() {
	vec3 colour = mix(body, body_low, COLOR.a);
	colour = mix(colour, sac, COLOR.b);
	colour = mix(colour, head, COLOR.r);
	ALBEDO = mix(colour, vein, vein_share * COLOR.g);
	ROUGHNESS = 0.34;
	SPECULAR = 0.55;
	CLEARCOAT = 0.35;
	CLEARCOAT_ROUGHNESS = 0.25;
	SSS_STRENGTH = 0.35;
	RIM = 0.25;
	RIM_TINT = 0.6;
}
