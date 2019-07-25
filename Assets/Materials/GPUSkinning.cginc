struct appdata {
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
	float4 vertex : POSITION;
	half3 normal : NORMAL;
	half3 bone: TANGENT;
	half2 texcoord : TEXCOORD0;
	half2 texcoord1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

sampler2D _Animation0;
sampler2D _Animation1;

UNITY_INSTANCING_BUFFER_START(Props)
    UNITY_DEFINE_INSTANCED_PROP(half4, _AnimState)
UNITY_INSTANCING_BUFFER_END(Props)

#if MORTON_CODE

	// The pixel map should be with size [_size, _size], _size should be pow of 2
	uniform int _size;
	// The power of _size. _pow = log(_size)
	uniform int _pow;

	// Get the morton code, and decode it to normalized uv
	inline float4 decodeMorton(half x, half y)
	{
		uint morton = (uint)x | ((uint)y << 1);
		return float4(
			((float)(morton & (_size - 1)) + 0.5) / _size,
			((float)(morton >> _pow) + 0.5) / _size,
			0, 0);
	}

#endif

inline half4 sampleRow(sampler2D tex, float4x4 uv, half4 weights)
{
	half4x4 samples = transpose(half4x4(
		tex2Dlod(tex, uv[0]),
		tex2Dlod(tex, uv[1]),
		tex2Dlod(tex, uv[2]),
		tex2Dlod(tex, uv[3])));
	return mul(samples, weights);
}

inline half4 sampleRowSimple(sampler2D tex, float2x4 uv, half2 weights)
{
	half4x2 samples = transpose(half2x4(
		tex2Dlod(tex, uv[0]),
		tex2Dlod(tex, uv[1])));
	return mul(samples, weights);
}

// Credit: Kevan et al. http://isg.cs.tcd.ie/cosulliv/Pubs/sdq-tog08.pdf
inline half3 rotateQuaternion(half3 v, half4 q)
{
	return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

// Credit: http://dev.theomader.com/dual-quaternion-skinning/
inline half3 transformPositionDQ(half3 position, half4 real, half4 dual )
{
    return rotateQuaternion(position, real) +
        2 * (real.w * dual.xyz - dual.w * real.xyz + cross(real.xyz, dual.xyz));
}
 
inline half3 transformNormalDQ(half3 normal, half4 real, half4 dual )
{
    return rotateQuaternion(normal, real);
}

inline void normalizeDQ(inout half4 real, inout half4 dual)
{
	half mag = length(real);
	real /= mag;
	dual /= mag;
}

void applySkin(inout appdata v)
{
	#ifdef UNITY_INSTANCING_ENABLED
		half4 anim = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimState);

		#if MORTON_CODE
			float4x4 uv = float4x4(
				decodeMorton(v.bone[0], anim[0]),
				decodeMorton(v.bone[0], anim[2]),
				decodeMorton(v.bone[1], anim[0]),
				decodeMorton(v.bone[1], anim[2]));
		#endif
		#if XY_INDEXING
			float4x4 uv = float4x4(
				v.bone[0], anim[0], 0, 0,
				v.bone[0], anim[2], 0, 0,
				v.bone[1], anim[0], 0, 0,
				v.bone[1], anim[2], 0, 0);
		#endif

		half w_bone0 = v.bone[2];
		half w_bone1 = 1 - w_bone0;

		half4 weights = half4(
			w_bone0 * anim[1],
			w_bone0 * anim[3],
			w_bone1 * anim[1],
			w_bone1 * anim[3]);

		half4 real = sampleRow(_Animation0, uv, weights);
		half4 dual = sampleRow(_Animation1, uv, weights);

		normalizeDQ(real, dual);

		v.vertex.xyz = transformPositionDQ(v.vertex.xyz, real, dual);
		v.normal = transformNormalDQ(v.normal, real, dual);
	#endif
}

void applySkinSimple(inout appdata v)
{
	#ifdef UNITY_INSTANCING_ENABLED
		half4 anim = UNITY_ACCESS_INSTANCED_PROP(Props, _AnimState);

		#if MORTON_CODE
			float2x4 uv = float2x4(
				decodeMorton(v.bone[0], anim[0]),
				decodeMorton(v.bone[1], anim[0]));
		#endif
		#if XY_INDEXING
			float2x4 uv = float2x4(
				v.bone[0], anim[0], 0, 0,
				v.bone[1], anim[0], 0, 0);
		#endif

		half2 weights = half2(v.bone[2], 1 - v.bone[2]);

		half4 real = sampleRowSimple(_Animation0, uv, weights);
		half4 dual = sampleRowSimple(_Animation1, uv, weights);

		// normalizeDQ(real, dual);

		v.vertex.xyz = transformPositionDQ(v.vertex.xyz, real, dual);
		// v.normal = transformNormalDQ(v.normal, real, dual);
	#endif
}