namespace SwissWear.Web.Services;

public interface IStorageService
{
    Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string fileName, CancellationToken cancellationToken = default);
    Task DeleteAsync(string fileName, CancellationToken cancellationToken = default);
    Task<Uri> GetUriAsync(string fileName, CancellationToken cancellationToken = default);
}
