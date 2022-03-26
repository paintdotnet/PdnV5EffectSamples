//********************************************************* 
// 
// Copyright (c) Microsoft. All rights reserved. 
// This code is licensed under the MIT License (MIT). 
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF 
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY 
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR 
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT. 
// 
//*********************************************************

// Custom effects using pixel shaders should use HLSL helper functions defined in
// d2d1effecthelpers.hlsli to make use of effect shader linking.
// See also: https://docs.microsoft.com/en-us/windows/win32/direct2d/hlsl-helpers
#define D2D_INPUT_COUNT 1           // The pixel shader takes 1 input texture.
#define D2D_INPUT0_COMPLEX          // The first input is sampled in a complex manner: to calculate the output of a pixel,
                                    // the shader samples more than just the corresponding input coordinate.
#define D2D_REQUIRES_SCENE_POSITION // The pixel shader requires the SCENE_POSITION input.

// Note that the custom build step must provide the correct path to find d2d1effecthelpers.hlsli when calling fxc.exe.
#include "d2d1effecthelpers.hlsli"

float size;
float frequency;
float phase;
float amplitude;
float spread;
float2 center;

D2D_PS_ENTRY(main)
{
    float2 toPixel = D2DGetScenePosition().xy - center;

    // Scale distance such that the ripple's displacement decays to 0 at the requested size (in pixels)
    float distance = length(toPixel * (1.0f / size));
    float2 direction = normalize(toPixel);

    float2 wave;
    sincos(frequency * distance + phase, wave.x, wave.y);

    // Clamp the falloff value so that the ripple does not extend beyond the limit.
    // Spread controls how quickly the ripple decays, up to the limit.
    float falloff = saturate(1.0f - distance);
    falloff = pow(falloff, 1.0f / spread);

    // Calculate new mapping coordinates based on the frequency, center, and amplitude.
    float2 inputOffset = (wave.x * falloff * amplitude) * direction;
    float lighting = lerp(1.0f, 1.0f + wave.x * falloff * 0.2f, saturate(amplitude / 20.0f));

    // Resample the image based on the new coordinates.
    float4 color = D2DSampleInputAtOffset(0, inputOffset);
    color.rgb *= lighting;

    return color;
}