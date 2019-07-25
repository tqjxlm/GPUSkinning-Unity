// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/DepthTest"
{
	Properties{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo (RGB)", 2D) = "white" {}
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_MetallicGlossMap("Metallic", 2D) = "white" {}
		[Normal] _BumpMap("Normal Map", 2D) = "bump" {}
		_EmissionColor("Color", Color) = (0,0,0)
		_EmissionMap("Emission", 2D) = "white" {}
		_OutlineWidth("Outline Width", Float) = 0.1
		_BlendRate("Blend Rate", Float) = 0.5

		[HideInInspector] _SrcBlend ("__src", Float) = 1
		[HideInInspector] _DstBlend ("__dst", Float) = 0.1
	}
	SubShader{
		Tags{ "RenderType" = "Opaque" "Queue" = "Geometry" }
		LOD 200

		// Pass 1: Default rendering
		ZTest LEqual
		ZWrite On
		Lighting On

		Stencil {
			Ref 1
			Comp Always
			Pass Replace
		}

		CGPROGRAM
		#pragma surface surf Standard
		#pragma target 3.0

		sampler2D _MainTex;
		sampler2D _MetallicGlossMap;
		sampler2D _BumpMap;
		sampler2D _EmissionMap;

		half _Glossiness;
		fixed4 _Color;
		fixed4 _EmissionColor;

		struct Input {
			float2 uv_MainTex;
			float2 uv_BumpMap;
		};

		void surf(Input IN, inout SurfaceOutputStandard o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Metallic = tex2D(_MetallicGlossMap, IN.uv_MainTex);
			o.Smoothness = _Glossiness;
			o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
			o.Emission = tex2D(_EmissionMap, IN.uv_MainTex) * _EmissionColor;
		}
		ENDCG

		// Pass 2: Occluded blending
		Tags{ "RenderType" = "Opaque" "Queue" = "Transparent" }
		LOD 200

		ZTest Greater
		ZWrite Off
		Lighting Off

		Stencil{
			Ref 1
			Comp NotEqual
			Pass Replace
		}

		Blend [_SrcBlend] [_DstBlend]

		CGPROGRAM
		#pragma surface surf Lambert alpha:fade nolightmap noshadow nofog
		#pragma target 3.0

		sampler2D _MainTex;
		half _BlendRate;
		fixed4 _Color;

		struct Input {
			float2 uv_MainTex;
		};

		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Alpha = c.a * _BlendRate;
		}
		ENDCG

		Tags{ "RenderType" = "Opaque" "Queue" = "Transparent" }
		LOD 200

		// Pass 3: Contour extraction		
		Pass {
			ZTest Greater
			ZWrite Off
			Stencil {
				Ref 1
				Comp NotEqual
				Pass zero
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile OUTLINE_OFF OUTLINE_ON

			struct appdata {
				float4 vertex : POSITION;
			};

			struct v2f {
				float4 vertex : SV_POSITION;
			};

			half _OutlineWidth;
			v2f vert (appdata v)
			{
				v2f o;
				v.vertex.xyz *= 1 + _OutlineWidth;
				o.vertex = UnityObjectToClipPos(v.vertex);
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				#if OUTLINE_OFF
				discard;
				#endif

				return fixed4(1, 1, 0, 1);
			}
			ENDCG
		}
	}
	FallBack "Diffuse"
}
