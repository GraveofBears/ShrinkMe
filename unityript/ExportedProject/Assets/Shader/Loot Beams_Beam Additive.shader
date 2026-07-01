Shader "Loot Beams/Beam Additive" {
	Properties {
		[Header(Core Options)] _TintColor ("Color", Vector) = (1,1,1,1)
		_Glow ("Glow", Float) = 1
		_GlobalColor ("Global Color", Range(0, 1)) = 1
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "white" {}
		_BotFadeRange ("Bot Fade Range", Range(0, 1)) = 0
		_BotFadeSmooth ("Bot Fade Smooth", Range(0, 1)) = 0
		[Header(Particle Options)] [Toggle] _ForceParticle1 ("Force Particle", Float) = 0
		[Header(Mask Options)] [Toggle] _BlendMask ("Blend Mask", Float) = 0
		[NoScaleOffset] _MaskTex ("Mask Texture", 2D) = "white" {}
		_MaskValue ("Mask Value", Float) = 1
		_MaskScale ("Mask Scale", Float) = 1
		_MaskScroll ("Mask Scroll", Float) = 1
		[Header(Ripple Options)] [Toggle] _UVDistortion ("UV Distortion", Float) = 0
		[NoScaleOffset] _RippleTexture ("Ripple Texture", 2D) = "white" {}
		_RippleValue ("Ripple Value", Float) = 0.1
		_RippleScale ("Ripple Scale", Float) = 1
		_RippleScroll ("Ripple Scroll", Float) = 1
	}
	//DummyShaderTextExporter
	SubShader{
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			float4x4 unity_ObjectToWorld;
			float4x4 unity_MatrixVP;
			float4 _MainTex_ST;

			struct Vertex_Stage_Input
			{
				float4 pos : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Vertex_Stage_Output
			{
				float2 uv : TEXCOORD0;
				float4 pos : SV_POSITION;
			};

			Vertex_Stage_Output vert(Vertex_Stage_Input input)
			{
				Vertex_Stage_Output output;
				output.uv = (input.uv.xy * _MainTex_ST.xy) + _MainTex_ST.zw;
				output.pos = mul(unity_MatrixVP, mul(unity_ObjectToWorld, input.pos));
				return output;
			}

			Texture2D<float4> _MainTex;
			SamplerState sampler_MainTex;

			struct Fragment_Stage_Input
			{
				float2 uv : TEXCOORD0;
			};

			float4 frag(Fragment_Stage_Input input) : SV_TARGET
			{
				return _MainTex.Sample(sampler_MainTex, input.uv.xy);
			}

			ENDHLSL
		}
	}
}