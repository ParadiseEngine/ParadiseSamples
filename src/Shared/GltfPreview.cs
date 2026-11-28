using System;
using System.Numerics;
using Paradise.Assets.Gltf;
using Paradise.Rendering.Pbr;

namespace Paradise.Rendering.Sample;

/// <summary>Previews a source GLB through the renderer's runtime inputs: geometry spans and
/// engine-owned material descriptions.</summary>
/// <remarks>The renderer takes no glTF types, so the samples' model options map a decoded scene
/// here. Each glTF primitive keeps its own vertex stream; cooking to a MeshBlob would upload the
/// whole shared stream once per draw.</remarks>
internal static class GltfPreview
{
    /// <summary>The engine description of material <paramref name="index"/> of <paramref name="asset"/>.</summary>
    /// <remarks>glTF's <c>doubleSided</c> becomes back-face culling, except for a material a mirrored
    /// node draws: winding is decided after the model transform, so culling would show that node's
    /// inside. The engine's cook flips such triangles; this preview uploads them as they are
    /// (ParadiseEngine#387).</remarks>
    public static PbrMaterialDesc Describe(GltfAsset asset, int index)
    {
        var material = asset.Materials[index];
        return Describe(material) with { DoubleSided = material.DoubleSided || DrawnMirrored(asset, index) };
    }

    private static PbrMaterialDesc Describe(GltfMaterialData material) => new()
    {
        Name = material.Name,
        BaseColorFactor = material.BaseColorFactor,
        MetallicFactor = material.MetallicFactor,
        RoughnessFactor = material.RoughnessFactor,
        EmissiveFactor = material.EmissiveFactor,
        NormalScale = material.NormalScale,
        OcclusionStrength = material.OcclusionStrength,
        TransmissionFactor = material.TransmissionFactor,
        AlphaMode = material.AlphaMode switch
        {
            GltfAlphaMode.Opaque => PbrAlphaMode.Opaque,
            GltfAlphaMode.Mask => PbrAlphaMode.Mask,
            GltfAlphaMode.Blend => PbrAlphaMode.Blend,
            _ => throw new ArgumentOutOfRangeException(nameof(material), material.AlphaMode, "Unknown glTF alpha mode."),
        },
        BaseColorUvTransform = new PbrUvTransform(
            material.BaseColorUvTransform.Offset, material.BaseColorUvTransform.Scale, material.BaseColorUvTransform.Rotation),
    };

    /// <summary>Uploads every mesh of <paramref name="asset"/>, indexed like <see cref="GltfAsset.Meshes"/>.
    /// <paramref name="materialIds"/> maps glTF material indices; primitives without a material get a
    /// neutral grey.</summary>
    public static PbrMesh[] UploadMeshes(PbrRenderer pbr, GltfAsset asset, int[] materialIds)
    {
        var fallbackMaterial = -1;
        var meshes = new PbrMesh[asset.Meshes.Length];
        for (var m = 0; m < meshes.Length; m++)
        {
            var sources = asset.Meshes[m].Primitives;
            var primitives = new PbrPrimitive[sources.Length];
            for (var p = 0; p < primitives.Length; p++)
            {
                var source = sources[p];
                var materialId = source.MaterialIndex >= 0
                    ? materialIds[source.MaterialIndex]
                    : (fallbackMaterial >= 0 ? fallbackMaterial : fallbackMaterial = pbr.Materials.AddDefaultMaterial(new Vector4(0.8f, 0.8f, 0.8f, 1f)));
                primitives[p] = pbr.UploadPrimitive(source.Vertices, source.Indices, materialId);
            }
            meshes[m] = new PbrMesh(primitives);
        }
        return meshes;
    }

    private static bool DrawnMirrored(GltfAsset asset, int materialIndex)
    {
        foreach (var instance in asset.Instances)
        {
            if (instance.WorldTransform.GetDeterminant() >= 0f) continue;
            foreach (var primitive in asset.Meshes[instance.MeshIndex].Primitives)
            {
                if (primitive.MaterialIndex == materialIndex) return true;
            }
        }
        return false;
    }
}
