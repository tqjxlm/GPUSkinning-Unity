Shader "Custom/GPUSkinShader"
{
	Properties{
		// Per mesh values
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_MetallicGlossMap("Metallic", 2D) = "white" {}
		[HideInInspector] _Animation0("Baked Animation (row 0)", 2D) = "white" {}
		[HideInInspector] _Animation1("Baked Animation (row 1)", 2D) = "white" {}
		// Per instance values
		[HideInInspector] _AnimState("Animation Normalized Time: [Time0, Weight0, Time1, Weight1]", Vector) = (0,1,0,0)
	}
	SubShader{
		Tags{ "RenderType" = "Opaque"}
		LOD 200

		CGPROGRAM
		#pragma surface surf Standard vertex:vert addshadow halfasview noforwardadd exclude_path:deferred exclude_path:prepass
		#pragma multi_compile MORTON_CODE XY_INDEXING
		#pragma multi_compile CHARACTER WEAPON
		#pragma target 3.5

		sampler2D _MainTex;
		sampler2D _MetallicGlossMap;

		half _Glossiness;
		fixed4 _Color;

		#include "GPUSkinning.cginc"
		void vert(inout appdata v) {
			#if CHARACTER
				applySkin(v);
			#endif
			#if WEAPON
				applyWeapon(v);
			#endif
		}

		struct Input {
			half2 uv_MainTex;
			// half2 uv_BumpMap;
		};

		void surf(Input IN, inout SurfaceOutputStandard o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Metallic = tex2D(_MetallicGlossMap, IN.uv_MainTex);
			o.Smoothness = _Glossiness;
			// o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
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
			applySkin(v); 
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

