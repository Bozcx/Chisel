#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

matrix World;
matrix View;
matrix Projection;

float3 CameraPosition;
float3 LightDirection;
float3 LightColor;
float3 AmbientColor;

float shine;
float AlphaClip;
float Transparent;

texture MainTex;
sampler2D mainSampler = sampler_state
{
    Texture = (MainTex);
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};
texture SpecTex;
sampler2D specSampler = sampler_state
{
    Texture = (SpecTex);
    AddressU = Wrap;
    AddressV = Wrap;
};
texture NormalTex;
sampler2D normalSampler = sampler_state
{
    Texture = (NormalTex);
    AddressU = Wrap;
    AddressV = Wrap;
};

struct VSInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float3 Tangent : TANGENT0;
    float3 Binormal : BINORMAL0;
    float2 TexCoords : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoords : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
    float3 Normal : TEXCOORD2;
    float3 Tangent : TEXCOORD3;
    float3 Binormal : TEXCOORD4;
};

VSOutput MainVS(in VSInput input)
{
    VSOutput output = (VSOutput)0;

    float4 worldPos = mul(input.Position, World);
    output.Position = mul(mul(worldPos, View), Projection);
    output.WorldPos = worldPos.xyz;
    output.Normal = mul(input.Normal, (float3x3)World);
    output.Tangent = mul(input.Tangent, (float3x3)World);
    output.Binormal = mul(input.Binormal, (float3x3)World);
    output.TexCoords = input.TexCoords;

    return output;
}

float3 SkyColor(float3 dir)
{
    float3 ground = float3(0.08, 0.07, 0.06);
    float3 horizon = float3(0.55, 0.58, 0.62);
    float3 sky = float3(0.25, 0.4, 0.65);
    return dir.y > 0 ? lerp(horizon, sky, saturate(dir.y * 1.5)) : lerp(horizon, ground, saturate(-dir.y * 4));
}

float4 MainPS(VSOutput input) : COLOR
{
    float4 albedo = tex2D(mainSampler, input.TexCoords);
    if (AlphaClip > 0.5)
        clip(albedo.a - 0.01);

    float3 baseColor = pow(albedo.rgb, 2.2);
    float4 spec = tex2D(specSampler, input.TexCoords);

    float3 bump = tex2D(normalSampler, input.TexCoords).xyz * 2 - 1;
    float3 normal = normalize(normalize(input.Normal) + bump.x * input.Tangent - bump.y * input.Binormal);

    float3 viewDir = normalize(CameraPosition - input.WorldPos);
    float3 lightDir = normalize(LightDirection);

    float ndotl = saturate(dot(normal, lightDir));
    float3 diffuse = baseColor * (AmbientColor + LightColor * ndotl);

    float3 r = reflect(-lightDir, normal);
    float exponent = max(1, spec.g * shine * 64);
    float3 highlight = LightColor * spec.r * ndotl * pow(saturate(dot(r, viewDir)), exponent);

    float3 reflection = SkyColor(reflect(-viewDir, normal)) * spec.b;

    float3 color = diffuse + highlight + reflection;
    float alpha = Transparent > 0.5 ? albedo.a : 1;

    return float4(pow(saturate(color), 1 / 2.2), alpha);
}

technique Preview
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
