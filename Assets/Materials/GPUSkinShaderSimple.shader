Shader "Custom/GPUSkinShaderSimple"
{
	Properties{
		// Per mesh values
		_MainTex("Base (RGB)", 2D) = "white" {}
		[HideInInspector] _Animation0("Baked Animation (row 0)", 2D) = "white" {}
		[HideInInspector] _Animation1("Baked Animation (row 1)", 2D) = "white" {}
		// Per instance values
		[HideInInspector] _AnimState("Animation Normalized Time: [Time0, Weight0, Time1, Weight1]", Vector) = (0,1,0,0)
	}

	SubShader{
		Tags{ "RenderType" = "Opaque"}
		LOD 200

		CGPROGRAM
		#pragma surface surf BlinnPhong vertex:vert addshadow halfasview noforwardadd exclude_path:deferred exclude_path:prepass
		#pragma multi_compile MORTON_CODE XY_INDEXING
		#pragma target 3.5

		sampler2D _MainTex;

		#include "GPUSkinning.cginc"
		void vert(inout appdata v) {
			applySkinSimple(v); 
		}

		struct Input {
			half2 uv_MainTex;
		};

		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * 0.7;
			o.Albedo = c.rgb;
		}
		ENDCG
	}

	SubShader{
		Tags{ "RenderType" = "Opaque"}
		LOD 150

		CGPROGRAM
		#pragma surface surf BlinnPhong vertex:vert addshadow halfasview noforwardadd exclude_path:deferred exclude_path:prepass
		#pragma multi_compile MORTON_CODE XY_INDEXING
		#pragma target 3.5

		sampler2D _MainTex;

		#include "GPUSkinning.cginc"
		void vert(inout appdata v) {
			applySkinSimple(v); 
		}

		struct Input {
			half2 uv_MainTex;
		};

		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
			o.Albedo = c.rgb;
		}
		ENDCG
	}
	FallBack "Diffuse"
}

