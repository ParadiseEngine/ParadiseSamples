using Paradise.Assets.Gltf;
using Paradise.Assets.Textures;
using Paradise.Rendering.Pbr;

namespace Paradise.Rendering.Sample;

/// <summary>Transcodes a glTF material's KTX2 images into the upload-ready payloads the renderer takes.</summary>
/// <remarks>The renderer reads no KTX2 (ParadiseEngine#397), so this native viewer decodes here. It is
/// kept out of the shared sources so the browser sample does not link libktx.</remarks>
internal static class GltfTextures
{
    /// <summary>Each slot is transcoded for a device granting <paramref name="supported"/>, in its colour
    /// space: sRGB for base color and emissive, a normal-map layout for normal, linear data otherwise.
    /// A slot without an image, or whose image transcodes to nothing, takes the default texture.</summary>
    public static PbrMaterialTextures Transcode(
        GltfMaterialData material, GltfImageData[] images, TextureCompressionFormats supported) => new()
    {
        BaseColor = Decode(material.BaseColorImage, images, CompressedTextureUsage.ColorSrgb, supported),
        MetallicRoughness = Decode(material.MetallicRoughnessImage, images, CompressedTextureUsage.LinearData, supported),
        Normal = Decode(material.NormalImage, images, CompressedTextureUsage.NormalMap, supported),
        Occlusion = Decode(material.OcclusionImage, images, CompressedTextureUsage.LinearData, supported),
        Emissive = Decode(material.EmissiveImage, images, CompressedTextureUsage.ColorSrgb, supported),
    };

    private static CompressedTextureData? Decode(
        int index, GltfImageData[] images, CompressedTextureUsage usage, TextureCompressionFormats supported)
    {
        if (index < 0 || images[index].Bytes.Length == 0) return null;
        var transcoded = Ktx2Transcoder.Transcode(images[index].Bytes, usage, supported);
        return transcoded.IsEmpty ? null : transcoded;
    }
}
