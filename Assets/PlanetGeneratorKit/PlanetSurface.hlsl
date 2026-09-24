#ifndef MYHLSLINCLUDE_INCLUDED
#define MYHLSLINCLUDE_INCLUDED
float hash21(float2 p)
{
    p = frac(p * float2(123.34, 345.45));
    p += dot(p, p + 34.23);

    return frac(p.x * p.y);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float a = hash21(i);
    float b = hash21(i + float2(1,0));
    float c = hash21(i + float2(0,1));
    float d = hash21(i + float2(1,1));

    float2 u = f * f * (3.0 - 2.0 * f);

    return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
}

float fbm(float2 p, int Octaves)
{
    float v = 0;
    float a = 0.5;

    for(int i=0;i<Octaves;i++)
    {
        v += noise(p)*a;
        p *= 2.0;
        a *= 0.5;
    }

    return v;
}

float continentNoise(float2 p, int Octaves)
{
    float continents = fbm(p * 0.5, Octaves);
    float islands = fbm(p * 3.0, Octaves) * 0.25;
    return continents + islands;
}

void PlanetSurface_float(
    float3 Position,
    float3 Normal,
    float BlendSharpness,
    float Scale,
    float LandThreshold,
    float EdgeSmoothness,
    float Octaves,
    out float Out)
{
    float3 n = abs(Normal);
    n = pow(n, BlendSharpness);
    n /= (n.x + n.y + n.z);

    float2 offsetX = float2(7.35, 3.72);
    float2 offsetY = float2(9.41, 1.19);
    float2 offsetZ = float2(8.23, 6.09);

    float2 uvX = (Position.yz + offsetX) * Scale;
    float2 uvY = (Position.xz + offsetY) * Scale;
    float2 uvZ = (Position.xy + offsetZ) * Scale;

    float noiseX = continentNoise(uvX, (int)Octaves);
    float noiseY = continentNoise(uvY, (int)Octaves);
    float noiseZ = continentNoise(uvZ, (int)Octaves);

    float height = noiseX * n.x + noiseY * n.y + noiseZ * n.z;
    float land = smoothstep(LandThreshold - EdgeSmoothness, LandThreshold + EdgeSmoothness, height);

    Out = land;
}
#endif