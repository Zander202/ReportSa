using Municipal.Web.Models;

namespace Municipal.Web.Services;

public class PhotoUploadService
{
    public const int MaxPhotos = 3;
    public const int MaxBytes = 5 * 1024 * 1024;

    public async Task<List<RequestPhoto>> PrepareAsync(IEnumerable<IFormFile> files, bool resolution = false)
    {
        var selected = files.Where(file => file.Length > 0).ToList();
        if (selected.Count > MaxPhotos) throw new ArgumentException("Choose up to three photos.");
        var photos = new List<RequestPhoto>();
        foreach (var file in selected)
        {
            if (file.Length > MaxBytes) throw new ArgumentException("Each photo must be 5 MB or smaller.");
            using var content = new MemoryStream();
            await using var input = file.OpenReadStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                if (content.Length + read > MaxBytes) throw new ArgumentException("Each photo must be 5 MB or smaller.");
                await content.WriteAsync(buffer.AsMemory(0, read));
            }
            var bytes = content.ToArray();
            var type = DetectType(bytes) ?? throw new ArgumentException("Photos must be JPEG, PNG or WebP images.");
            var name = Path.GetFileName(file.FileName);
            photos.Add(new RequestPhoto
            {
                FileName = name.Length > 120 ? name[..120] : name,
                ContentType = type, Content = bytes, IsResolution = resolution, UploadedAt = DateTimeOffset.UtcNow
            });
        }
        return photos;
    }

    public static string? DetectType(byte[] bytes)
    {
        if (bytes.Length >= 12 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff
            && bytes[^2] == 0xff && bytes[^1] == 0xd9) return "image/jpeg";
        if (bytes.Length >= 33 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return "image/png";
        if (bytes.Length >= 20 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
