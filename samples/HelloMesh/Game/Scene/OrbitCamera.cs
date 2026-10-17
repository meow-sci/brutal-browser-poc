using Brutal.Numerics;

namespace HelloMesh.Scene;

/// <summary>
/// Orbit camera around a home target: yaw/pitch/distance plus a pan offset in the view plane.
/// Matrices use BRUTAL's row-vector convention and a Vulkan clip space (depth 0..1, Y down).
/// </summary>
public sealed class OrbitCamera
{
    private const float FieldOfView = MathF.PI / 4f;
    private const float MaxPitch = 89f;
    private const float HomeYaw = 30f;
    private const float HomePitch = 15f;

    private readonly float3 _homeTarget;
    private readonly float _homeDistance;

    public float Yaw { get; private set; } = HomeYaw;
    public float Pitch { get; private set; } = HomePitch;
    public float Distance { get; private set; }
    public float2 Pan { get; private set; }

    /// <summary>Incremented whenever yaw, pitch, distance or pan changes.</summary>
    public int Version { get; private set; }

    /// <param name="target">Point the camera orbits.</param>
    /// <param name="radius">Radius of a sphere around <paramref name="target"/> that must fit in view at the home distance.</param>
    public OrbitCamera(float3 target, float radius)
    {
        _homeTarget = target;
        _homeDistance = radius / MathF.Sin(FieldOfView / 2f) * 0.9f;
        Distance = _homeDistance;
    }

    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw = (Yaw + deltaYaw) % 360f;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);
        Version++;
    }

    /// <summary>Multiplies the distance by <paramref name="factor"/> (below 1 zooms in).</summary>
    public void Zoom(float factor)
    {
        Distance = Math.Clamp(Distance * factor, _homeDistance * 0.1f, _homeDistance * 8f);
        Version++;
    }

    /// <summary>Moves the target by a fraction of the current distance along the view-plane right and up axes.</summary>
    public void PanBy(float right, float up)
    {
        Pan += new float2(right, up) * Distance;
        Version++;
    }

    public void Reset()
    {
        Yaw = HomeYaw;
        Pitch = HomePitch;
        Distance = _homeDistance;
        Pan = float2.Zero;
        Version++;
    }

    public float4x4 GetView()
    {
        var (right, up, towardEye) = Axes();
        var target = _homeTarget + right * Pan.X + up * Pan.Y;
        return float4x4.CreateLookAt(target + towardEye * Distance, target, float3.UnitY);
    }

    public float4x4 GetProjection(float aspect)
    {
        var projection = float4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, _homeDistance * 0.02f, _homeDistance * 50f);
        projection.Y.Y = -projection.Y.Y;
        return projection;
    }

    private (float3 Right, float3 Up, float3 TowardEye) Axes()
    {
        var (yaw, pitch) = (Yaw * MathF.PI / 180f, Pitch * MathF.PI / 180f);
        var towardEye = new float3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
        var right = float3.Normalize(float3.Cross(float3.UnitY, towardEye));
        return (right, float3.Cross(towardEye, right), towardEye);
    }
}
