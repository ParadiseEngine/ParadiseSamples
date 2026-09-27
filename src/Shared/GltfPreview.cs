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
    public static PbrMaterialDesc Describe(GltfMaterialData material) => new()
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

    /// <summary>The material's KTX2 images; a slot without an image takes the default texture.</summary>
    public static PbrMaterialTextures Textures(GltfMaterialData material, GltfImageData[] images) => new()
    {
        BaseColor = Image(material.BaseColorImage, images),
        MetallicRoughness = Image(material.MetallicRoughnessImage, images),
        Normal = Image(material.NormalImage, images),
        Occlusion = Image(material.OcclusionImage, images),
        Emissive = Image(material.EmissiveImage, images),
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

    private static ReadOnlyMemory<byte> Image(int index, GltfImageData[] images) =>
        index >= 0 ? images[index].Bytes : ReadOnlyMemory<byte>.Empty;
}
