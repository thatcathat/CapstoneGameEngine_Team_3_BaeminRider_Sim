#ifndef DELIVERY_RIDER_CURVED_WORLD_INCLUDED
#define DELIVERY_RIDER_CURVED_WORLD_INCLUDED

// Set every frame by CurvedWorld.cs. Zero curvature (edit mode, other scenes) leaves meshes untouched.
float3 _CurvedWorldPivot;
float _CurvedWorldCurvature;

// The rendered world drops with the squared horizontal distance from the pivot. Rendering only: physics stays flat.
// Keep in step with CurvedWorld.Bend.
float3 CurvedWorldBendObject(float3 positionOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float2 offset = positionWS.xz - _CurvedWorldPivot.xz;
    float drop = dot(offset, offset) * _CurvedWorldCurvature;
    return positionOS - TransformWorldToObjectDir(float3(0.0, drop, 0.0), false);
}

#endif
