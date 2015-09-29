// Copyright (c) 2010-2013 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#ifdef USE_TEXTURE
#define MyRS1 "RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), " \
              "CBV(b0, visibility = SHADER_VISIBILITY_VERTEX), " \
              "DescriptorTable( CBV(b1, numDescriptors = 1), " \
							  " visibility = SHADER_VISIBILITY_VERTEX), " \
              "DescriptorTable( SRV(t0, numDescriptors = 1), " \
							  " visibility = SHADER_VISIBILITY_PIXEL), " \
              "DescriptorTable( Sampler(s0, numDescriptors = 1), " \
							  " visibility = SHADER_VISIBILITY_PIXEL), " \

#else
#define MyRS1 "RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), " \
              "CBV(b0, visibility = SHADER_VISIBILITY_VERTEX), " \
              "DescriptorTable( CBV(b1, numDescriptors = 1), " \
							  " visibility = SHADER_VISIBILITY_VERTEX), " \

#endif
struct VS_IN
{
	float3 pos : POSITION;
	float3 normal : NORMAL;
	float2 tex : TEXCOORD;
#ifdef USE_INSTANCES
	float3 offset : OFFSET;
#endif
};

struct PS_IN
{
	float4 pos : SV_POSITION;
	float3 normal : TEXCOORD0;
	float2 tex : TEXCOORD1;
};

cbuffer worldMatrix : register(b0)
{
	float4x4 World;
};

cbuffer viewProjMatrix : register(b1)
{
	float4x4 ViewProj;
};

#ifdef USE_TEXTURE
Texture2D<float4> tex : register(t0);
sampler sampl : register(s0);
#endif


PS_IN VS( VS_IN input )
{
	PS_IN output = (PS_IN)0;
	
#ifdef USE_INSTANCES
	output.pos = mul(float4(input.pos + input.offset, 1.0f), World);
#else
	output.pos = mul(float4(input.pos, 1.0f), World);
#endif
	output.pos = mul(output.pos, ViewProj);
	output.tex = input.tex;
	
	return output;
}

float4 PS( PS_IN input ) : SV_Target
{
#ifdef USE_TEXTURE
	return tex.Sample(sampl, input.tex);
#else
	return float4(input.tex, 1, 1);
#endif
}
